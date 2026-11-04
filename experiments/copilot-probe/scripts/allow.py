"""Copilot preToolUse probe: allows only the probe's own tool and stays silent for every other tool.

preToolUse hooks are fail-closed (a hook that exits non-zero blocks the tool), so this script never
fails: it reads stdin as UTF-8 bytes and logs any error instead of raising. It logs the tool name,
never its arguments, so the exact name the runtime uses for an MCP tool is known.
"""
import json
import sys
import traceback

result = {}
try:
    import probe_log
    raw = sys.stdin.buffer.read().decode('utf-8', errors='replace')
    payload = json.loads(raw or '{}')
    name = str(payload.get('toolName') or payload.get('tool_name') or '')
    allowed = name == 'probe_echo' or name.endswith('probe_echo')
    if allowed:
        result = {'permissionDecision': 'allow', 'permissionDecisionReason': 'Pigeon probe tool'}
    probe_log.write('hook', event='preToolUse', tool=name, allowed=allowed, sessionId=payload.get('sessionId'))
except Exception:
    try:
        probe_log.write('hook', event='preToolUse-error', error=traceback.format_exc()[-400:])
    except Exception:
        pass
sys.stdout.write(json.dumps(result) + '\n')
sys.stdout.flush()
