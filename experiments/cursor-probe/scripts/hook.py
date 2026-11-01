"""Cursor hook probe. Usage (from .cursor/hooks.json): python hook.py <event>

Logs the event, the conversation and generation IDs, a few status fields and the payload's field names.
It never logs the prompt, attachments, the transcript or the user's e-mail address.

- sessionStart adds a fixed sentence to the conversation's context, to test whether the agent sees it.
- stop: if `logs/stop-plan.txt` holds a number of seconds, the hook waits that long (logging when it
  starts and finishes) and then returns a follow-up message asking for PONG. The plan is used once.
"""
import json
import os
import sys
import time

import probe_log

event = sys.argv[1] if len(sys.argv) > 1 else 'unknown'
try:
    payload = json.loads(sys.stdin.read() or '{}')
except json.JSONDecodeError:
    payload = {}
safe = {key: payload.get(key) for key in ('conversation_id', 'generation_id', 'session_id', 'hook_event_name', 'status', 'loop_count', 'cursor_version', 'model') if key in payload}
probe_log.write('hook', event=event, fields=sorted(payload), values=safe, environment=probe_log.environment_names())

output = {}
if event == 'sessionStart':
    output = {'additional_context': 'Cursor probe: the code word for this session is PEGASUS. Mention it only if asked for the code word.'}
elif event == 'beforeSubmitPrompt':
    output = {'continue': True}
elif event == 'stop':
    plan = os.path.join(probe_log.LOG_DIR, 'stop-plan.txt')
    if os.path.exists(plan):
        with open(plan, encoding='utf-8') as file:
            seconds = int(file.read().strip() or '0')
        os.remove(plan)
        probe_log.write('hook', event='stop-wait-start', seconds=seconds, values=safe)
        time.sleep(seconds)
        probe_log.write('hook', event='stop-wait-end', seconds=seconds, values=safe)
        output = {'followup_message': 'Cursor probe: this is a test follow-up from a stop hook. Reply with the single word PONG.'}
print(json.dumps(output))
