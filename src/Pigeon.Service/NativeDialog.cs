using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Pigeon.Service;

// What a dialog asks: "ask" is Allow or Deny, "code" asks for a join code, "show" shows the join
// code to give the other person (Code), with a Copy button where the platform has one.
public sealed record DialogRequest(string Kind, string Message, string? Code = null);

// Answered: "accept" (with the code entered, for "code") or "decline". NoAnswer: the window was
// closed by Pigeon, or timed out. Failed: no window could be shown.
public sealed record DialogOutcome(string? Action, string? Code = null, bool Failed = false) {
    public static readonly DialogOutcome NoAnswer = new(Action: null);
    public static readonly DialogOutcome Broken = new(Action: null, Failed: true);
}

public sealed record DialogCommand(ProcessStartInfo Start, Func<int, string, DialogOutcome> Read);

// A small window of the operating system for hosts that cannot show Pigeon's prompts (see
// docs/remote-design.md, "Native dialogs"). It is outside the conversation: no Pigeon tool can read
// it or answer it. Text from the other machine never reaches a shell or a script: Windows and macOS
// read it from environment variables, and Linux gets it as an argument with markup escaped.
public static partial class NativeDialog {
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    // "windows", "macos", the full path of zenity or kdialog, or null when this machine shows no
    // dialogs or the person turned them off with PIGEON_PROMPTS=terminal.
    public static string? Platform(Func<string, string?> environment) {
        if (string.Equals(environment("PIGEON_PROMPTS"), "terminal", StringComparison.OrdinalIgnoreCase))
            return null;
        if (OperatingSystem.IsWindows())
            return Environment.UserInteractive && File.Exists(WindowsPowerShell) ? "windows" : null;
        if (OperatingSystem.IsMacOS())
            return File.Exists("/usr/bin/osascript") ? "macos" : null;
        if (OperatingSystem.IsLinux() && (!string.IsNullOrEmpty(environment("DISPLAY")) || !string.IsNullOrEmpty(environment("WAYLAND_DISPLAY"))))
            return OnPath("zenity", environment) ?? OnPath("kdialog", environment);
        return null;
    }

    // Runs dialogs on this machine, or null when it shows none.
    public static Func<DialogRequest, CancellationToken, Task<DialogOutcome>>? ForThisMachine() =>
        Platform(Environment.GetEnvironmentVariable) is { } platform ? (request, cancellation) => RunAsync(Command(request, platform), cancellation) : null;

