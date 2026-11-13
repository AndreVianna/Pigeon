#!/bin/sh
# Runs `pigeon hook wait claude-code` for an asyncRewake hook and forwards exit code 2 only
# for Pigeon's own fixed wake or renewal line. Claude Code shows the stderr of an exit-2
# asyncRewake hook to the model and starts a turn, so an older or mismatched `pigeon` that
# exits 2 on a usage error would otherwise wake the session after every turn. Every other
# outcome exits 0 without output.
# POSIX sh only: the hook runs as `sh`, which is dash on Debian and Ubuntu.
# PIGEON_EXE selects a specific build for isolated tests; normally `pigeon` comes from PATH.
for arg in "$@"; do
    case "$arg" in
        --[a-z]*|[0-9]*) ;;
        *) exit 0 ;;
    esac
done
errors=$("${PIGEON_EXE:-pigeon}" hook wait claude-code "$@" 2>&1 >/dev/null)
code=$?
[ "$code" -eq 2 ] || exit 0
# Command substitution removes trailing LF, leaving CR for CRLF output.
cr=$(printf '\r')
case "$errors" in
    *"$cr") errors=${errors%"$cr"} ;;
esac
newline='
'
case "$errors" in
    *"$cr"*|*"$newline"*) exit 0 ;;
esac
case "$errors" in
    'Pigeon wake generation '*)
        remainder=${errors#'Pigeon wake generation '}
        marker=${remainder%%.*}
        [ "${#marker}" -eq 32 ] || exit 0
        case "$marker" in *[!0123456789abcdef]*) exit 0 ;; esac
        suffix=${remainder#"$marker"}
        case "$suffix" in '. '*) ;; *) exit 0 ;; esac
        ;;
    'Pigeon notification waiter needs renewal. '*) ;;
    *) exit 0 ;;
esac
printf '%s\n' "$errors" >&2
exit 2
