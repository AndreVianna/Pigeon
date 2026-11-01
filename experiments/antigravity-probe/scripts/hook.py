"""Antigravity hook probe: logs the event, the conversation ID and the payload's field names.

Usage (from hooks.json): python hook.py <EventName>
It never logs prompt text (lastUserInput) or transcript contents, and always lets the agent continue.
"""
import json
import sys

import probe_log

event = sys.argv[1] if len(sys.argv) > 1 else 'unknown'
try:
    payload = json.loads(sys.stdin.read() or '{}')
except json.JSONDecodeError:
    payload = {}
safe = {key: payload.get(key) for key in ('conversationId', 'invocationNum', 'executionNum', 'terminationReason', 'fullyIdle', 'modelName') if key in payload}
probe_log.write('hook', event=event, fields=sorted(payload), values=safe, environment=probe_log.environment_names())
# Stop requires a decision; any value other than "continue" lets the agent stop.
print(json.dumps({'decision': 'allow'} if event == 'Stop' else {}))
