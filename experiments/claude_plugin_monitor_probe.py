"""Approved isolated alpha.14 plugin monitor probe; never targets existing sessions.

Creates a temporary service/database and a single nonpersistent Claude session.
The Claude process receives only a synthetic topic, identity, and test message.
It uses a test-specific MCP config and a session-only plugin directory.
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

from polling_e2e import Claude, JsonProcess, ROOT, query

PIGEON = ROOT / 'src' / 'Pigeon.Cli' / 'bin' / 'Debug' / 'net11.0' / 'pigeon.exe'
PLUGIN = ROOT / 'integrations' / 'claude-plugin'
CLAUDE = shutil.which('claude.exe') or str(Path.home() / '.local' / 'bin' / 'claude.exe')


def main():
    session = str(uuid.uuid4())
    agent = 'claude-isolated-monitor-' + uuid.uuid4().hex[:12]
    marker = 'PIGEON_MONITOR_' + uuid.uuid4().hex
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = str(sock.getsockname()[1])
    # All created files and cleanup stay inside the Pigeon workspace.
    with tempfile.TemporaryDirectory(prefix='.pigeon-monitor-probe-', dir=ROOT) as temp:
        directory = Path(temp)
        plugin = directory / 'plugin'
        shutil.copytree(PLUGIN, plugin)
        marker_file = directory / 'monitor-started.txt'
        wrapper = directory / 'monitor-wrapper.py'
        wrapper.write_text('import os\nfrom pathlib import Path\n'
            + 'Path(' + repr(str(marker_file)) + ').write_text("session-present=" + str(bool(os.environ.get("CLAUDE_CODE_SESSION_ID"))))\n'
            + 'os.execv(' + repr(str(PIGEON)) + ', [' + repr(str(PIGEON)) + ', "messages", "wait", "--claude"])\n', encoding='utf-8')
        manifest_path = plugin / '.claude-plugin' / 'plugin.json'
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
        manifest['experimental']['monitors'][0]['command'] = '"' + sys.executable + '" "' + str(wrapper) + '"'
        if '--always' in sys.argv:
            manifest['experimental']['monitors'][0]['when'] = 'always'
        manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
        env = os.environ.copy()
        env.pop('CLAUDE_CODE_SESSION_ID', None)
        env['PIGEON_PORT'] = port
        env['PIGEON_DATABASE'] = str(directory / 'messages.db')
        env['PATH'] = str(PIGEON.parent) + os.pathsep + env.get('PATH', '')
        service = subprocess.Popen([str(PIGEON), 'start', '--foreground'], cwd=ROOT, env=env,
                                   stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        process = None
        try:
            def cli(*args):
                return subprocess.run([str(PIGEON), *args], cwd=ROOT, env=env, capture_output=True, text=True, timeout=12)
            for _ in range(50):
                if cli('status').returncode == 0:
                    break
                if service.poll() is not None:
                    raise RuntimeError('Disposable service exited during startup')
                time.sleep(.1)
            else:
                raise RuntimeError('Disposable service did not start')
            for command in [('topics', 'create', 'probe'), ('topics', 'subscribe', 'probe', 'synthetic-sender', 'codex', 'synthetic-session')]:
                result = cli(*command)
                if result.returncode:
                    raise RuntimeError('Test topic setup failed')
            config = {'mcpServers': {'pigeon_test': {'command': str(PIGEON), 'args': ['mcp', port]}}}
            args = [CLAUDE, '--print', '--verbose', '--input-format', 'stream-json', '--output-format', 'stream-json',
                    '--strict-mcp-config', '--mcp-config', json.dumps(config), '--setting-sources', '',
                    '--plugin-dir', str(plugin), '--no-session-persistence', '--session-id', session,
                    '--tools', 'Skill', '--permission-mode', 'dontAsk', '--allowedTools',
                    'Skill(pigeon:connect),mcp__pigeon_test__pigeon_topic_join,mcp__pigeon_test__pigeon_wake_confirm,mcp__pigeon_test__pigeon_receive,mcp__pigeon_test__pigeon_acknowledge,mcp__pigeon_test__pigeon_wake_complete']
            process = JsonProcess(args, str(directory), env)
            process.send({'type': 'user', 'message': {'role': 'user', 'content':
                '/pigeon:connect For this isolated synthetic test, join topic probe as ' + agent +
                ', runtime claude-code, session ID ' + session + '. Invoke the pigeon:connect skill, then report ready. '
                'When a wake event arrives, confirm the generation, receive and acknowledge the synthetic message, and complete the wake. '
                'Do not run shell commands or change files.'}})
            try:
                event = process.wait(lambda e: e.get('type') == 'result', timeout=45)
            except TimeoutError:
                print('Observed event types:', sorted({str(e.get('type', 'unknown')) for e in process.observed}), flush=True)
                print('Sanitized diagnostic categories:', sorted(process.diagnostics), flush=True)
                raise
            if event.get('is_error'):
                raise RuntimeError('Claude could not complete its setup turn')
            print('Isolated Claude setup turn completed.', flush=True)
            used_tools = []
            for observed in process.observed:
                message = observed.get('message')
                if not isinstance(message, dict):
                    continue
                for block in message.get('content', []):
                    if isinstance(block, dict) and block.get('type') == 'tool_use':
                        used_tools.append(block.get('name', 'unknown'))
            print('Test tool names observed:', used_tools, flush=True)
            print('Sanitized diagnostic categories:', sorted(process.diagnostics), flush=True)
            texts = [block.get('text', '') for observed in process.observed
                     if isinstance(observed.get('message'), dict)
                     for block in observed['message'].get('content', [])
                     if isinstance(block, dict) and block.get('type') == 'text']
            print('Isolated test response:', ' '.join(texts)[-1800:], flush=True)
            time.sleep(4)
            status = cli('status').stdout
            bridge = any(agent in line and ' | mcp | ' in line for line in status.splitlines())
            monitor = any(agent in line and ' | claude-monitor | ' in line for line in status.splitlines())
            print('Matching MCP bridge visible:', bridge, flush=True)
            print('Plugin monitor heartbeat visible:', monitor, flush=True)
            print('Plugin monitor process launched:', marker_file.exists(), flush=True)
            if marker_file.exists():
                print('Monitor session environment:', marker_file.read_text(encoding='utf-8'), flush=True)
            if not bridge or not monitor:
                print('Monitor not proven active; stopping without sending a test message.', flush=True)
                return 2
            if cli('messages', 'send', 'synthetic-sender', 'probe', marker).returncode:
                raise RuntimeError('Synthetic send failed')
            print('One synthetic message stored.', flush=True)
            deadline = time.monotonic() + 50
            while time.monotonic() < deadline:
                rows = query(env['PIGEON_DATABASE'], 'SELECT status FROM messages WHERE recipient=? AND body=?', agent, marker)
                if rows and rows[0][0] == 'acknowledged':
                    print('Agent acknowledgment observed: True', flush=True)
                    return 0
                time.sleep(1)
            print('Agent acknowledgment observed: False', flush=True)
            return 3
        finally:
            if process is not None:
                process.close()
            if service.poll() is None:
                service.stdin.write(b'\n')
                service.stdin.flush()
                try:
                    service.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    service.terminate()
                    service.wait(timeout=5)


if __name__ == '__main__':
    raise SystemExit(main())
