using System.Reflection;

internal static class CliHelp {
    private sealed record Command(string Name, string Usage, string Summary, string Details, bool Internal = false);
    private static readonly Command[] Commands = [
        new("start", "pigeon start [--foreground]", "Start the Pigeon service in the background",
            "The service keeps messages on this computer. --foreground keeps it attached to this terminal until you press Enter.\nPIGEON_PORT selects the local port (default 49372) and PIGEON_DATABASE the database file."),
        new("stop", "pigeon stop", "Stop the Pigeon service", "Messages already queued are kept for the next start."),
        new("status", "pigeon status", "Show whether the service runs, which sessions are connected, and what is waiting", "Connected means the session's tool is running and reachable; it does not prove that a conversation is active."),
        new("topics", "pigeon topics", "List topics, their members and waiting messages", "A topic is a private conversation between two sessions."),
        new("join", "pigeon join <topic> as <session-id> using <tool>", "Join a topic, creating it if it does not exist",
            "The first session to join creates the topic and waits for a partner; the second completes it. A third is refused.\n<tool> is the program the session belongs to, for example claude-code, codex or antigravity. Only those three are woken automatically.\n\nExample:\n  pigeon join parser-review as 01a19407-57ce using codex"),
        new("leave", "pigeon leave <topic> as <session-id>", "Leave a topic",
            "Your unread messages wait for whoever takes your place. The last session to leave deletes the topic.\n\nExample:\n  pigeon leave parser-review as 01a19407-57ce"),
        new("send", "pigeon send from <session-id> to <topic> \"<message>\"", "Send a message to the other member of a topic",
            "The message waits in order until the recipient reads it, for up to 24 hours, also while nobody else is in the topic yet.\nUse - as the message to read it from standard input.\n\nExample:\n  pigeon send from 01a19407-57ce to parser-review \"Ready for review\""),
        new("receive", "pigeon receive for <session-id> from <topic>", "Read the messages waiting for a session in a topic",
            "Receiving a message removes it from the queue.\n\nExample:\n  pigeon receive for b1f82365-c3a4 from parser-review"),
        new("peer", "pigeon peer <name|listen|invite|accept|confirm|list|remove|address> ...", "Pair with Pigeon services on other machines",
            "Usage:\n  pigeon peer name [<name>]                     Name this service for its peers, for example ana-laptop\n  pigeon peer listen <address>[:<port>] | off   Accept paired peers on one address (port 49374 by default)\n  pigeon peer invite                            Create a one-time invitation (10 minutes)\n  pigeon peer accept <invitation> [as <name>]   Accept an invitation and compare the check code\n  pigeon peer confirm <code> [as <name>]        Finish pairing with the code the other person reads you\n  pigeon peer list                              Show peers, pairings waiting for you, and shared topics\n  pigeon peer remove <name>                     Remove a peer; its shared topics end at once\n  pigeon peer address <name> <address>[:<port>] Update where a peer is reached\n\nPeers talk over TLS with pinned certificates. Pairing grants nothing by itself: share topics one at a time with 'pigeon topic share'. Agents cannot pair or share."),
        new("topic", "pigeon topic <share|unshare> ...", "Share a topic with a paired peer, or stop sharing it",
            "Usage:\n  pigeon topic share <topic> with <peer> [--no-tool]   Offer one topic to one peer; its second seat becomes the peer's\n  pigeon topic unshare <topic>                         Stop sharing; the peer's copy ends\n\nOn the peer's machine an agent joins the topic as <topic>@<this service's name>. --no-tool withholds your member's tool name; session IDs are never shared."),
        new("mcp", "pigeon mcp [--codex-queue]", "Run the MCP server for an agent tool (used by the plugins)",
            "--codex-queue wakes the joined Codex session through `codex queue`. PIGEON_RUNTIME names the tool, and lets a Claude Code session restore itself on start. With antigravity or opencode, each call acts for the conversation or session the host names in it; with copilot, for the session in COPILOT_AGENT_SESSION_ID, or else the chat VS Code names in the call.", Internal: true),
        new("hook", "pigeon hook session-start <codex|claude-code|antigravity>\n       pigeon hook <turn-start|turn-end> antigravity\n       pigeon hook wait claude-code [--timeout <seconds>] [--renew]", "Handle the plugins' session hooks (used by the plugins)",
            "All read the host's hook event from standard input and use only its session ID.\nsession-start rebinds a known session and re-arms a wake that gave up. turn-start and turn-end report an Antigravity turn, so a wake is sent only while the conversation is idle. wait keeps a Claude Code conversation reachable: it exits 2 with a fixed wake line when a message arrives.", Internal: true),
        new("sidecar", "pigeon sidecar antigravity", "Wake idle Antigravity conversations (used by the plugin's sidecar)",
            "Runs inside the Antigravity App as the Pigeon plugin's sidecar and sends the fixed wake instruction with agentapi send-message.", Internal: true)
    ];

