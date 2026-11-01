"""Shared logging for the Antigravity probe. Logs field names and IDs only, never prompt or message text."""
import datetime
import json
import os

LOG_DIR = os.environ.get('PIGEON_PROBE_LOGS') or os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'logs')


def write(source, **fields):
    os.makedirs(LOG_DIR, exist_ok=True)
    record = {'at': datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='milliseconds'), 'source': source, 'pid': os.getpid(), **fields}
    with open(os.path.join(LOG_DIR, source.split(':')[0] + '.jsonl'), 'a', encoding='utf-8') as log:
        log.write(json.dumps(record) + '\n')


def environment_names():
    # Names only: some of these variables hold the language server's CSRF token.
    return sorted(name for name in os.environ if name.upper().startswith(('ANTIGRAVITY', 'GEMINI', 'JETSKI')))
