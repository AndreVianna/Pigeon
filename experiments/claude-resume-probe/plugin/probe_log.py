"""Append one JSON line to the probe log; never accesses Pigeon, sessions or credentials."""
import json
import os
from datetime import datetime, timezone
from pathlib import Path

LOG = Path('C:/Projects/Pigeon/experiments/claude-resume-probe/logs/probe.jsonl')


def log(probe, event, **fields):
    LOG.parent.mkdir(parents=True, exist_ok=True)
    entry = {'ts': datetime.now(timezone.utc).isoformat(timespec='milliseconds'), 'probe': probe,
             'event': event, 'pid': os.getpid(), 'ppid': os.getppid(),
             'env_session_id': os.environ.get('CLAUDE_CODE_SESSION_ID'), **fields}
    with LOG.open('a', encoding='utf-8') as f:
        f.write(json.dumps(entry) + '\n')
