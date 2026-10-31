# Runs `pigeon hook wait claude-code` for an asyncRewake hook and forwards exit code 2 only
# for Pigeon's own fixed wake or renewal line. Claude Code shows the stderr of an exit-2
# asyncRewake hook to the model and starts a turn, so an older or mismatched `pigeon` that
# exits 2 on a usage error would otherwise wake the session after every turn. Every other
# outcome exits 0 without output.
#
# The child is started through System.Diagnostics.Process rather than PowerShell redirection:
# Windows PowerShell 5.1 turns native stderr into error records, which would swallow real wakes.
try {
    foreach ($arg in $args) {
        if ($arg -notmatch '^(--[a-z]+|[0-9]+)$') { exit 0 }
    }
    # PIGEON_EXE selects a specific build for isolated tests; normally `pigeon` comes from PATH.
    $executable = if ($env:PIGEON_EXE) { $env:PIGEON_EXE } else { (Get-Command pigeon -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source }
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $executable
    $start.Arguments = (@('hook', 'wait', 'claude-code') + $args) -join ' '
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $hookInput = [Console]::In.ReadToEnd()
    $child = [System.Diagnostics.Process]::Start($start)
    $output = $child.StandardOutput.ReadToEndAsync()
    $errors = $child.StandardError.ReadToEndAsync()
    $child.StandardInput.Write($hookInput)
    $child.StandardInput.Close()
    $child.WaitForExit()
    $null = $output.Result
    $lines = @($errors.Result -split "`r?`n" | Where-Object { $_ -ne '' })
    $wake = '^Pigeon wake generation [0-9a-f]{32}\. '
    $renewal = '^Pigeon notification waiter needs renewal\. '
    if ($child.ExitCode -eq 2 -and $lines.Count -eq 1 -and ($lines[0] -match $wake -or $lines[0] -match $renewal)) {
        [Console]::Error.WriteLine($lines[0])
        exit 2
    }
}
catch {
    # Never let a wrapper failure become a wake.
}
exit 0
