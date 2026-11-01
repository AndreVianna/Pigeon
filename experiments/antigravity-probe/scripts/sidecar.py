"""Antigravity sidecar probe: tests whether agentapi send-message starts a turn in an existing conversation.

It logs the environment variable names it receives and where agentapi is, then waits for a trigger file
(written by `probe.py trigger <conversation-id>`). For each trigger it runs
`agentapi send-message <conversation-id> "<fixed probe text>"` once and logs the exit code and output.
"""
import os
import shutil
import subprocess
import time

import probe_log

MESSAGE = 'Pigeon probe: this is a test message sent by a sidecar. Reply with the single word PONG.'
TRIGGER = os.path.join(probe_log.LOG_DIR, 'trigger.txt')


def agentapi():
    configured = os.environ.get('ANTIGRAVITY_AGENTAPI_EXE')
    return configured if configured and os.path.exists(configured) else shutil.which('agentapi')


probe_log.write('sidecar', event='started', agentapi=agentapi(), environment=probe_log.environment_names())
while True:
    time.sleep(1)
    if not os.path.exists(TRIGGER):
        continue
    with open(TRIGGER, encoding='utf-8') as trigger:
        conversation = trigger.read().strip()
    os.remove(TRIGGER)
    executable = agentapi()
    # "metadata <id>" asks only for the conversation's metadata, which may show whether it is busy.
    if conversation.startswith('metadata ') and executable:
        conversation = conversation.split(' ', 1)[1]
        prefix = ['agentapi'] if os.path.basename(executable).lower().startswith('language_server') else []
        result = subprocess.run([executable, *prefix, 'get-conversation-metadata', conversation], capture_output=True, text=True, timeout=60)
        probe_log.write('sidecar', event='metadata', conversation=conversation, exit_code=result.returncode, stdout=result.stdout[:4000], stderr_head=result.stderr[:1000])
        continue
    if not executable or not conversation:
        probe_log.write('sidecar', event='skipped', agentapi=executable, conversation=conversation)
        continue
    started = time.time()
    # ANTIGRAVITY_AGENTAPI_EXE is the language server binary; Antigravity's own sidecars pass "agentapi"
    # as its first argument. A plain agentapi on PATH takes the subcommand directly.
    arguments = ['agentapi', 'send-message'] if os.path.basename(executable).lower().startswith('language_server') else ['send-message']
    try:
        result = subprocess.run([executable, *arguments, conversation, MESSAGE], capture_output=True, text=True, timeout=60)
        probe_log.write('sidecar', event='send-message', conversation=conversation, arguments=arguments, exit_code=result.returncode, stdout=result.stdout[:2000], stderr_head=result.stderr[:1500], stderr_tail=result.stderr[-500:], seconds=round(time.time() - started, 2))
    except (OSError, subprocess.TimeoutExpired) as error:
        probe_log.write('sidecar', event='send-message-failed', conversation=conversation, error=repr(error))
