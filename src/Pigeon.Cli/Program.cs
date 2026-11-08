using Pigeon.Adapters;
using Pigeon.Core;
using Pigeon.Service;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

if (CliHelp.TryHandle(args, out var helpExitCode))
    return helpExitCode;

if (args.Length > 0) {
    try {
        return await ServiceCommands.RunAsync(args);
    }
    catch (CommandException e) {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
    catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) {
        Console.Error.WriteLine($"The Pigeon service is not running on 127.0.0.1:{ServiceCommands.Port}. Start it with 'pigeon start'.");
        return 1;
    }
    catch (Exception e) when (e is ArgumentException or InvalidOperationException or UnauthorizedAccessException or JsonException or FormatException or StorageUnavailableException) {
        Console.Error.WriteLine("Pigeon command failed: " + e.Message);
        return 1;
    }
}

return 2;

// A rejection reported by the service, shown to the user as is.
internal sealed class CommandException(string message) : Exception(message);

internal static class ServiceCommands {
    public static string Port => Environment.GetEnvironmentVariable("PIGEON_PORT") ?? "29372";

    public static async Task<int> RunAsync(string[] args) {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new InvalidOperationException("Pigeon runs on Windows, macOS and Linux.");
        var port = Port;
        if (!int.TryParse(port, out var number) || number is < 1024 or > 65535)
            throw new ArgumentException("PIGEON_PORT must be a port number from 1024 to 65535.");
        if (args[0] == "hook" && args[1] == "wait") {
            var renew = args.Contains("--renew", StringComparer.Ordinal);
            var timeout = args.Length >= 5 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : renew ? ClaudeHookWait.DefaultRenewalSeconds : ClaudeHookWait.DefaultTimeoutSeconds;
            return await ClaudeHookWait.RunAsync(port, timeout, renew);
        }

        if (args[0] == "hook" && args[1] is "turn-start" or "turn-end")
            return await SessionHook.TurnAsync(port, args[2], args[1] == "turn-start" ? "busy" : "idle", args[1] == "turn-start" ? "{}" : "{\"decision\":\"allow\"}");
        if (args[0] == "hook")
            return await SessionHook.RunAsync(port, args[2]);
        if (args[0] == "sidecar") {
            // Antigravity gives its sidecars the language server binary that serves agentapi.
            var agentApi = Environment.GetEnvironmentVariable("ANTIGRAVITY_AGENTAPI_EXE");
            if (string.IsNullOrWhiteSpace(agentApi) || !Path.IsPathFullyQualified(agentApi) || !File.Exists(agentApi))
                throw new InvalidOperationException("ANTIGRAVITY_AGENTAPI_EXE is not set. This command runs as the Pigeon plugin's sidecar inside the Antigravity App.");
            // Antigravity does not stop its sidecars when it quits (seen in the 0.6.0-alpha.2 host
            // test), so the sidecar stops when the language server that started it exits.
            using var parent = MonitorParent.Capture();
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                stop.Cancel();
            };
            var watch = Task.Run(async () => {
                while (!stop.IsCancellationRequested && parent.IsAlive)
                    await Task.Delay(1000);
                stop.Cancel();
            });
            try {
                await new AntigravityWakeSidecar(port, agentApi).RunAsync(stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

            await watch;

            return 0;
        }

        if (args[0] == "start" && args.Length == 1)
            return await StartAsync(port);
        if (args[0] is "peer" or "requests" or "allow" or "deny")
            return await PeerCommands.RunAsync(port, args);
        if (args[0] == "stop") {
            var stopped = await LocalHttpClient.ExecuteAsync(port, "service_stop", new { });
            if (!stopped.GetProperty("Ok").GetBoolean())
                throw new InvalidOperationException();
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(5)) {
                try {
                    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                    _ = await LocalHttpClient.ExecuteAsync(port, "service_status", new { }, probeTimeout.Token);
                }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) {
                    Console.WriteLine("Pigeon service stopped.");
                    return 0;
                }

                await Task.Delay(100);
            }

            throw new TimeoutException();
        }

