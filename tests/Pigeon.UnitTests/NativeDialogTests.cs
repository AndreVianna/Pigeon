using Pigeon.Service;

using Xunit;

namespace Pigeon.UnitTests;

// Pigeon's own windows, for hosts that cannot show its prompts. These tests never open one.
public sealed class NativeDialogTests {
    [Fact]
    public void The_person_can_turn_dialogs_off() =>
        Assert.Null(NativeDialog.Platform(name => name == "PIGEON_PROMPTS" ? "terminal" : Environment.GetEnvironmentVariable(name)));

    [Fact]
    public void A_window_shows_the_prompt_without_what_only_fits_a_host_prompt() {
        Assert.Equal("Office-laptop wants to join your topic \"t\" with codex. Allow?", NativeDialog.Text("Pigeon: office-laptop wants to join your topic \"t\" with codex. Allow? (Accept to allow; Decline or Esc to refuse.)"));
        Assert.Equal("Pigeon join code: 123456. Give it to the person there.", NativeDialog.Text("Pigeon join code: 123456. Give it to the person there."));
    }

    // A claimed machine name comes from the other machine; it must never reach a script or a shell.
    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    public void Windows_and_macOS_read_the_text_from_the_environment_only(string platform) {
        const string hostile = "evil'; rm -rf ~; $(calc) \"`n";
        var command = NativeDialog.Command(new("code", $"Pigeon: a new machine, {hostile}, wants to join"), platform);
        Assert.DoesNotContain(command.Start.ArgumentList, argument => argument.Contains("evil", StringComparison.Ordinal));
        Assert.Equal($"A new machine, {hostile}, wants to join", command.Start.Environment["PIGEON_DIALOG_TEXT"]);
        Assert.Equal("code", command.Start.Environment["PIGEON_DIALOG_KIND"]);
        Assert.False(command.Start.UseShellExecute);
    }

    [Fact]
    public void Linux_dialogs_get_the_text_as_an_argument_with_markup_escaped() {
        var zenity = NativeDialog.Command(new("ask", "Pigeon: <b>bold</b> & co wants to join"), "/usr/bin/zenity");
        Assert.Contains("--text=&lt;b&gt;bold&lt;/b&gt; &amp; co wants to join", zenity.Start.ArgumentList);
        Assert.Contains("--ok-label=Allow", zenity.Start.ArgumentList);
        var show = NativeDialog.Command(new("show", "Pigeon join code: 123456.", "123456"), "/usr/bin/kdialog");
        Assert.Equal(["--title", "Pigeon", "--inputbox", "Pigeon join code: 123456.", "123456"], show.Start.ArgumentList);
    }

    [Fact]
    public void Answers_are_read_from_each_kind_of_window() {
        var windows = NativeDialog.Command(new("code", "x"), "windows");
        Assert.Equal(new DialogOutcome("accept", "123456"), windows.Read(0, "accept 123456\r\n"));
        Assert.Equal(new DialogOutcome("decline"), windows.Read(0, "decline\r\n"));
        Assert.Equal(DialogOutcome.NoAnswer, windows.Read(0, "timeout\n"));
        Assert.True(windows.Read(1, "").Failed);
        Assert.True(windows.Read(0, "something else").Failed);

        var zenity = NativeDialog.Command(new("code", "x"), "/usr/bin/zenity");
        Assert.Equal(new DialogOutcome("accept", "654321"), zenity.Read(0, "654321\n"));
        Assert.Equal(new DialogOutcome("decline"), zenity.Read(1, ""));
        Assert.Equal(DialogOutcome.NoAnswer, zenity.Read(5, ""));
        Assert.True(zenity.Read(255, "").Failed);
        // Closing the code to show means only that it was seen.
        Assert.Equal(new DialogOutcome("accept"), NativeDialog.Command(new("show", "x", "123456"), "/usr/bin/zenity").Read(1, ""));
        Assert.Equal(new DialogOutcome("decline"), NativeDialog.Command(new("ask", "x"), "/usr/bin/kdialog").Read(1, ""));
    }

    [Fact]
    public async Task A_session_uses_windows_only_when_its_host_cannot_ask() {
        static async Task<McpSession> Session(bool hostShowsPrompts, bool dialogs) {
            var session = new McpSession("1", "claude-code", hostShowsPrompts: hostShowsPrompts, dialogs: dialogs ? (_, _) => Task.FromResult(DialogOutcome.NoAnswer) : null);
            _ = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{\"elicitation\":{}}}}");
            return session;
        }

        var terminal = await Session(hostShowsPrompts: true, dialogs: true);
        Assert.True(terminal.CanPrompt);
        Assert.False(terminal.UsesDialogs);
        var desktop = await Session(hostShowsPrompts: false, dialogs: true);
        Assert.True(desktop.UsesDialogs);
        Assert.True(desktop.ShowsPrompts);
        desktop.DialogsFailed();
        Assert.False(desktop.ShowsPrompts);
        Assert.False((await Session(hostShowsPrompts: false, dialogs: false)).ShowsPrompts);
    }
}
