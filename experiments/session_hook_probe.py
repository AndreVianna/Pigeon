"""Run one isolated Claude startup and prompt-hook probe with disposable Pigeon data.

No existing sessions, transcripts or global configuration are accessed or modified.
The process uses a new nonpersistent session and a temporary service and plugin copy.
"""
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import uuid

from polling_e2e import JsonProcess, ROOT

PIGEON = ROOT / 'src' / 'Pigeon.Cli' / 'bin' / 'Debug' / 'net11.0' / 'pigeon.exe'
CLAUDE = shutil.which('claude.exe') or str(Path.home() / '.local' / 'bin' / 'claude.exe')
PLUGIN = ROOT / 'integrations' / 'claude-plugin'


def main():
    session_id = str(uuid.uuid4())
    with socket.socket() as socket_probe:
        socket_probe.bind(('127.0.0.1', 0))
        port = str(socket_probe.getsockname()[1])
    with tempfile.TemporaryDirectory(prefix='.pigeon-hook-probe-', dir=ROOT) as temp:
        directory = Path(temp)
        plugin = directory / 'plugin'
        shutil.copytree(PLUGIN, plugin)
        if '--without-prompt-hook' in sys.argv:
            hook_path = plugin / 'hooks' / 'hooks.json'
            hooks = json.loads(hook_path.read_text(encoding='utf-8'))
            hooks['hooks'].pop('UserPromptSubmit', None)
            hook_path.write_text(json.dumps(hooks), encoding='utf-8')
        env = os.environ.copy()
        env.pop('CLAUDE_CODE_SESSION_ID', None)
        env['PIGEON_PORT'] = port
        env['PIGEON_DATABASE'] = str(directory / 'data.db')
        env['PATH'] = str(PIGEON.parent) + os.pathsep + env.get('PATH', '')
        server = subprocess.Popen([str(PIGEON), 'start', '--foreground'], cwd=ROOT, env=env,
                                  stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        claude = None
        try:
            def cli(*args):
                return subprocess.run([str(PIGEON), *args], cwd=ROOT, env=env, capture_output=True, text=True, timeout=12)
            for _ in range(50):
                if cli('status').returncode == 0:
                    break
                if server.poll() is not None:
                    raise RuntimeError('Disposable service did not start')
                time.sleep(.1)
            else:
                raise RuntimeError('Disposable service did not become ready')
            for args in [('topics', 'create', 'hook-test'), ('topics', 'join', 'hook-test', 'claude-code', session_id)]:
                result = cli(*args)
                if result.returncode:
                    raise RuntimeError('Disposable route setup failed: ' + result.stderr)
            environment_evidence = directory / 'bridge-environment.json'
            wrapper = directory / 'bridge-wrapper.py'
            wrapper.write_text('import json, os, subprocess, sys\nfrom pathlib import Path\n'
                + 'v = os.environ.get("CLAUDE_CODE_SESSION_ID")\n'
                + 'Path(' + repr(str(environment_evidence)) + ').write_text(json.dumps({"present": bool(v), "matches": v == ' + repr(session_id) + '}))\n'
                + ('os.environ.pop("CLAUDE_CODE_SESSION_ID", None)\n' if '--strip-bridge-session' in sys.argv else '')
                + 'sys.exit(subprocess.call([' + repr(str(PIGEON)) + ', "mcp", ' + repr(port) + ']))\n', encoding='utf-8')
            config = {'mcpServers': {'plugin:pigeon:pigeon': {'command': sys.executable, 'args': [str(wrapper)], 'env': {'PIGEON_RUNTIME': 'claude-code'}}}}
            args = [CLAUDE, '--print', '--verbose', '--input-format', 'stream-json', '--output-format', 'stream-json',
                    '--strict-mcp-config', '--mcp-config', json.dumps(config), '--setting-sources', '',
                    '--plugin-dir', str(plugin), '--no-session-persistence', '--session-id', session_id,
                    '--tools', '', '--permission-mode', 'dontAsk']
            for cycle in (1, 2):
                claude = JsonProcess(args, str(directory), env)
                automatic = False
                for _ in range(8):
                    early = cli('status').stdout
                    if any(session_id in line and '| mcp |' in line and '| restore: bound' in line for line in early.splitlines()):
                        automatic = True
                        break
                    time.sleep(.5)
                print(f'Cycle {cycle}: bridge bound before any user prompt:', automatic, flush=True)
                claude.send({'type': 'user', 'message': {'role': 'user', 'content': 'This is an isolated hook test. Reply TEST_READY without calling any tools.'}})
                result = claude.wait(lambda e: e.get('type') == 'result', timeout=50)
                print(f'Cycle {cycle}: isolated Claude prompt completed:', not result.get('is_error'), flush=True)
                print('Hook event metadata:', [{key: e[key] for key in ('subtype', 'hook_name', 'hook_event', 'outcome', 'exit_code') if key in e} for e in claude.observed if e.get('type') == 'system' and str(e.get('subtype', '')).startswith('hook_')], flush=True)
                for _ in range(8):
                    status = cli('status').stdout
                    if any(session_id in line and '| mcp |' in line and '| restore: bound' in line for line in status.splitlines()):
                        print(f'Cycle {cycle}: bridge bound the matching session:', True, flush=True)
                        break
                    time.sleep(1)
                else:
                    print(f'Cycle {cycle}: bridge bound the matching session:', False, flush=True)
                    print('Isolated status:', status, flush=True)
                    if environment_evidence.exists():
                        print('Bridge session environment:', environment_evidence.read_text(encoding='utf-8'), flush=True)
                    else:
                        print('Bridge wrapper did not start.', flush=True)
                    return 2
                if environment_evidence.exists():
                    print(f'Cycle {cycle}: bridge session environment:', environment_evidence.read_text(encoding='utf-8'), flush=True)
                claude.close()
                claude = None
                if cycle == 1:
                    for _ in range(22):
                        if session_id not in cli('status').stdout:
                            break
                        time.sleep(1)
                    else:
                        raise RuntimeError('First test bridge did not disconnect')
            print('Second process restored the same test identity; this is not a host --resume test.', flush=True)
            return 0
        finally:
            if claude is not None:
                claude.close()
            if server.poll() is None:
                server.stdin.write(b'\n')
                server.stdin.flush()
                try:
                    server.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    server.terminate()
                    server.wait(timeout=5)


if __name__ == '__main__':
    raise SystemExit(main())