    private static string WindowsPowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    private static string? OnPath(string name, Func<string, string?> environment) {
        foreach (var directory in (environment("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var candidate = Path.Combine(directory, name);
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    // The prompt's text without what only fits a host prompt: the "Pigeon:" prefix (the window is
    // titled Pigeon) and the "(Accept to ...)" hint (the window has its own buttons).
    public static string Text(string message) {
        var text = AcceptHint().Replace(message.StartsWith("Pigeon: ", StringComparison.Ordinal) ? message["Pigeon: ".Length..] : message, "");
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    [GeneratedRegex(@" \((Accept|Enter)[^)]*\)")]
    private static partial Regex AcceptHint();

    public static DialogCommand Command(DialogRequest request, string platform) => platform switch {
        "windows" => Windows(request),
        "macos" => MacOs(request),
        _ when Path.GetFileName(platform) == "kdialog" => KDialog(request, platform),
        _ => Zenity(request, platform)
    };

    private static ProcessStartInfo Start(string file, IEnumerable<string> arguments, DialogRequest request) {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["PIGEON_DIALOG_KIND"] = request.Kind;
        start.Environment["PIGEON_DIALOG_TEXT"] = Text(request.Message);
        start.Environment["PIGEON_DIALOG_CODE"] = request.Code ?? "";
        return start;
    }

    // Windows and macOS print "accept", "accept <code>", "decline" or "timeout" as their last line.
    private static DialogOutcome ReadLine(int exitCode, string output) {
        if (exitCode != 0)
            return DialogOutcome.Broken;
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        return line switch {
            "accept" => new("accept"),
            "decline" => new("decline"),
            "timeout" => DialogOutcome.NoAnswer,
            _ when line.StartsWith("accept ", StringComparison.Ordinal) => new("accept", line["accept ".Length..].Trim()),
            _ => DialogOutcome.Broken
        };
    }

    private static DialogCommand Windows(DialogRequest request) {
        var script = Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsScript));
        return new(Start(WindowsPowerShell, ["-NoProfile", "-NonInteractive", "-Sta", "-EncodedCommand", script], request), ReadLine);
    }

    // A fixed script: everything it shows comes from the environment.
    internal const string WindowsScript = """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        [System.Windows.Forms.Application]::EnableVisualStyles()
        $kind = $env:PIGEON_DIALOG_KIND
        $code = $env:PIGEON_DIALOG_CODE
        $form = New-Object System.Windows.Forms.Form
        $form.Text = 'Pigeon'
        $form.TopMost = $true
        $form.StartPosition = 'CenterScreen'
        $form.FormBorderStyle = 'FixedDialog'
        $form.MaximizeBox = $false
        $form.MinimizeBox = $false
        $form.AutoSize = $true
        $form.AutoSizeMode = 'GrowAndShrink'
        $form.Padding = New-Object System.Windows.Forms.Padding 12
        $panel = New-Object System.Windows.Forms.FlowLayoutPanel
        $panel.FlowDirection = 'TopDown'
        $panel.WrapContents = $false
        $panel.AutoSize = $true
        $label = New-Object System.Windows.Forms.Label
        $label.UseMnemonic = $false
        $label.AutoSize = $true
        $label.MaximumSize = New-Object System.Drawing.Size 440, 0
        $label.Text = $env:PIGEON_DIALOG_TEXT
        $panel.Controls.Add($label)
        $box = $null
        if ($kind -ne 'ask') {
            $box = New-Object System.Windows.Forms.TextBox
            $box.Font = New-Object System.Drawing.Font 'Consolas', 18
            $box.Width = 140
            $box.Margin = New-Object System.Windows.Forms.Padding 0, 10, 0, 10
            if ($kind -eq 'show') { $box.Text = $code; $box.ReadOnly = $true } else { $box.MaxLength = 6 }
            $panel.Controls.Add($box)
        }
        $buttons = New-Object System.Windows.Forms.FlowLayoutPanel
        $buttons.AutoSize = $true
        $buttons.WrapContents = $false
        function Add-Button([string] $text, $result) {
            $button = New-Object System.Windows.Forms.Button
            $button.Text = $text
            $button.AutoSize = $true
            $button.DialogResult = $result
            $buttons.Controls.Add($button)
            return $button
        }
        if ($kind -eq 'show') {
            $copy = Add-Button 'Copy' ([System.Windows.Forms.DialogResult]::None)
            $copy.Add_Click({ [System.Windows.Forms.Clipboard]::SetText($code) })
            $close = Add-Button 'Close' ([System.Windows.Forms.DialogResult]::OK)
            $form.AcceptButton = $close
            $form.CancelButton = $close
        } else {
            $allow = Add-Button 'Allow' ([System.Windows.Forms.DialogResult]::OK)
            $deny = Add-Button 'Deny' ([System.Windows.Forms.DialogResult]::Cancel)
            $form.AcceptButton = $allow
            $form.CancelButton = $deny
        }
        $panel.Controls.Add($buttons)
        $form.Controls.Add($panel)
        $form.Add_Shown({ $form.Activate(); if ($kind -eq 'code') { [void] $box.Focus() } })
        $result = $form.ShowDialog()
        if ($kind -eq 'show') { 'accept' }
        elseif ($result -eq [System.Windows.Forms.DialogResult]::OK) { if ($kind -eq 'code') { 'accept ' + $box.Text } else { 'accept' } }
        else { 'decline' }
        """;

    private static DialogCommand MacOs(DialogRequest request) =>
        new(Start("/usr/bin/osascript", MacOsScript.Split('\n').SelectMany(line => new[] { "-e", line.TrimEnd('\r') }), request), ReadLine);

    internal const string MacOsScript = """
        set k to system attribute "PIGEON_DIALOG_KIND"
        set m to system attribute "PIGEON_DIALOG_TEXT"
        set c to system attribute "PIGEON_DIALOG_CODE"
        if k is "show" then
            repeat
                set r to display dialog m default answer c buttons {"Copy", "Close"} default button "Close" with title "Pigeon" giving up after 600
                if button returned of r is "Copy" then
                    set the clipboard to c
                else
                    exit repeat
                end if
            end repeat
            return "accept"
        end if
        try
            if k is "code" then
                set r to display dialog m default answer "" buttons {"Deny", "Allow"} default button "Allow" cancel button "Deny" with title "Pigeon" giving up after 600
            else
                set r to display dialog m buttons {"Deny", "Allow"} default button "Allow" cancel button "Deny" with title "Pigeon" giving up after 600
            end if
        on error number -128
            return "decline"
        end try
        if gave up of r then return "timeout"
        if k is "code" then return "accept " & (text returned of r)
        return "accept"
        """;

    // zenity and kdialog read Pango or rich text, so markup characters are escaped.
    internal static string Escaped(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    private static DialogCommand Zenity(DialogRequest request, string zenity) {
        var text = "--text=" + Escaped(Text(request.Message));
        string[] arguments = request.Kind switch {
            "show" => ["--entry", "--title=Pigeon", text, "--entry-text=" + request.Code, "--ok-label=Close", "--timeout=600"],
            "code" => ["--entry", "--title=Pigeon", text, "--ok-label=Allow", "--cancel-label=Deny", "--timeout=600"],
            _ => ["--question", "--title=Pigeon", text, "--ok-label=Allow", "--cancel-label=Deny", "--timeout=600"]
        };
        return new(Start(zenity, arguments, request), (exit, output) => Linux(request.Kind, exit, output, timeout: 5));
    }

    private static DialogCommand KDialog(DialogRequest request, string kdialog) {
        var text = Escaped(Text(request.Message));
        string[] arguments = request.Kind switch {
            "show" => ["--title", "Pigeon", "--inputbox", text, request.Code ?? ""],
            "code" => ["--title", "Pigeon", "--inputbox", text, ""],
            _ => ["--title", "Pigeon", "--yes-label", "Allow", "--no-label", "Deny", "--yesno", text]
        };
        return new(Start(kdialog, arguments, request), (exit, output) => Linux(request.Kind, exit, output, timeout: null));
    }

    private static DialogOutcome Linux(string kind, int exit, string output, int? timeout) =>
        kind == "show" ? exit is 0 or 1 or 5 ? new("accept") : DialogOutcome.Broken
        : exit == 0 ? new("accept", kind == "code" ? output.Trim() : null)
        : exit == timeout ? DialogOutcome.NoAnswer
        : exit is 1 or 2 ? new("decline")
        : DialogOutcome.Broken;

    // Shows the window until the person answers, Pigeon closes it (cancellation), or 10 minutes pass.
    public static async Task<DialogOutcome> RunAsync(DialogCommand command, CancellationToken cancellation) {
        Process process;
        try {
            process = Process.Start(command.Start) ?? throw new InvalidOperationException("no process");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) {
            return DialogOutcome.Broken;
        }

        using (process) {
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(Timeout);
            try {
                await process.WaitForExitAsync(limit.Token);
                return command.Read(process.ExitCode, await output);
            }
            catch (OperationCanceledException) {
                return DialogOutcome.NoAnswer;
            }
            finally {
                if (!process.HasExited) {
                    try {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
    }
}
