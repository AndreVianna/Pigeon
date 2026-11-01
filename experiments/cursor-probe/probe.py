"""Helper for the Cursor probe (see README.md next to this file).

  python -I probe.py stop <seconds>   make the next stop hook wait that long, then return a PONG follow-up
  python -I probe.py show             print the probe logs
  python -I probe.py clean            delete the probe logs
"""
import os
import shutil
import sys

LOG_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'logs')


def main(argv):
    command = argv[0] if argv else ''
    if command == 'stop' and len(argv) == 2 and argv[1].isdigit():
        os.makedirs(LOG_DIR, exist_ok=True)
        with open(os.path.join(LOG_DIR, 'stop-plan.txt'), 'w', encoding='utf-8') as plan:
            plan.write(argv[1])
        print(f'The next stop hook waits {argv[1]} seconds, then asks for PONG.')
    elif command == 'show':
        for name in ('hook.jsonl', 'mcp.jsonl'):
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
