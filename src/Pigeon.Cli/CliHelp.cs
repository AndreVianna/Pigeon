using System.Reflection;

internal static class CliHelp {
    private sealed record Command(string Name, string Usage, string Summary, string Details, bool Internal = false);
    private static readonly Command[] Commands = [
        new("start", "pigeon start [--foreground]", "Start the Pigeon service in the background",
            "The service keeps messages on this computer. --foreground keeps it attached to this terminal until you press Enter.\nPIGEON_PORT selects the local port (default 29372) and PIGEON_DATABASE the database file."),
        new("stop", "pigeon stop", "Stop the Pigeon service", "Messages already queued are kept for the next start."),
        new("status", "pigeon status", "Show whether the service runs, which sessions are connected, and what is waiting", "Connected means the session's tool is running and reachable; it does not prove that a conversation is active."),
        new("topics", "pigeon topics [on <machine>]", "List topics, their members and waiting messages",
            "A topic is a private conversation between two sessions. 'on <machine>' lists the topics a known machine has open to other machines."),
        new("join", "pigeon join <topic> as <session-id> using <tool> [--remote] [--approval]", "Join a topic, creating it if it does not exist",
            "The first session to join creates the topic and waits for a partner; the second completes it. A third is refused.\n<tool> is the program the session belongs to, for example claude-code, codex or antigravity.\n\n--remote opens a new topic to other machines (once 'pigeon peer listen' is on). Someone there joins it as <topic>@<machine>, or <topic>@<address> the first time, and a person here allows it.\n--approval makes a local joiner give a join code that a person here enters.\nA topic on another machine is joined as <topic>@<machine>; the request waits for the person there (see 'pigeon requests').\n\nExamples:\n  pigeon join parser-review as 01a19407-57ce using codex\n  pigeon join project-1@192.168.2.20 as 01a19407-57ce using codex"),
        new("leave", "pigeon leave <topic> as <session-id>", "Leave a topic",
            "Your unread messages wait for whoever takes your place. The last session to leave deletes the topic.\n\nExample:\n  pigeon leave parser-review as 01a19407-57ce"),
        new("send", "pigeon send from <session-id> to <topic> \"<message>\"", "Send a message to the other member of a topic",
            "The message waits in order until the recipient reads it, for up to 24 hours, also while nobody else is in the topic yet.\nUse - as the message to read it from standard input.\n\nExample:\n  pigeon send from 01a19407-57ce to parser-review \"Ready for review\""),
        new("receive", "pigeon receive for <session-id> from <topic>", "Read the messages waiting for a session in a topic",
            "Receiving a message removes it from the queue.\n\nExample:\n  pigeon receive for b1f82365-c3a4 from parser-review"),
        new("peer", "pigeon peer <name|listen|list|remove|address> ...", "Connect with Pigeon on other machines",
            "Usage:\n  pigeon peer name [<name>]                       Name this machine for others (default: its host name)\n  pigeon peer listen [<address>[:<port>] | off]   Let other machines reach this one: on its LAN and Tailscale\n                                                  addresses, or on one address (port 29374 by default)\n  pigeon peer list                                Show known machines and topics with them\n  pigeon peer remove <name>                       Forget a machine; its topics end at once\n  pigeon peer address <name> <address>[:<port>]   Update where a machine is reached\n\nMachines talk over TLS and pin each other's certificate after the first join, which needs a join code that the two people pass between them. A machine reaches only the topics it was allowed to join."),
        new("requests", "pigeon requests [--show <request>]", "Show join requests waiting for an answer",
            "Requests to join your topics, and your own requests to join topics elsewhere. --show prints the join code of your own request, to give to the person at the other machine. Pigeon also asks in the agent's app when the app supports it."),
        new("allow", "pigeon allow <request> [<code>]", "Allow a join request, or confirm contacting a new machine",
            "A new machine, or a local topic that requires approval, needs the join code the other person gave you.\n\nExample:\n  pigeon allow 3f2a9c01b7de 123456"),
        new("deny", "pigeon deny <request>", "Refuse a join request", "The seat stays free; the other side is told it was not allowed."),
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
        "stop" or "status" => a.Length == 1,
        "join" => a.Length is >= 6 and <= 8 && a[2] == "as" && a[4] == "using" && Values(a[1], a[3], a[5]) && a[6..].All(o => o is "--remote" or "--approval") && a[6..].Distinct().Count() == a.Length - 6,
        "topics" => a.Length == 1 || (a.Length == 3 && a[1] == "on" && Values(a[2])),
        "requests" => a.Length == 1 || (a.Length == 3 && a[1] == "--show" && Values(a[2])),
        "allow" => a.Length is 2 or 3 && Values(a[1..]),
        "deny" => a.Length == 2 && Values(a[1]),
        "leave" => a.Length == 4 && a[2] == "as" && Values(a[1], a[3]),
        "send" => a.Length == 6 && a[1] == "from" && a[3] == "to" && Values(a[2], a[4]) && !string.IsNullOrWhiteSpace(a[5]),
        "receive" => a.Length == 5 && a[1] == "for" && a[3] == "from" && Values(a[2], a[4]),
        "mcp" => a.Length == 1 || (a.Length == 2 && a[1] == "--codex-queue"),
        "peer" => a.Length >= 2 && a[1] switch {
            "name" or "listen" => a.Length is 2 or 3 && Values(a[2..]),
            "list" => a.Length == 2,
            "remove" => a.Length == 3 && Values(a[2]),
            "address" => a.Length == 4 && Values(a[2], a[3]),
            _ => false
        },
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
        writer.WriteLine("\nA topic is a private conversation between two sessions. A session ID is the one your\nagent tool assigned; Pigeon does not verify it, and any program on this computer can use Pigeon.\nTo reach agents on other machines, turn on 'pigeon peer listen' and open a topic with --remote.");
    }
}
