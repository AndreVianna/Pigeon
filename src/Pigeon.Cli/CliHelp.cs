using System.Reflection;

internal static class CliHelp {
    private static readonly string[] Commands = ["start", "stop", "status", "topics", "sessions", "messages", "mcp", "hook"];
    private static readonly string[] SendOptions = ["--correlation-id", "--idempotency-key", "--expires-at-unix-ms", "--reply-to", "--topic"];

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
        if (args[0] == "help") {
            if (args.Length == 2 && Commands.Contains(args[1], StringComparer.Ordinal)) {
                Command(Console.Out, args[1]);
                return true;
            }
            return Error("Expected a valid command after 'help'.", out exitCode);
        }
        if (!Commands.Contains(args[0], StringComparer.Ordinal))
            return Error("Unknown command or option.", out exitCode);
        if (args.Length == 2 && args[1] is "--help" or "-h") {
            Command(Console.Out, args[0]);
            return true;
        }
        if (args.Length == 3 && args[0] == "messages" && args[2] is "--help" or "-h" && args[1] is "send" or "direct" or "send-session" or "get" or "ack" or "status" or "retry-wake" or "get-session" or "ack-session" or "status-session" or "retry-wake-session" or "wait") {
            MessageCommand(Console.Out, args[1]);
            return true;
        }
        if (args.Length == 3 && args[0] == "topics" && (args[2] is "--help" or "-h") && (args[1] is "create" or "delete" or "subscribe" or "unsubscribe" or "join" or "leave" or "sessions")) {
            TopicCommand(Console.Out, args[1]);
            return true;
        }

