"""Copilot hook probe: logs the event, the session IDs and the payload's field names.

Usage: python hook.py <event>. Never logs prompts, tool arguments or transcripts, and never blocks.
"""
import json
import sys

import probe_log

event = sys.argv[1] if len(sys.argv) > 1 else 'unknown'
try:
    payload = json.loads(sys.stdin.read() or '{}')
except json.JSONDecodeError:
    payload = {}
keys = ('sessionId', 'session_id', 'hookEventName', 'hook_event_name', 'source', 'stopReason', 'stop_hook_active', 'reason')
probe_log.write('hook', event=event, fields=sorted(payload), values={k: payload[k] for k in keys if k in payload}, environment=probe_log.environment_names())
print('{}')
