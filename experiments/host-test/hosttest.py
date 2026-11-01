"""Helper for Pigeon host tests H1-H3 (see README.md next to this file).

Uses only the frozen test build in artifacts/host-test/bin, an isolated service on port 49473
and a disposable database in artifacts/host-test/data. It never touches the user's normal
Pigeon service (port 49372) or any session other than the test ones named on the command line.

  python -I hosttest.py start                       start the isolated service and join the peer to the topic
  python -I hosttest.py status                      pigeon status and topics for the isolated service
  python -I hosttest.py send <text>                 send a synthetic message from the peer to its partner
  python -I hosttest.py inbox                       receive the messages sent to the peer
  python -I hosttest.py waiters                     list test waiters and their launcher chains
  python -I hosttest.py kill-outer-shell --yes      H3: kill only the outer hook shell of the test waiter
  python -I hosttest.py stop [--clean]              stop the isolated service; --clean deletes its data
"""
import json, os, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
EXE = os.path.join(ROOT, 'artifacts', 'host-test', 'bin', 'pigeon.exe')
# The overrides exist only to dry-run this script against a throwaway service.
DATA = os.environ.get('HOSTTEST_DATA') or os.path.join(ROOT, 'artifacts', 'host-test', 'data')
PORT, TOPIC = os.environ.get('HOSTTEST_PORT') or '49473', 'pigeon-host-test'
PEER_TOOL, PEER_SESSION = 'host-test', 'host-test-peer'
ENV = dict(os.environ, PIGEON_PORT=PORT, PIGEON_DATABASE=os.path.join(DATA, 'messages.db'))
ENV.pop('CLAUDE_CODE_SESSION_ID', None)


def pigeon(*args, check=True):
    result = subprocess.run([EXE, *args], env=ENV, capture_output=True, text=True, timeout=30)
    if check and result.returncode != 0:
        sys.exit(f'pigeon {" ".join(args)} failed ({result.returncode}): {result.stderr.strip()}')
    return result


def processes():
    script = 'Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine | ConvertTo-Json -Compress'
    out = subprocess.run(['powershell', '-NoProfile', '-Command', script], capture_output=True, text=True).stdout
    return {p['ProcessId']: p for p in json.loads(out)}


def waiters():
    """Test waiters only: hook waiters running the frozen test build."""
    procs = processes()
    found = []
    for p in procs.values():
        if (p['ExecutablePath'] or '').lower() == EXE.lower() and 'hook wait' in (p['CommandLine'] or ''):
            chain, pid = [], p['ProcessId']
            while pid in procs and len(chain) < 10:
                chain.append(procs[pid])
                if procs[pid]['Name'].lower() == 'claude.exe':
                    break
                pid = procs[pid]['ParentProcessId']
            found.append(chain)
    return found


def main(argv):
    command = argv[0] if argv else ''
    if command == 'start':
        os.makedirs(DATA, exist_ok=True)
        print(pigeon('start').stdout.strip())
        print(pigeon('join', TOPIC, 'as', PEER_SESSION, 'using', PEER_TOOL).stdout.strip())
        print(f'Service on port {PORT}, data in {DATA}. The test conversation joins {TOPIC} as the partner.')
    elif command == 'status':
        print(pigeon('status', check=False).stdout)
        print(pigeon('topics', check=False).stdout)
    elif command == 'send' and len(argv) == 2:
        print(pigeon('send', 'from', PEER_SESSION, 'to', TOPIC, argv[1]).stdout.strip())
    elif command == 'inbox':
        print(pigeon('receive', 'for', PEER_SESSION, 'from', TOPIC).stdout.strip())
    elif command == 'waiters':
        for chain in waiters():
            print(' <- '.join(f"{p['ProcessId']} {p['Name']}" for p in chain))
        print('done')
    elif command == 'kill-outer-shell':
        found = waiters()
        if len(found) != 1:
            sys.exit(f'Expected exactly one test waiter, found {len(found)}; nothing killed.')
        chain = found[0]
        # Claude Code's hook launcher can exit at once (seen on the host), so the chain may end at
        # the top hook shell rather than at claude.exe.
        shells = chain[1:-1] if chain[-1]['Name'].lower() == 'claude.exe' else chain[1:]
        if not shells or not all(p['Name'].lower() == 'bash.exe' and 'hook-wait.sh' in (p['CommandLine'] or '') for p in shells):
            sys.exit('The test waiter is not under hook-wait.sh shells; nothing killed.')
        outer = shells[-1]  # the top hook shell; inner shells and the waiter stay alive
        print('chain: ' + ' <- '.join(f"{p['ProcessId']} {p['Name']}" for p in chain))
        if '--yes' not in argv:
            sys.exit(f"Would kill only PID {outer['ProcessId']} ({outer['Name']}). Re-run with --yes.")
        subprocess.run(['taskkill', '/F', '/PID', str(outer['ProcessId'])], check=True)
        print(f"Killed only PID {outer['ProcessId']}.")
    elif command == 'stop':
        print(pigeon('stop', check=False).stdout.strip())
        if '--clean' in argv:
            import shutil
            shutil.rmtree(DATA, ignore_errors=True)
            print('data removed')
    else:
        print(__doc__)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
