#!/usr/bin/env bash
# Runs `pigeon hook wait claude-code` for an asyncRewake hook and forwards exit code 2 only
# for Pigeon's own fixed wake or renewal line. Claude Code shows the stderr of an exit-2
# asyncRewake hook to the model and starts a turn, so an older or mismatched `pigeon` that
# exits 2 on a usage error would otherwise wake the session after every turn. Every other
# outcome exits 0 without output. hook-wait.ps1 is the equivalent for hosts without Git Bash.
# PIGEON_EXE selects a specific build for isolated tests; normally `pigeon` comes from PATH.
for arg in "$@"; do
    case "$arg" in
        --[a-z]*|[0-9]*) ;;
        *) exit 0 ;;
    esac
done
errors=$("${PIGEON_EXE:-pigeon}" hook wait claude-code "$@" 2>&1 >/dev/null)
code=$?
errors=${errors//$'\r'/}
[ "$code" -eq 2 ] || exit 0
case "$errors" in
    *$'\n'*) exit 0 ;;
esac
wake='^Pigeon wake generation [0-9a-f]{32}\. '
renewal='^Pigeon notification waiter needs renewal\. '
if [[ $errors =~ $wake || $errors =~ $renewal ]]; then
    printf '%s\n' "$errors" >&2
    exit 2
fi
exit 0
