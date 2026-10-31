using Pigeon.Adapters;
using Pigeon.Core;
using Pigeon.Service;

using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

if (CliHelp.TryHandle(args, out var helpExitCode))
    return helpExitCode;

if (args.Length > 0 && args[0] is "start" or "stop" or "topics" or "sessions" or "messages" or "mcp" or "status" or "hook") {
    try {
        return await ServiceCommands.RunAsync(args);
    }
    catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or FormatException or TimeoutException or OperationCanceledException or StorageUnavailableException) {
        if (e is InvalidOperationException && e.Message.Contains("automatic migration cannot safely merge", StringComparison.Ordinal)) {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        Console.Error.WriteLine("Pigeon command failed; check configuration, topic and identity names, and local service availability.");
        return 1;
    }
}

return 2;

internal static class ServiceCommands {
    public static async Task<int> RunAsync(string[] args) {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException();
        var defaultPort = Environment.GetEnvironmentVariable("PIGEON_PORT") ?? "49372";
        var grouped = args[0] is "start" or "stop" or "topics" or "sessions" or "messages" or "hook";
        var port = grouped || (args[0] == "status" && args.Length == 1) || (args[0] == "mcp" && (args.Length == 1 || args[1].StartsWith("--", StringComparison.Ordinal))) ? defaultPort : args[1];
        if (!CliHelp.ValidPort(port))
            throw new ArgumentException();
        var project = Mailbox.TopicNamespace;
        var agent = "operator";
        var principal = new Principal(project, agent);
        if (args[0] == "hook" && args[1] == "wait") {
            var renew = args.Contains("--renew", StringComparer.Ordinal);
            var timeout = args.Length >= 5 ? int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : renew ? ClaudeHookWait.DefaultRenewalSeconds : ClaudeHookWait.DefaultTimeoutSeconds;
            return await ClaudeHookWait.RunAsync(port, timeout, renew);
        }
        if (args[0] == "hook")
            return await SessionHook.RunAsync(port, args[2]);
        if (args[0] == "messages" && args.Length >= 2 && args[1] == "wait")
            return await ClaudeMonitor.RunAsync(port, args.Skip(1).ToArray());
        if (args[0] == "start" && args.Length == 1)
            return await StartAsync(port, principal);
        if (args[0] == "stop") {
            var stopped = await LocalHttpClient.ExecuteAsync(port, principal, "service_stop", new { });
            if (!stopped.GetProperty("Ok").GetBoolean())
                throw new InvalidOperationException();
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(5)) {
                try {
                    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                    _ = await LocalHttpClient.ExecuteAsync(port, principal, "service_status", new { }, probeTimeout.Token);
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
            var maxAutomated = ConfiguredLimit("PIGEON_MAX_AUTOMATED", 4, 0, 100);
            var maxAutomatedPerMinute = ConfiguredLimit("PIGEON_MAX_AUTOMATED_PER_MINUTE", 100, 0, 1000);
            var maxWakeAttempts = ConfiguredLimit("PIGEON_MAX_WAKE_ATTEMPTS", 3, 1, 100);
            var wakeRetrySeconds = ConfiguredLimit("PIGEON_WAKE_RETRY_SECONDS", 120, 1, 86400);
            var lockName = "Local\\Pigeon-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(port)));
            using var guard = new Semaphore(1, 1, lockName);
            if (!guard.WaitOne(0))
                throw new InvalidOperationException();
            try {
                var parent = Path.GetDirectoryName(database);
                if (parent is null)
                    throw new ArgumentException();
                if (configuredDatabase is null)
                    Directory.CreateDirectory(parent);
                else if (!Directory.Exists(parent))
                    throw new ArgumentException();
                using var broker = new Broker(database, bodyRetentionMilliseconds: retention, maxAttempts: maxAttempts, maxAutomated: maxAutomated, maxAutomatedPerMinute: maxAutomatedPerMinute, topicMode: true, maxWakeAttempts: maxWakeAttempts, wakeRetryMilliseconds: wakeRetrySeconds * 1000L);
                using var stop = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => {
                    e.Cancel = true;
                    stop.Cancel();
                };
                var host = new LocalHttpServer(port, broker, stop.Cancel);
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
            }
            finally {
                _ = guard.Release();
            }

            return 0;
        }

        if (args[0] == "topics")
            return await TopicsAsync(port, project, args);
        if (args[0] == "sessions") {
            var operation = args[1] == "adopt" ? "session_adopt" : "session_candidates";
            object payload = args[1] == "adopt" ? new { tool = args[2], sessionId = args[3], legacyAgent = args[4] } : new { tool = args[2], sessionId = args[3] };
            var result = await LocalHttpClient.ExecuteAsync(port, new Principal(project, "operator"), operation, payload);
            if (!result.GetProperty("Ok").GetBoolean()) throw new InvalidOperationException();
            Console.WriteLine(result.GetProperty("Value").GetRawText());
            return 0;
        }
        if (args[0] == "messages")
            return await MessageAsync(port, project, args);
        if (args[0] == "mcp") {
            var channelEnabled = args[^1] == "--claude-channel";
            var codexQueueEnabled = args[^1] == "--codex-queue";
            var autoTool = Environment.GetEnvironmentVariable("PIGEON_RUNTIME");
            if (autoTool is not (null or "codex" or "claude-code")) throw new ArgumentException("Invalid PIGEON_RUNTIME");
            // The isolated Codex app-server did not provide a thread ID to its MCP child.
            // Do not trust a potentially inherited ID from some other Codex session.
            var autoSession = autoTool == "claude-code" ? Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID") : null;
            var session = new McpSession(port, new Principal(project, "unregistered-" + Guid.NewGuid().ToString("N")), channelEnabled ? ClaudeChannel.ExperimentalCapabilities : null, requireRegistrationName: true, autoRestoreTool: autoTool, autoRestoreSession: autoSession);
            var channel = channelEnabled ? new ClaudeChannel(port, () => session.CurrentPrincipal, () => session.ClaimedSessionId) : null;
            var codexQueue = codexQueueEnabled ? new CodexQueueNotifier(port, () => session.CurrentPrincipal, () => session.ClaimedSessionId) : null;
            Func<CancellationToken, Task<McpOutboundNotification?>>? notifications = channel is not null ? channel.NextAsync : codexQueue is not null ? codexQueue.TickAsync : null;
            await McpStdio.RunAsync(Console.In, Console.Out, session, nextNotification: notifications, notificationMode: codexQueueEnabled ? "codex-queue" : "claude-channel");
            return 0;
        }

        if (args[0] == "status") {
            try {
                var health = await LocalHttpClient.ExecuteAsync(port, principal, "service_status", new { });
                if (!health.GetProperty("Ok").GetBoolean())
                    throw new InvalidOperationException();
                // Services before 0.1.0 do not report a version.
                var serviceVersion = health.GetProperty("Value").TryGetProperty("version", out var reported) ? reported.GetString() : null;
                Console.WriteLine(serviceVersion == PigeonVersion.Current
                    ? $"Pigeon service: running, version {serviceVersion}"
                    : $"Pigeon service: running, version {serviceVersion ?? "unknown (older)"}; this CLI is {PigeonVersion.Current}. Restart the service after updating the CLI.");
            }
            catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) {
                Console.Error.WriteLine($"Pigeon service: unavailable on 127.0.0.1:{port}.");
                return 1;
            }
            var bridges = await LocalHttpClient.ExecuteAsync(port, principal, "bridges", new { });
            if (!bridges.GetProperty("Ok").GetBoolean()) throw new InvalidOperationException();
            var active = bridges.GetProperty("Value").EnumerateArray().ToArray();
            Console.WriteLine($"MCP bridges: {active.Length} recently active (agent session liveness not verified)");
            foreach (var bridge in active) {
                var binding = bridge.GetProperty("binding");
                var runtime = binding.ValueKind == JsonValueKind.Null ? "unregistered" : binding.GetProperty("Runtime").GetString();
                var sessionId = binding.ValueKind == JsonValueKind.Null ? "unknown" : binding.GetProperty("SessionId").GetString();
                var agentName = bridge.GetProperty("agent").GetString();
                var displayName = agentName is not null && agentName.StartsWith("session-", StringComparison.Ordinal) ? runtime : agentName;
                var topics = bridge.GetProperty("topics").EnumerateArray().Select(x => x.GetString());
                var wake = bridge.GetProperty("wake");
                var wakeState = wake.ValueKind == JsonValueKind.Null ? "unregistered" : wake.GetProperty("State").GetString();
                var restoration = bridge.TryGetProperty("restoration", out var restoreValue) ? restoreValue.GetString() : null;
                var mode = bridge.GetProperty("mode").GetString();
                var notifier = bridge.TryGetProperty("notifier", out var notifierValue) ? notifierValue : default;
                var owner = notifier.ValueKind == JsonValueKind.Object ? notifier.GetProperty("active").GetString() : null;
                var notification = "";
                if (binding.ValueKind == JsonValueKind.Object && mode == "mcp" && Mailbox.ToolName(runtime) == "claude-code") {
                    if (owner is not null) notification = $" | notification: {owner}";
                    else if (!active.Any(other => other.GetProperty("agent").GetString() == agentName && other.GetProperty("mode").GetString() is "claude-monitor" or "claude-channel" or "claude-hook")) {
                        var last = notifier.ValueKind == JsonValueKind.Object ? notifier.GetProperty("lastRelease") : default;
                        // A hook waiter that expired leaves the session without a wake path until its next turn.
                        notification = last.ValueKind == JsonValueKind.Object
                            ? $" | notification: none (last {last.GetProperty("mode").GetString()} {last.GetProperty("reason").GetString()} at {DateTimeOffset.FromUnixTimeMilliseconds(last.GetProperty("atUnixMs").GetInt64()):u})"
                            // No notifier since the service started: the plugin's hook may not be running,
                            // or the installed CLI may not support the hook's arguments (the wrapper stays silent).
                            : " | notification: none (no notifier seen since the service started; check the Pigeon plugin and that the CLI supports its hook)";
                    }
                }
                Console.WriteLine($"  {displayName} | {runtime} | session {sessionId} | topics: {string.Join(", ", topics)} | {mode} | wake: {wakeState}" + (restoration is null ? "" : $" | restore: {restoration}") + notification);
            }
            var wakes = await LocalHttpClient.ExecuteAsync(port, principal, "wake_states", new { });
            if (!wakes.GetProperty("Ok").GetBoolean()) throw new InvalidOperationException();
            var pending = wakes.GetProperty("Value").EnumerateArray().ToArray();
            Console.WriteLine($"Recipients with pending messages: {pending.Length} (first 100)");
            foreach (var item in pending) {
                var wake = item.GetProperty("Wake");
                var pendingAgent = item.GetProperty("Agent").GetString();
                var pendingTool = item.GetProperty("Tool").GetString();
                var pendingSession = item.GetProperty("SessionId").GetString();
                var pendingDisplay = pendingAgent is not null && pendingAgent.StartsWith("session-", StringComparison.Ordinal) ? $"{pendingTool} | session {pendingSession}" : pendingAgent;
                var outcome = wake.GetProperty("LastOutcome");
                var next = wake.GetProperty("NextAttemptUnixMs");
                var nextText = next.ValueKind == JsonValueKind.Null ? "none" : DateTimeOffset.FromUnixTimeMilliseconds(next.GetInt64()).ToString("u");
                Console.WriteLine($"  {pendingDisplay} | wake: {wake.GetProperty("State").GetString()} | attempts since progress: {wake.GetProperty("Attempts").GetInt32()} | last transport: {outcome.GetString() ?? "none"} | next eligible: {nextText} | queued: {wake.GetProperty("Queued").GetInt32()} | leased: {wake.GetProperty("Leased").GetInt32()}");
            }
            return 0;
        }
        return 2;
    }
    private static async Task<int> StartAsync(string port, Principal principal) {
        try {
            using var shortWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var existing = await LocalHttpClient.ExecuteAsync(port, principal, "service_status", new { }, shortWait.Token);
            if (existing.GetProperty("Ok").GetBoolean()) {
                Console.WriteLine("Pigeon service is already running.");
                return 0;
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }

        var executable = Environment.ProcessPath ?? throw new InvalidOperationException();
        var info = new ProcessStartInfo(executable) {
            UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.CurrentDirectory
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
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
                    var response = await LocalHttpClient.ExecuteAsync(port, principal, "service_status", new { }, ready.Token);
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

    private static async Task<int> TopicsAsync(string port, string project, string[] args) {
        var action = args.Length == 1 ? "list" : args[1];
        var principal = new Principal(project, action is "subscribe" or "unsubscribe" ? args[3] : "operator");
        var operation = action switch { "list" => "topics", "create" => "topic_create", "delete" => "topic_delete", "join" => "session_join", "leave" => "session_leave", "sessions" => "topic_sessions", "subscribe" => "topic_join", "unsubscribe" => "subscribe", _ => throw new ArgumentException() };
        object arguments = action switch {
            "list" => new { },
            "create" or "delete" => new { topic = args[2] },
            "join" => new { topic = args[2], tool = args[3], sessionId = args[4] },
            "leave" => new { topic = args[2], tool = args[3], sessionId = args[4] },
            "sessions" => new { topic = args[2] },
            "subscribe" => new { topic = args[2], runtime = args[4], sessionId = args[5] },
            "unsubscribe" => new { topic = args[2], subscribed = false },
            _ => throw new ArgumentException()
        };
        var result = await LocalHttpClient.ExecuteAsync(port, principal, operation, arguments);
        if (!result.GetProperty("Ok").GetBoolean() || (action == "delete" && result.GetProperty("Value").ValueKind == JsonValueKind.False))
            throw new InvalidOperationException();
        Console.WriteLine(result.GetProperty("Value").GetRawText());
        return 0;
    }

    private static async Task<int> MessageAsync(string port, string project, string[] args) {
        var principal = new Principal(project, args[2]);
        if (args[1] == "send-session") {
            var sender = await LocalHttpClient.ExecuteAsync(port, new Principal(project, "operator"), "session_restore", new { tool = args[2], sessionId = args[3] });
            if (!sender.GetProperty("Ok").GetBoolean() || sender.GetProperty("Value").ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
            principal = new Principal(project, sender.GetProperty("Value").GetProperty("Agent").GetString()!);
            var sessionSend = await LocalHttpClient.ExecuteAsync(port, principal, "session_send", new { recipientTool = args[4], recipientSessionId = args[5], body = args[6], topic = args.Length == 9 ? args[8] : null });
            if (!sessionSend.GetProperty("Ok").GetBoolean()) throw new InvalidOperationException();
            Console.WriteLine(sessionSend.GetProperty("Value").GetRawText());
            return 0;
        }
        if (args[1] is "get-session" or "ack-session" or "status-session" or "retry-wake-session") {
            var route = await LocalHttpClient.ExecuteAsync(port, new Principal(project, "operator"), "session_restore", new { tool = args[2], sessionId = args[3] });
            if (!route.GetProperty("Ok").GetBoolean() || route.GetProperty("Value").ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
            var internalAgent = route.GetProperty("Value").GetProperty("Agent").GetString()!;
            var legacyAction = args[1][..^"-session".Length];
            var mapped = args.Length == 5 ? new[] { "messages", legacyAction, internalAgent, args[4] } : new[] { "messages", legacyAction, internalAgent };
            return await MessageAsync(port, project, mapped);
        }
        if (args[1] == "retry-wake") {
            var result = await LocalHttpClient.ExecuteAsync(port, principal, "wake_retry", new { });
            if (!result.GetProperty("Ok").GetBoolean()) throw new InvalidOperationException();
            Console.WriteLine(result.GetProperty("Value").GetBoolean() ? "Wake retry scheduled." : "No pending messages to wake for.");
            return 0;
        }
        if (args[1] == "status") {
            var status = await LocalHttpClient.ExecuteAsync(port, principal, "status", new { messageId = args[3] });
            if (!status.GetProperty("Ok").GetBoolean())
                throw new InvalidOperationException();
            Console.WriteLine(status.GetProperty("Value").GetRawText());
            return 0;
        }
        if (args[1] == "ack") {
            var ack = await LocalHttpClient.ExecuteAsync(port, principal, "acknowledge", new { receipt = args[3] });
            if (!ack.GetProperty("Ok").GetBoolean() || !ack.GetProperty("Value").GetBoolean())
                throw new InvalidOperationException();
            Console.WriteLine("Acknowledged.");
            return 0;
        }
        if (args[1] == "get") {
            var messages = new List<JsonElement>();
            for (var i = 0; i < 20; i++) {
                var result = await LocalHttpClient.ExecuteAsync(port, principal, "receive", new { topic = args.Length == 4 ? args[3] : null });
                if (!result.GetProperty("Ok").GetBoolean())
                    throw new InvalidOperationException();
                var value = result.GetProperty("Value");
                if (value.ValueKind == JsonValueKind.Null)
                    break;
                messages.Add(value.Clone());
            }
            Console.WriteLine(JsonSerializer.Serialize(messages));
            return 0;
        }
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 5; i < args.Length; i += 2)
            options.Add(args[i], args[i + 1]);
        var suppliedCorrelation = options.GetValueOrDefault("--correlation-id") ?? Environment.GetEnvironmentVariable("PIGEON_CORRELATION_ID");
        var suppliedKey = options.GetValueOrDefault("--idempotency-key") ?? Environment.GetEnvironmentVariable("PIGEON_IDEMPOTENCY_KEY");
        var suppliedExpiry = options.GetValueOrDefault("--expires-at-unix-ms") ?? Environment.GetEnvironmentVariable("PIGEON_EXPIRES_AT_UNIX_MS");
        if (suppliedKey is not null && (suppliedCorrelation is null || suppliedExpiry is null))
            throw new ArgumentException("A retry key also requires a correlation ID and fixed expiry.");
        var correlation = suppliedCorrelation ?? Guid.NewGuid().ToString("N");
        var idempotency = suppliedKey ?? Guid.NewGuid().ToString("N");
        var ttl = ConfiguredLimit("PIGEON_MESSAGE_TTL_SECONDS", 86400, 1, 604800);
        var expiry = suppliedExpiry is null ? DateTimeOffset.UtcNow.AddSeconds(ttl).ToUnixTimeMilliseconds() : long.Parse(suppliedExpiry);
        var body = args[4] == "-" ? await ReadBodyAsync() : args[4];
        var direct = args[1] == "direct";
        object payload = direct
            ? new { recipient = args[3], body, correlationId = correlation, idempotencyKey = idempotency, expiresAtUnixMs = expiry, replyTo = options.GetValueOrDefault("--reply-to"), topic = options.GetValueOrDefault("--topic") }
            : new { topic = args[3], body, correlationId = correlation, idempotencyKey = idempotency, expiresAtUnixMs = expiry };
        var sent = await LocalHttpClient.ExecuteAsync(port, principal, direct ? "send" : "publish", payload);
        if (!sent.GetProperty("Ok").GetBoolean())
            throw new InvalidOperationException();
        Console.WriteLine(sent.GetProperty("Value").GetRawText());
        return 0;
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
