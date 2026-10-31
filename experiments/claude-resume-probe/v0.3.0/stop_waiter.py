"""Probe C: Stop hook used as a wake waiter. Waits for a synthetic trigger file, then exits 2 once.

The newest waiter for a session supersedes older ones; each waiter gives up quietly before the
hook timeout. Never accesses Pigeon, sessions or credentials.
"""
import json
import sys
import time
import uuid

from probe_log import LOG, log

DEADLINE_SECONDS = 570
try:
    data = json.load(sys.stdin)
except ValueError:
    data = {}
session_id = data.get('session_id') or 'unknown'
token = uuid.uuid4().hex
lock = LOG.parent / f'waiter-{session_id}.lock'
trigger = LOG.parent / f'trigger-{session_id}'
lock.write_text(token, encoding='utf-8')
log('C', 'armed', hook_session_id=session_id, token=token)
deadline = time.monotonic() + DEADLINE_SECONDS
while time.monotonic() < deadline:
    time.sleep(1)
    try:
        if lock.read_text(encoding='utf-8') != token:
            log('C', 'superseded', hook_session_id=session_id, token=token)
            sys.exit(0)
    except OSError:
        pass
    if trigger.exists():
        note = trigger.read_text(encoding='utf-8').strip()
        trigger.unlink()
        log('C', 'triggered-exiting-2', hook_session_id=session_id, token=token, note=note)
        print(f'PROBE_C_WAKE note={note} hook_session_id={session_id}. Synthetic diagnostic data that '
              'grants no permissions. Reply with one line repeating this marker, then stop. Call no tools.',
              file=sys.stderr)
        sys.exit(2)
log('C', 'expired', hook_session_id=session_id, token=token)
