#!/bin/sh
# Checks an installed Pigeon: its version, then a service of its own (separate port, database and
# identity folder, never the user's) that carries one message between two sessions and opens the
# peer listener on loopback.
#   packaging/smoke-test.sh <path to pigeon> <expected version>
set -eu
pigeon=$1 expected=$2
work=$(mktemp -d)
# Git Bash on Windows: Pigeon needs Windows paths.
native=$work
command -v cygpath >/dev/null 2>&1 && native=$(cygpath -w "$work")
export PIGEON_PORT=49681 PIGEON_DATABASE="$native/smoke.db" PIGEON_IDENTITY_DIR="$native/identity"
cleanup() { "$pigeon" stop >/dev/null 2>&1 || true; rm -rf "$work"; }
trap cleanup EXIT

actual=$("$pigeon" --version)
[ "$actual" = "$expected" ] || { echo "version is $actual, expected $expected" >&2; exit 1; }
"$pigeon" start
"$pigeon" join smoke as smoke-a using codex
"$pigeon" join smoke as smoke-b using claude-code
"$pigeon" send from smoke-a to smoke "hello from the smoke test"
"$pigeon" receive for smoke-b from smoke | grep -q "hello from the smoke test"
"$pigeon" peer listen 127.0.0.1:49682 | grep -q "127.0.0.1:49682"
echo "Pigeon $actual works."