        if (args[0] == "start") {
            var background = args[^1] == "--background";
            if (background) {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
            }

            var defaultRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var configuredDatabase = Environment.GetEnvironmentVariable("PIGEON_DATABASE");
            if (configuredDatabase is null && (string.IsNullOrWhiteSpace(defaultRoot) || !Path.IsPathFullyQualified(defaultRoot)))
                throw new InvalidOperationException();
            if (configuredDatabase is not null && !Path.IsPathFullyQualified(configuredDatabase))
                throw new ArgumentException();
            var database = configuredDatabase ?? Path.Combine(defaultRoot, "Pigeon", "messages.db");
            long? retention = null;
            var retentionDays = Environment.GetEnvironmentVariable("PIGEON_BODY_RETENTION_DAYS");
            if (retentionDays is not null) {
                if (!int.TryParse(retentionDays, out var days) || days is < 1 or > 36500)
                    throw new ArgumentException();
                retention = checked(days * 86400000L);
            }

            var maxAttempts = ConfiguredLimit("PIGEON_MAX_ATTEMPTS", 3, 1, 100);
            var maxPerHour = ConfiguredLimit("PIGEON_MAX_MESSAGES_PER_HOUR", 60, 1, 10000);
            var abandonedMinutes = ConfiguredLimit("PIGEON_ABANDONED_TOPIC_MINUTES", 60, 1, 525600);
            var maxWakeAttempts = ConfiguredLimit("PIGEON_MAX_WAKE_ATTEMPTS", 3, 1, 100);
            var wakeRetrySeconds = ConfiguredLimit("PIGEON_WAKE_RETRY_SECONDS", 120, 1, 86400);
            using var guard = ServiceLock.TryAcquire(port) ?? throw new InvalidOperationException();
            // A background service on Unix must outlive the terminal it was started from.
            using var hangup = background && !OperatingSystem.IsWindows() ? PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => context.Cancel = true) : null;
            var parent = Path.GetDirectoryName(database);
            if (parent is null)
                throw new ArgumentException();
            if (configuredDatabase is null)
                Directory.CreateDirectory(parent);
            else if (!Directory.Exists(parent))
                throw new ArgumentException();
            using var broker = new Broker(database, bodyRetentionMilliseconds: retention, maxAttempts: maxAttempts, maxMessagesPerHour: maxPerHour, maxWakeAttempts: maxWakeAttempts, wakeRetryMilliseconds: wakeRetrySeconds * 1000L, abandonedTopicMilliseconds: abandonedMinutes * 60000L);
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                stop.Cancel();
            };
            // The identity toward peers lives in the user's certificate store; PIGEON_IDENTITY_DIR
            // keeps it in a folder instead, for tests and isolated runs.
            var identityDirectory = Environment.GetEnvironmentVariable("PIGEON_IDENTITY_DIR");
            if (identityDirectory is not null && !Path.IsPathFullyQualified(identityDirectory))
                throw new ArgumentException("PIGEON_IDENTITY_DIR must be a full path.");
            Pigeon.Service.Peers.IIdentityStore identityStore = identityDirectory is not null
                ? new Pigeon.Service.Peers.FileIdentityStore(identityDirectory)
                : new Pigeon.Service.Peers.UserCertificateStore(Path.Combine(parent, "identity.thumbprint"));
            using var peers = new Pigeon.Service.Peers.PeerHost(broker, identityStore);
            var host = new LocalHttpServer(port, broker, stop.Cancel, peers);
            var peerWork = peers.RunAsync(stop.Token);
            var server = host.RunAsync(stop.Token);
            if (await Task.WhenAny(server, host.Ready) == server)
                await server;
            if (!background) {
                Console.WriteLine("Pigeon service ready. Press Enter or Ctrl+C to stop.");
                var input = Console.In.ReadLineAsync();
                if (await Task.WhenAny(server, input) == input)
                    stop.Cancel();
            }

            await server;
            await stop.CancelAsync();
            await peerWork;

            return 0;
        }

        if (args[0] == "mcp") {
            var runtime = Environment.GetEnvironmentVariable("PIGEON_RUNTIME");
            if (runtime is not (null or "codex" or "claude-code" or "antigravity" or "opencode" or "copilot"))
                throw new ArgumentException("PIGEON_RUNTIME must be codex, claude-code, antigravity, opencode or copilot.");
            // The isolated Codex app-server did not provide a thread ID to its MCP child.
            // Do not trust a potentially inherited ID from some other Codex session.
            var autoSession = runtime switch {
                "claude-code" => Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID"),
                // Set by the Copilot runtime for each session's own MCP server (seen in VS Code 1.145).
                "copilot" => Environment.GetEnvironmentVariable("COPILOT_AGENT_SESSION_ID"),
                _ => null
            };
            // Claude sessions started by the Claude desktop app advertise prompts but cancel them unseen.
            var desktopClaude = runtime == "claude-code" && Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT") == "claude-desktop";
            var session = new McpSession(port, runtime, autoSession, hostShowsPrompts: !desktopClaude, dialogs: NativeDialog.ForThisMachine());
            var codexQueue = args.Length == 2 ? new CodexQueueNotifier(port, () => session.SessionId) : null;
            await McpStdio.RunAsync(Console.In, Console.Out, session, nextNotification: codexQueue is null ? null : codexQueue.TickAsync);
            return 0;
        }

        if (args[0] == "status")
            return await StatusAsync(port);
        if (args[0] == "topics" && args.Length == 3) {
            var remote = await Call(port, "peer_topics", new { machine = args[2] }, slow: true);
            var open = remote.GetProperty("topics").EnumerateArray().Select(t => t.GetString()).ToArray();
            Console.WriteLine(open.Length == 0 ? $"{args[2]} has no topic open to other machines." : string.Join("\n", open.Select(t => $"{t}@{args[2]}")));
            return 0;
        }

        if (args[0] == "topics") {
            var topics = (await Call(port, "topics", new { })).EnumerateArray().ToArray();
            if (topics.Length == 0)
                Console.WriteLine("No topics.");
            foreach (var topic in topics) {
                var members = topic.GetProperty("Members").EnumerateArray().Select(m => $"{m.GetProperty("Tool").GetString()} {m.GetProperty("SessionId").GetString()}").ToArray();
                var partnerText = members.Length < 2 ? " (waiting for a partner)" : "";
                var open = topic.TryGetProperty("OpenToOtherMachines", out var o) && o.GetBoolean() ? " | open to other machines" : "";
                Console.WriteLine($"{topic.GetProperty("Topic").GetString()}: {string.Join(" and ", members)}{partnerText} | {topic.GetProperty("Waiting").GetInt32()} waiting{open}");
            }

            return 0;
        }

        if (args[0] == "join") {
            var topic = await Call(port, "join", new { topic = args[1], sessionId = args[3], tool = args[5], remote = args.Contains("--remote"), approval = args.Contains("--approval") });
            if (topic.TryGetProperty("pending", out _)) {
                Console.WriteLine($"{topic.GetProperty("text").GetString()!.Replace(" Do not call pigeon_join again; you will be told the outcome.", "")}\nRequest {topic.GetProperty("request").GetString()}: see pigeon requests.");
                return 0;
            }

            var partner = topic.GetProperty("Members").EnumerateArray().FirstOrDefault(m => m.GetProperty("SessionId").GetString() != args[3]);
            Console.WriteLine(partner.ValueKind == JsonValueKind.Object
                ? $"Joined {args[1]} with {partner.GetProperty("Tool").GetString()} {partner.GetProperty("SessionId").GetString()}."
                : $"Joined {args[1]}. Waiting for a partner.");
            return 0;
        }

        if (args[0] == "leave") {
            var left = await Call(port, "leave", new { topic = args[1], sessionId = args[3] });
            Console.WriteLine(left.GetProperty("deleted").GetBoolean() ? $"Left {args[1]}. Nobody is left, so the topic was deleted." : $"Left {args[1]}.");
            return 0;
        }

        if (args[0] == "send") {
            var message = args[5] == "-" ? await ReadBodyAsync() : args[5];
            var sent = await Call(port, "send", new { sessionId = args[2], topic = args[4], message, retryKey = Guid.NewGuid().ToString("N") });
            Console.WriteLine(sent.GetProperty("text").GetString());
            return 0;
        }

        if (args[0] == "receive") {
            var messages = (await Call(port, "take", new { sessionId = args[2], topic = args[4] })).EnumerateArray().ToArray();
            if (messages.Length == 0)
                Console.WriteLine("No new messages.");
            foreach (var m in messages) {
                var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(m.GetProperty("SentAtUnixMs").GetInt64());
                Console.WriteLine($"[{sentAt:u}] {m.GetProperty("SenderTool").GetString()} {m.GetProperty("SenderSessionId").GetString()}: {m.GetProperty("Body").GetString()}");
            }

            return 0;
        }

        return 2;
    }

    // Calls the service and turns its rejection into a message for the user.
    private static async Task<JsonElement> Call(string port, string operation, object args, bool slow = false) {
        var result = await LocalHttpClient.ExecuteAsync(port, operation, args, slow);
        if (!result.GetProperty("Ok").GetBoolean()) {
            var error = result.GetProperty("Error").GetString();
            throw new CommandException(error is null or "invalid request" ? "The service rejected the request." : error);
        }

        return result.GetProperty("Value");
    }

    private static async Task<int> StatusAsync(string port) {
        try {
            var health = await LocalHttpClient.ExecuteAsync(port, "service_status", new { });
            // Older services do not report a version.
            var serviceVersion = health.GetProperty("Value").TryGetProperty("version", out var reported) ? reported.GetString() : null;
            Console.WriteLine(serviceVersion == PigeonVersion.Current
                ? $"Pigeon service: running, version {serviceVersion}"
                : $"Pigeon service: running, version {serviceVersion ?? "unknown (older)"}; this CLI is {PigeonVersion.Current}. Restart the service after updating the CLI.");
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) {
            Console.Error.WriteLine($"Pigeon service: not running on 127.0.0.1:{port}.");
            return 1;
        }

        var all = (await Call(port, "bridges", new { })).EnumerateArray().ToArray();
        var sidecar = all.Any(b => b.GetProperty("mode").GetString() == AntigravityWakeSidecar.Mode);
        var openCodeWaker = all.Any(b => b.GetProperty("mode").GetString() == "opencode-prompt");
        bool CopilotExtension(string? session) => all.Any(b => b.GetProperty("mode").GetString() == "copilot-extension" && b.GetProperty("sessionId").ValueKind == JsonValueKind.String && b.GetProperty("sessionId").GetString() == session);
        var bridges = all.Where(b => b.GetProperty("mode").GetString() is "mcp" or "codex-queue").ToArray();
        var joined = bridges.Where(b => b.GetProperty("sessionId").ValueKind == JsonValueKind.String).ToArray();
        Console.WriteLine($"Connected sessions: {joined.Length} (and {bridges.Length - joined.Length} agent connections not in a topic)");
        foreach (var bridge in joined) {
            var tool = bridge.GetProperty("tool").GetString();
            var topics = string.Join(", ", bridge.GetProperty("topics").EnumerateArray().Select(t => t.GetString()));
            var notification = "";
            if (tool == "claude-code") {
                var notifier = bridge.GetProperty("notifier");
                var owner = notifier.GetProperty("active").GetString();
                var last = notifier.GetProperty("lastRelease");
                notification = owner is not null ? $" | notification: {owner}"
                    // A hook waiter that ended leaves the session without a wake path until its next turn.
                    : last.ValueKind == JsonValueKind.Object
                    ? $" | notification: none (last {last.GetProperty("mode").GetString()} {last.GetProperty("reason").GetString()} at {DateTimeOffset.FromUnixTimeMilliseconds(last.GetProperty("atUnixMs").GetInt64()):u})"
                    // The plugin's hook may not be running, or the installed CLI may not support its arguments.
                    : " | notification: none (no notifier seen since the service started; check the Pigeon plugin and that the CLI supports its hook)";
            }
            else if (tool == "antigravity")
                notification = sidecar ? " | notification: Antigravity sidecar" : " | notification: none (the Pigeon wake sidecar is not running; enable it in Antigravity's Automations)";
            else if (tool == "copilot")
                notification = CopilotExtension(bridge.GetProperty("sessionId").GetString()) ? " | notification: Copilot extension" : " | notification: none (the Pigeon Copilot extension is not reporting for this session; check that the plugin is installed)";
            else if (tool == "opencode")
                notification = openCodeWaker ? " | notification: OpenCode plugin" : " | notification: none (the Pigeon OpenCode plugin is not reporting; check that it is installed and OpenCode is running)";
            Console.WriteLine($"  {tool} {bridge.GetProperty("sessionId").GetString()} | topics: {topics} | wake: {bridge.GetProperty("wake").GetProperty("State").GetString()}{notification}");
        }

        var pending = (await Call(port, "wake_states", new { })).EnumerateArray().ToArray();
        Console.WriteLine($"Sessions with waiting messages: {pending.Length}");
        foreach (var item in pending) {
            var wake = item.GetProperty("Wake");
            var next = wake.GetProperty("NextAttemptUnixMs");
            var nextText = next.ValueKind == JsonValueKind.Null ? "none" : DateTimeOffset.FromUnixTimeMilliseconds(next.GetInt64()).ToString("u");
            var waiting = wake.GetProperty("Queued").GetInt32() + wake.GetProperty("Leased").GetInt32();
            Console.WriteLine($"  {item.GetProperty("Tool").GetString()} {item.GetProperty("SessionId").GetString()} | waiting: {waiting} | wake: {wake.GetProperty("State").GetString()} | attempts since progress: {wake.GetProperty("Attempts").GetInt32()} | last transport: {wake.GetProperty("LastOutcome").GetString() ?? "none"} | next attempt: {nextText}");
        }

        // Other machines appear only once they are used; 'pigeon peer list' has the details.
        var peers = await LocalHttpClient.ExecuteAsync(port, "peer_status", new { });
        if (peers.GetProperty("Ok").GetBoolean()) {
            var overview = peers.GetProperty("Value");
            var paired = overview.GetProperty("peers").EnumerateArray().ToArray();
            var addresses = overview.GetProperty("addresses").EnumerateArray().Select(e => e.GetString()).ToArray();
            if (paired.Length != 0 || addresses.Length != 0) {
                Console.WriteLine($"Known machines: {paired.Length} | listening: {(addresses.Length == 0 ? "off" : string.Join(", ", addresses))}");
                if (overview.GetProperty("requests").GetInt32() is > 0 and var requests)
                    Console.WriteLine($"Requests waiting: {requests} (pigeon requests)");
                foreach (var peer in paired) {
                    var contact = peer.GetProperty("LastContactUnixMs");
                    Console.WriteLine($"  {peer.GetProperty("Name").GetString()} | last contact: {(contact.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeMilliseconds(contact.GetInt64()).ToString("u") : "never")} | waiting to send: {peer.GetProperty("outbox").GetInt32()}");
                }
            }
        }

        return 0;
    }

    private static async Task<int> StartAsync(string port) {
        try {
            using var shortWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var existing = await LocalHttpClient.ExecuteAsync(port, "service_status", new { }, shortWait.Token);
            if (existing.GetProperty("Ok").GetBoolean()) {
                Console.WriteLine("Pigeon service is already running.");
                return 0;
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }

        var executable = Environment.ProcessPath ?? throw new InvalidOperationException();
        // On Windows the shell starts it without a window. Elsewhere shell execution would open
        // the program with the desktop's file handler, so it starts directly, detached from the
        // terminal's input and output.
        var windows = OperatingSystem.IsWindows();
        var info = new ProcessStartInfo(executable) {
            UseShellExecute = windows,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = !windows,
            RedirectStandardOutput = !windows,
            RedirectStandardError = !windows,
            WorkingDirectory = Environment.CurrentDirectory
        };
        // Run with `dotnet pigeon.dll` (development only), the service needs the assembly again; a
        // single-file build never runs under dotnet, so the empty Location there is never used.
#pragma warning disable IL3000
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
#pragma warning restore IL3000
        info.ArgumentList.Add("start");
        var database = Environment.GetEnvironmentVariable("PIGEON_DATABASE");
        if (database is not null) {
            if (!Path.IsPathFullyQualified(database))
                throw new ArgumentException();
        }

        info.ArgumentList.Add("--background");
        using var process = Process.Start(info) ?? throw new InvalidOperationException();
        try {
            using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!ready.IsCancellationRequested && !process.HasExited) {
                try {
                    var response = await LocalHttpClient.ExecuteAsync(port, "service_status", new { }, ready.Token);
                    if (response.GetProperty("Ok").GetBoolean()) {
                        Console.WriteLine("Pigeon service started.");
                        return 0;
                    }
                }
                catch (IOException) { }
                catch (OperationCanceledException) when (!ready.IsCancellationRequested) { }

                await Task.Delay(100, ready.Token);
            }

            throw new InvalidOperationException("Pigeon service did not start.");
        }
        catch {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw;
        }
    }

    private static async Task<string> ReadBodyAsync() {
        const int maxLength = 16384;
        var buffer = new char[1024];
        var body = new StringBuilder();
        while (true) {
            var count = await Console.In.ReadAsync(buffer.AsMemory());
            if (count == 0)
                break;
            if (body.Length + count > maxLength)
                throw new ArgumentException("Message body is too long.");
            _ = body.Append(buffer, 0, count);
        }

        return body.ToString();
    }
    private static int ConfiguredLimit(string name, int fallback, int min, int max) {
        var input = Environment.GetEnvironmentVariable(name);
        if (input is null)
            return fallback;
        if (!int.TryParse(input, out var value) || value < min || value > max)
            throw new ArgumentException();
        return value;
    }
}
