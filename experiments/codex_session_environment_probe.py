"""Inspect only a new isolated Codex thread's MCP session-ID environment.

Uses a temporary service and test-owned files, never enumerates other sessions,
disables hooks for this probe, and reports only presence/matching booleans.
"""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time

from codex_queue_probe import find_codex
from codex_restart_probe import Server

ROOT = Path(__file__).resolve().parents[1]
PIGEON = ROOT / 'src' / 'Pigeon.Cli' / 'bin' / 'Debug' / 'net11.0' / 'pigeon.exe'


def main():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = str(sock.getsockname()[1])
    with tempfile.TemporaryDirectory(prefix='.pigeon-codex-env-', dir=ROOT) as temp:
        directory = Path(temp)
        env = os.environ.copy()
        env['PIGEON_PORT'] = port
        env['PIGEON_DATABASE'] = str(directory / 'data.db')
        env['CODEX_APP_RUNTIME_CLI_PATH'] = find_codex()
        service = subprocess.Popen([str(PIGEON), 'start', '--foreground'], cwd=ROOT, env=env,
                                   stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        server = None
        try:
            def cli(*args):
                return subprocess.run([str(PIGEON), *args], cwd=ROOT, env=env, capture_output=True, text=True, timeout=12)
            for _ in range(50):
                if cli('status').returncode == 0:
                    break
                if service.poll() is not None:
                    raise RuntimeError('Disposable service exited')
                time.sleep(.1)
            else:
                raise RuntimeError('Disposable service did not start')
            if cli('topics', 'create', 'hook-test').returncode:
                raise RuntimeError('Could not create test topic')
            evidence = directory / 'env.json'
            wrapper = directory / 'mcp-wrapper.py'
            wrapper.write_text('import json, os, subprocess, sys\nfrom pathlib import Path\n'
                + 'Path(' + repr(str(evidence)) + ').write_text(json.dumps({k:os.environ.get(k) for k in ("CODEX_THREAD_ID", "CODEX_SESSION_ID")}))\n'
                + 'sys.exit(subprocess.call([' + repr(str(PIGEON)) + ', "mcp", ' + repr(port) + ', "--codex-queue"]))\n', encoding='utf-8')
            inline = ('{command=' + json.dumps(sys.executable) + ',args=[' + json.dumps(str(wrapper))
                      + '],env={PIGEON_RUNTIME="codex",CODEX_APP_RUNTIME_CLI_PATH=' + json.dumps(find_codex()) + '},required=true}')
            # Do not let this calling chat's IDs be mistaken for the new thread's IDs.
            os.environ.pop('CODEX_THREAD_ID', None)
            os.environ.pop('CODEX_SESSION_ID', None)
            server = Server(str(directory), ['-c', 'features.hooks=false', '-c', 'mcp_servers={pigeon_test=' + inline + '}'])
            thread = server.request('thread/start', {'cwd': str(directory), 'ephemeral': True, 'sandbox': 'read-only'})['thread']['id']
            if cli('topics', 'join', 'hook-test', 'codex', thread).returncode:
                raise RuntimeError('Could not save test route')
            turn = server.request('turn/start', {'threadId': thread, 'input': [{'type': 'text', 'text': 'This is an isolated test. Reply READY only. Do not call tools or access files.'}]})['turn']['id']
            done = server.wait(lambda e: e.get('method') == 'turn/completed' and e.get('params', {}).get('turn', {}).get('id') == turn, seconds=60)
            print('Isolated test turn completed:', done.get('params', {}).get('turn', {}).get('status') == 'completed', flush=True)
            if evidence.exists():
                values = json.loads(evidence.read_text(encoding='utf-8'))
                for key in ('CODEX_THREAD_ID', 'CODEX_SESSION_ID'):
                    print(key + ': present=' + str(bool(values.get(key))) + ', matches-test-thread=' + str(values.get(key) == thread), flush=True)
            else:
                print('Test MCP environment wrapper did not start.', flush=True)
            for _ in range(8):
                status = cli('status').stdout
                if any(thread in line and '| codex-queue |' in line and '| restore: bound' in line for line in status.splitlines()):
                    print('Bridge auto-restored the test thread:', True, flush=True)
                    return 0
                time.sleep(1)
            print('Bridge auto-restored the test thread:', False, flush=True)
            return 2
        finally:
            if server is not None:
                server.close()
            if service.poll() is None:
                service.stdin.write(b'\n'); service.stdin.flush()
                try:
                    service.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    service.terminate(); service.wait(timeout=5)


if __name__ == '__main__':
    raise SystemExit(main())