    // Validate before connecting to a service or reading message input.
    public static bool TryHandle(string[] args, out int exitCode) {
        exitCode = 0;
        if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "-h" or "help")) {
            Root(Console.Out);
            return true;
        }

        if (args.Length == 1 && args[0] is "--version" or "-V") {
            var version = typeof(CliHelp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Console.WriteLine(version.Split('+')[0]);
            return true;
        }

        var command = Commands.FirstOrDefault(c => c.Name == (args[0] == "help" && args.Length == 2 ? args[1] : args[0]));
        if (command is null)
            return Error(args[0] == "help" ? "Expected a command after 'help'." : $"Unknown command '{args[0]}'.", "pigeon --help", out exitCode);
        if (args[0] == "help" || (args.Length == 2 && args[1] is "--help" or "-h")) {
            Console.WriteLine($"{command.Summary}.\n\nUsage: {command.Usage}\n\n{command.Details}");
            return true;
        }

        if (Valid(args))
            return false;
        // An asyncRewake hook would show exit code 2 and this text to the model as a wake.
        _ = Error($"Invalid arguments for '{args[0]}'.\nUsage: {command.Usage}", $"pigeon {args[0]} --help", out exitCode);
        if (args[0] == "hook")
            exitCode = 1;
        return true;
    }

    private static bool Valid(string[] a) => a[0] switch {
        "start" => a.Length == 1 || (a.Length == 2 && a[1] is "--foreground" or "--background"),
        "stop" or "status" or "topics" => a.Length == 1,
        "join" => a.Length == 6 && a[2] == "as" && a[4] == "using" && Values(a[1], a[3], a[5]),
        "leave" => a.Length == 4 && a[2] == "as" && Values(a[1], a[3]),
        "send" => a.Length == 6 && a[1] == "from" && a[3] == "to" && Values(a[2], a[4]) && !string.IsNullOrWhiteSpace(a[5]),
        "receive" => a.Length == 5 && a[1] == "for" && a[3] == "from" && Values(a[2], a[4]),
        "mcp" => a.Length == 1 || (a.Length == 2 && a[1] == "--codex-queue"),
        "peer" => a.Length >= 2 && a[1] switch {
            "name" => a.Length is 2 or 3 && Values(a[2..]),
            "listen" => a.Length == 3 && Values(a[2]),
            "invite" or "list" => a.Length == 2,
            "accept" or "confirm" => (a.Length == 3 && Values(a[2])) || (a.Length == 5 && a[3] == "as" && Values(a[2], a[4])),
            "remove" => a.Length == 3 && Values(a[2]),
            "address" => a.Length == 4 && Values(a[2], a[3]),
            _ => false
        },
        "topic" => (a.Length is 5 or 6 && a[1] == "share" && a[3] == "with" && Values(a[2], a[4]) && (a.Length == 5 || a[5] == "--no-tool")) ||
            (a.Length == 3 && a[1] == "unshare" && Values(a[2])),
        "sidecar" => a.Length == 2 && a[1] == "antigravity",
        "hook" => (a.Length == 3 && a[1] == "session-start" && a[2] is "codex" or "claude-code" or "antigravity") ||
            (a.Length == 3 && a[1] is "turn-start" or "turn-end" && a[2] == "antigravity") ||
            (a.Length >= 3 && a[1] == "wait" && a[2] == "claude-code" && (a.Length == 3 || (a.Length == 4 && a[3] == "--renew") || ((a.Length == 5 || (a.Length == 6 && a[5] == "--renew")) && a[3] == "--timeout" && int.TryParse(a[4], out var seconds) && seconds is >= 10 and <= 86400))),
        _ => false
    };
    private static bool Values(params string[] values) => values.All(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith("--", StringComparison.Ordinal));

    private static bool Error(string message, string help, out int exitCode) {
        Console.Error.WriteLine("Error: " + message);
        Console.Error.WriteLine($"Run '{help}' for usage.");
        exitCode = 2;
        return true;
    }

    private static void Root(TextWriter writer) {
        writer.WriteLine("Pigeon - private conversations between AI agent sessions on this computer\n\nUsage: pigeon <command> [arguments]\n\nCommands:");
        foreach (var c in Commands.Where(c => !c.Internal))
            writer.WriteLine($"  {c.Usage.Split('\n')[0],-56} {c.Summary}");
        writer.WriteLine("\nUsed by the plugins:");
        foreach (var c in Commands.Where(c => c.Internal))
            writer.WriteLine($"  {"pigeon " + c.Name,-56} {c.Summary.Replace(" (used by the plugins)", "")}");
        writer.WriteLine("\nOptions:\n  -h, --help      Show help (also 'pigeon <command> --help')\n  -V, --version   Show the version");
        writer.WriteLine("\nA topic is a private conversation between two sessions. A session ID is the one your\nagent tool assigned; Pigeon does not verify it, and any program on this computer can use Pigeon.\nTo reach agents on other machines, pair with 'pigeon peer' and share topics with 'pigeon topic share'.");
    }
}
