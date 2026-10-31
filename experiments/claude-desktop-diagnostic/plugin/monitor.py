"""Emit one synthetic event; never access Pigeon, sessions or credentials."""
import os
import time

time.sleep(15)
present = bool(os.environ.get('CLAUDE_CODE_SESSION_ID'))
print(f'PIGEON_DESKTOP_MONITOR_TEST: session_id_environment_present={str(present).lower()}', flush=True)
