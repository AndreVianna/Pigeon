"""Helper for the Antigravity probe (see README.md next to this file).

  python -I probe.py trigger <conversation-id>   ask the sidecar to send its test message to that conversation
  python -I probe.py metadata <conversation-id>  ask the sidecar to log that conversation's metadata
  python -I probe.py show                        print the probe logs
  python -I probe.py clean                       delete the probe logs
"""
import os
import shutil
import sys

LOG_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs')


def main(argv):
    command = argv[0] if argv else ''
    if command == 'trigger' and len(argv) == 2:
        os.makedirs(LOG_DIR, exist_ok=True)
        with open(os.path.join(LOG_DIR, 'trigger.txt'), 'w', encoding='utf-8') as trigger:
            trigger.write(argv[1])
        print(f'Trigger written; the sidecar sends its message to {argv[1]} within a second if it is running.')
    elif command == 'metadata' and len(argv) == 2:
        os.makedirs(LOG_DIR, exist_ok=True)
        with open(os.path.join(LOG_DIR, 'trigger.txt'), 'w', encoding='utf-8') as trigger:
            trigger.write('metadata ' + argv[1])
        print(f'Trigger written; the sidecar logs the metadata of {argv[1]} within a second if it is running.')
    elif command == 'show':
        for name in ('hook.jsonl', 'mcp.jsonl', 'sidecar.jsonl'):
            path = os.path.join(LOG_DIR, name)
            print(f'== {name}')
            if os.path.exists(path):
                with open(path, encoding='utf-8') as log:
                    print(log.read().rstrip())
    elif command == 'clean':
        shutil.rmtree(LOG_DIR, ignore_errors=True)
        print('Logs deleted.')
    else:
        print(__doc__)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