        var valid = args[0] switch {
            "start" => args.Length == 1 || (args.Length == 2 && args[1] is "--foreground" or "--background"),
            "stop" => args.Length == 1,
            "topics" => args.Length == 1 || (args.Length == 3 && args[1] is "create" or "delete" or "sessions") || (args.Length == 5 && args[1] is "join" or "leave") || (args.Length == 6 && args[1] == "subscribe") || (args.Length == 4 && args[1] == "unsubscribe"),
            "sessions" => (args.Length == 4 && args[1] == "candidates") || (args.Length == 5 && args[1] == "adopt"),
            "messages" => ValidMessages(args),
            "hook" => (args.Length == 3 && args[1] == "session-start" && args[2] is "codex" or "claude-code") ||
                (args.Length >= 3 && args[1] == "wait" && args[2] == "claude-code" && (args.Length == 3 || (args.Length == 4 && args[3] == "--renew") || ((args.Length == 5 || (args.Length == 6 && args[5] == "--renew")) && args[3] == "--timeout" && int.TryParse(args[4], out var seconds) && seconds is >= 10 and <= 86400))),
            "mcp" => args.Length == 1 || (args.Length == 2 && (args[1] is "--claude-channel" or "--codex-queue" || ValidPort(args[1]))) || (args.Length == 3 && ValidPort(args[1]) && args[2] is "--claude-channel" or "--codex-queue"),
            "status" => args.Length == 1 || (args.Length == 2 && ValidPort(args[1])),
            _ => false
        };
        if (!valid) {
            Console.Error.WriteLine($"Error: Invalid arguments for '{args[0]}'.");
            Console.Error.WriteLine($"Run 'pigeon {args[0]} --help' for usage.");
            // An asyncRewake hook would show exit code 2 and this text to the model as a wake.
            exitCode = args[0] == "hook" && args.Length > 1 && args[1] == "wait" ? 1 : 2;
            return true;
        }
        return false;
    }

    private static bool ValidMessages(string[] args) {
        if (args.Length < 2) return false;
        if (args[1] == "get-session") return args.Length is 4 or 5;
        if (args[1] == "retry-wake-session") return args.Length == 4;
        if (args[1] is "ack-session" or "status-session") return args.Length == 5;
        if (args[1] == "send-session") return args.Length == 7 || (args.Length == 9 && args[7] == "--topic" && !string.IsNullOrWhiteSpace(args[8]));
        if (args[1] == "wait") return (args.Length is 3 or 4 && args[2] == "--claude") || (args.Length == 4 && !args[2].StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(args[2]) && !string.IsNullOrWhiteSpace(args[3]));
        if (args[1] == "get") return args.Length is 3 or 4;
        if (args[1] == "retry-wake") return args.Length == 3;
        if (args[1] is "ack" or "status") return args.Length == 4;
        if (args[1] is not ("send" or "direct") || args.Length < 5 || (args.Length - 5) % 2 != 0) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 5; i < args.Length; i += 2) {
            var option = args[i];
            var value = args[i + 1];
            if (!SendOptions.Contains(option, StringComparer.Ordinal) || !seen.Add(option) || string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal) || (option is "--reply-to" or "--topic" && args[1] != "direct") || (option == "--expires-at-unix-ms" && !long.TryParse(value, out _))) return false;
        }
        return true;
    }
    internal static bool ValidPort(string value) => int.TryParse(value, out var port) && port is >= 1024 and <= 65535;
    private static bool Error(string message, out int exitCode) {
        Console.Error.WriteLine("Error: " + message);
        Console.Error.WriteLine("Run 'pigeon --help' for usage.");
        exitCode = 2;
        return true;
    }

    private static void Root(TextWriter writer) {
        writer.WriteLine("Pigeon - local messaging for agent sessions\n\nUsage: pigeon <command> [options]\n\nCommands:");
        writer.WriteLine("  start                Start the local messaging service");
        writer.WriteLine("  stop                 Stop the local messaging service");
        writer.WriteLine("  status               Show service, bridge, and pending-recipient status");
        writer.WriteLine("  topics               Manage project topics and subscriptions");
        writer.WriteLine("  sessions             Resolve legacy session identity conflicts");
        writer.WriteLine("  messages             Send, receive, acknowledge or inspect deliveries");
        writer.WriteLine("  mcp                  Connect an MCP client to the service");
        writer.WriteLine("  hook                 Handle agent session lifecycle events");
        writer.WriteLine("  help [command]       Show help for a command");
        writer.WriteLine("\nOptions:\n  -h, --help           Show help\n  -V, --version        Show version");
        writer.WriteLine("\nRun 'pigeon <command> --help' for command details.");
        writer.WriteLine("\nAgent integration:");
        writer.WriteLine("  Configured Codex and Claude Code sessions can poll for messages through MCP.");
        writer.WriteLine("  Claude can receive notifications when its session loads the Pigeon channel.");
        writer.WriteLine("  Codex queue notifications require explicit opt-in and an accessible session; acceptance is not confirmation.");
        writer.WriteLine("Messages and routing names are accessible to local processes on this computer.");
    }

    private static void Command(TextWriter writer, string command) {
        if (command == "topics") {
            writer.WriteLine("Manage project topics and subscriptions.\n\nUsage: pigeon topics [command]\n\nCommands:");
            writer.WriteLine("  create <name>                              Create a project topic");
        writer.WriteLine("  delete <name>                              Close a topic and notify subscribers");
            writer.WriteLine("  join <topic> <tool> <session-id>           Join using a tool/session identity");
            writer.WriteLine("  leave <topic> <tool> <session-id>          Remove a session's subscription");
            writer.WriteLine("  sessions <topic>                           List subscribed tool/session identities");
            writer.WriteLine("  subscribe <topic> <name> <tool> <session>   Register and subscribe an agent");
            writer.WriteLine("  unsubscribe <topic> <name>                 Remove a subscription");
            writer.WriteLine("\nRun without a command to list topics, or use 'pigeon topics <command> --help' for details.");
            return;
        }
        if (command == "messages") {
            writer.WriteLine("Send, retrieve, acknowledge and inspect deliveries.\n\nUsage: pigeon messages <command> [arguments]\n\nCommands:");
            writer.WriteLine("  send <sender> <topic> <message>         Publish to topic subscribers");
            writer.WriteLine("  direct <sender> <recipient> <message>   Send to one agent");
            writer.WriteLine("  send-session <tool> <session> <recipient-tool> <recipient-session> <message> [--topic <name>]");
            writer.WriteLine("  get-session <tool> <session> [topic]    Retrieve pending session deliveries");
            writer.WriteLine("  ack-session <tool> <session> <receipt>  Acknowledge a session delivery");
            writer.WriteLine("  status-session <tool> <session> <id>    Inspect a session delivery");
            writer.WriteLine("  retry-wake-session <tool> <session>     Re-arm a session wake");
            writer.WriteLine("  get <reader> [topic]                   Retrieve up to 20 deliveries");
            writer.WriteLine("  ack <reader> <receipt>                 Acknowledge a delivery");
            writer.WriteLine("  status <agent> <message-id>            Inspect a delivery and its attempts");
            writer.WriteLine("  retry-wake <reader>                    Re-arm a failed wake for pending messages");
            writer.WriteLine("  wait --claude [session-id]             Watch a Claude session without leasing messages");
            writer.WriteLine("\nRun 'pigeon messages <command> --help' for details.");
            return;
        }
        if (command == "sessions") {
            writer.WriteLine("Resolve old identities that claim the same tool and session.\n\nUsage: pigeon sessions <command>\n\nCommands:\n  candidates <tool> <session-id>              Inspect old names and pending counts\n  adopt <tool> <session-id> <legacy-name>     Select one old identity for this route\n\nAdoption does not merge or delete other identities or messages.");
            return;
        }
        var (usage, description, details) = command switch {
            "start" => ("pigeon start [--foreground]", "Start the local messaging service.", "Runs in the background by default; --foreground keeps it attached to this terminal. PIGEON_PORT selects the local port and PIGEON_DATABASE the absolute data path."),
            "stop" => ("pigeon stop", "Stop the local messaging service.", "PIGEON_PORT selects the service; any local process can stop it."),
            "mcp" => ("pigeon mcp [port] [--claude-channel|--codex-queue]", "Run the MCP bridge used by an agent tool.", "The port defaults to PIGEON_PORT or 49372. Claude must load its channel. --codex-queue opts in to queue notifications for the registered Codex session; availability depends on the runtime."),
            "status" => ("pigeon status [port]", "Show service status and recently active MCP bridges.", "Bridge activity is not proof of agent session liveness. PIGEON_PORT selects the service."),
            "hook" => ("pigeon hook session-start <codex|claude-code>\n       pigeon hook wait claude-code [--timeout <seconds>] [--renew]", "Handle agent session lifecycle hooks.", "Both read the host hook event from stdin and use only its session ID. session-start restores a known session association; it never starts the service, creates a topic, or binds an MCP connection. wait is for a Claude Code async hook with asyncRewake: it waits for one wake of a connected session, writes the fixed wake instruction to stderr and exits 2. It exits 0 without output when the session is not connected, a newer waiter replaces it, or the timeout ends. The default is 570 seconds without --renew and 43200 seconds with it. With --renew, an active owner requests a model turn at the timeout to re-arm the hook; this is not a message and consumes model usage. While a monitor or channel owns the session's wakes it waits without claiming, and takes over when that notifier stops. It never leases messages."),
            _ => throw new ArgumentException("Unknown command", nameof(command))
        };
        writer.WriteLine(description + "\n\nUsage: " + usage + "\n\n" + details + "\n\nOptions:\n  -h, --help  Show help for this command");
    }
    private static void TopicCommand(TextWriter writer, string action) {
        if (action is "join" or "leave" or "sessions") {
            writer.WriteLine(action switch { "join" => "Usage: pigeon topics join <topic> <tool> <session-id>\n\nRecords a routing claim and subscription; it does not bind an MCP connection.", "leave" => "Usage: pigeon topics leave <topic> <tool> <session-id>\n\nRemoves the subscription and preserves queued deliveries.", _ => "Usage: pigeon topics sessions <topic>\n\nLists subscribed tool and session claims." });
            return;
        }
        var usage = action switch { "create" or "delete" => action + " <name>", "subscribe" => "subscribe <topic> <name> <tool> <session-id>", _ => "unsubscribe <topic> <name>" };
        writer.WriteLine("Usage: pigeon topics " + usage);
        writer.WriteLine(action switch { "delete" => "\nQueues a closure notice for each subscriber and removes subscriptions. Existing deliveries and message history are retained.", "subscribe" => "\nThe supplied session is a claim; registration does not verify or attach to the runtime.", "unsubscribe" => "\nAlready queued deliveries are retained.", _ => "\nCreates a project topic; an existing topic returns a warning." });
    }
    private static void MessageCommand(TextWriter writer, string action) {
        if (action == "send-session") {
            writer.WriteLine("Usage: pigeon messages send-session <sender-tool> <sender-session-id> <recipient-tool> <recipient-session-id> <message> [--topic <name>]\n\nBoth sessions must be registered and share the topic. Names and session IDs are routing claims, not authentication.");
            return;
        }
        if (action.EndsWith("-session", StringComparison.Ordinal)) {
            var syntax = action switch { "get-session" => "[topic]", "ack-session" => "<receipt>", "status-session" => "<message-id>", _ => "" };
            writer.WriteLine($"Usage: pigeon messages {action} <tool> <session-id> {syntax}\n\nUses the registered tool/session pair. Retrieving messages leases deliveries; acknowledge them after processing.");
            return;
        }
        if (action == "wait") {
            writer.WriteLine("Usage: pigeon messages wait --claude [session-id] | <legacy-reader> <session-id>\n\nWatches a Claude session without leasing messages. If session-id is omitted, --claude requires CLAUDE_CODE_SESSION_ID. A unique matching active registered MCP bridge is required. Prints a fixed wake instruction; it does not confirm receipt. Stop with Ctrl+C. PIGEON_PORT selects the service.");
            return;
        }
        if (action is "send" or "direct") {
            var destination = action == "send" ? "topic" : "recipient";
            writer.WriteLine($"Usage: pigeon messages {action} <sender> <{destination}> <message> [options]");
            writer.WriteLine("\nOptions:\n  --correlation-id <id>       Group related messages\n  --idempotency-key <key>     Deduplicate retries\n  --expires-at-unix-ms <time> Set expiry in Unix milliseconds");
            if (action == "direct") writer.WriteLine("  --reply-to <message-id>     Link a reply to a message");
            if (action == "direct") writer.WriteLine("  --topic <name>              Select a shared topic");
            writer.WriteLine("  -h, --help                  Show help for this command");
            writer.WriteLine("\nA fixed idempotency key also requires a fixed correlation ID and expiry. Use '-' for message input from stdin.");
        }
        else {
            var usage = action switch { "get" => "get <reader> [topic]", "ack" => "ack <reader> <receipt>", "retry-wake" => "retry-wake <reader>", _ => "status <agent> <message-id>" };
            writer.WriteLine("Usage: pigeon messages " + usage);
            writer.WriteLine(action switch { "get" => "\nRetrieval leases up to 20 deliveries; acknowledge each receipt after processing.", "retry-wake" => "\nRe-arms notifications for pending messages; older queued wakes can still arrive.", _ => "\nUse the routing name associated with the delivery." });
        }
    }
}
