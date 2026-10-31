"""Prepare or inspect a disposable Pigeon-to-Claude channel test.

Preparation starts no service or agent. Running the printed steps requires the
user's prior approval. Only the newly generated session ID and synthetic marker
are used; no existing sessions or global configuration are accessed.
"""
import argparse
from contextlib import closing
import json
import os
from pathlib import Path
import re
import shutil
import socket
import sqlite3
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[1]
PIGEON = ROOT / 'src' / 'Pigeon.Cli' / 'bin' / 'Debug' / 'net11.0' / 'pigeon.exe'


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def prepare():
    claude = shutil.which('claude.exe') or str(Path.home() / '.local' / 'bin' / 'claude.exe')
    if not PIGEON.is_file() or not Path(claude).is_file():
        raise SystemExit('Build Pigeon and install Claude Code before preparing this fixture.')
    directory = Path(tempfile.mkdtemp(prefix='pigeon-wake-test-'))
    session = str(uuid.uuid4())
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        port = str(listener.getsockname()[1])
    marker = 'PIGEON_WAKE_' + uuid.uuid4().hex
    database = directory / 'messages.db'
    config = {'mcpServers': {'pigeon_probe': {
        'command': str(PIGEON), 'args': ['mcp', port, '--claude-channel']}}}
    config_file = directory / 'mcp.json'
    config_file.write_text(json.dumps(config, indent=2), encoding='utf-8')
    (directory / 'test-info.json').write_text(json.dumps({'port': port, 'session': session, 'marker': marker}, indent=2), encoding='utf-8')
    print('Prepared a disposable fixture; no service or session was started.')
    print('Test directory:', directory)
    print('Synthetic marker:', marker)
    print('\nAfter approval, in PowerShell window 1 start the foreground service:')
    print('$env:PIGEON_PORT = ' + quote(port))
    print('$env:PIGEON_DATABASE = ' + quote(database))
    print('& ' + quote(PIGEON) + ' start --foreground')
    print('\nIn window 2, create the topic and two synthetic routing identities:')
    print('$env:PIGEON_PORT = ' + quote(port))
    print('& ' + quote(PIGEON) + ' topics create pigeon_wake')
    print('& ' + quote(PIGEON) + ' topics subscribe pigeon_wake bob claude_code ' + quote(session))
    print('& ' + quote(PIGEON) + ' topics subscribe pigeon_wake ana codex synthetic-sender')
    print('\nStart one new interactive Claude session in window 2:')
    print('Set-Location -LiteralPath ' + quote(directory))
    print('& ' + quote(claude) + ' --strict-mcp-config --mcp-config ' + quote(config_file)
          + ' --dangerously-load-development-channels server:pigeon_probe --session-id ' + quote(session))
    print('Accept the development-channel warning only if it names server:pigeon_probe.')
    print('Wait for its channel-registration notice. Then tell the new Claude session:')
    print('"This is an isolated Pigeon wake test. Call pigeon_topic_join for topic pigeon_wake, agent bob, runtime claude_code, sessionId ' + session + '. When the synthetic wake arrives, confirm its generation, then call pigeon_receive, '
          'check the marker ' + marker + ', acknowledge the receipt and complete the wake. Do not run commands or edit files."')
    print('\nIn window 3, send exactly one synthetic message:')
    print('$env:PIGEON_PORT = ' + quote(port))
    print('& ' + quote(PIGEON) + ' messages send ana pigeon_wake ' + quote(marker))
    print('\nAfter the Claude session responds, run the evidence check:')
    print('python ' + quote(Path(__file__).resolve()) + ' verify ' + quote(directory))
    print('Exit the new Claude session with /exit and stop the foreground service with Enter.')
    print('The fixture directory remains for inspection and is not deleted automatically.')


def verify(directory):
    directory = Path(directory).resolve(strict=True)
    temp_root = Path(tempfile.gettempdir()).resolve()
    if directory.parent != temp_root or not directory.name.startswith('pigeon-wake-test-'):
        raise SystemExit('Expected a Pigeon wake-test directory created under the temporary directory.')
    info = json.loads((directory / 'test-info.json').read_text(encoding='utf-8'))
    if not re.fullmatch(r'PIGEON_WAKE_[0-9a-f]{32}', info.get('marker', '')):
        raise SystemExit('Invalid test marker.')
    db = directory / 'messages.db'
    if not db.is_file():
        raise SystemExit('The test database does not exist.')
    with closing(sqlite3.connect(f'file:{db.as_posix()}?mode=ro', uri=True)) as connection:
        rows = connection.execute('SELECT id,status FROM messages WHERE project=? AND sender=? AND recipient=? AND body=?',
                                  ('__pigeon_topics_v2__', 'ana', 'bob', info['marker'])).fetchall()
        if len(rows) != 1:
            print('Expected one synthetic Bob delivery; found:', len(rows))
            return
        message_id, status = rows[0]
        writes = connection.execute("SELECT COUNT(*) FROM wake_attempts WHERE project=? AND agent=? AND mode='claude-channel' AND outcome='transport-written'", ('__pigeon_topics_v2__', 'bob')).fetchone()[0]
        wake = connection.execute("SELECT state,confirmed_at IS NOT NULL FROM wake_state WHERE project=? AND agent=?", ('__pigeon_topics_v2__', 'bob')).fetchone()
        confirms = connection.execute("SELECT COUNT(*) FROM activity WHERE project=? AND event='wake-confirmed' AND detail=?", ('__pigeon_topics_v2__', 'bob')).fetchone()[0]
        attempts = connection.execute('SELECT outcome FROM attempts WHERE message=? ORDER BY number', (message_id,)).fetchall()
        print('Synthetic delivery stored: True')
        print('Channel transport write recorded:', writes > 0)
        print('Wake confirmation recorded:', confirms > 0)
        print('Wake state:', wake[0] if wake else 'missing')
        print('Delivery status:', status)
        print('Attempt outcomes:', ', '.join(item[0] for item in attempts) or 'none')
        print('A transport write is not an agent acknowledgment; only acknowledged status records that.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['prepare', 'verify'])
    parser.add_argument('directory', nargs='?')
    arguments = parser.parse_args()
    if arguments.action == 'prepare' and arguments.directory is None:
        prepare()
    elif arguments.action == 'verify' and arguments.directory:
        verify(arguments.directory)
    else:
        parser.error('prepare takes no directory; verify requires the prepared directory')
