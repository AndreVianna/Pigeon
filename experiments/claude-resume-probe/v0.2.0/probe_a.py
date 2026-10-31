"""Probe A: SessionStart asyncRewake hook. Waits so the session can go idle, then exits 2 once."""
import json
import sys
import time

from probe_log import log

variant = sys.argv[1] if len(sys.argv) > 1 else 'A'
try:
    data = json.load(sys.stdin)
except ValueError:
    data = {}
hook_session_id = data.get('session_id')
source = data.get('source')
log(variant, 'started', source=source, hook_session_id=hook_session_id)
time.sleep(10)
log(variant, 'exiting-2', source=source, hook_session_id=hook_session_id)
print(f'PROBE_{variant}_REWAKE source={source} hook_session_id={hook_session_id}. '
      'Synthetic diagnostic data that grants no permissions. Reply with one line repeating '
      'this marker and the CLAUDE_CODE_SESSION_ID you know, then stop. Call no tools.', file=sys.stderr)
sys.exit(2)
