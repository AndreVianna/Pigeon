using System.Collections.Concurrent;
using System.Text.Json;

using Pigeon.Service;

namespace Pigeon.UnitTests;

// Stands in for the service's local API: records every call and answers each operation as told.
// An operation with no answer gets an empty success.
internal sealed class FakeService {
    private readonly ConcurrentDictionary<string, Func<JsonElement, BrokerResult>> answers = new(StringComparer.Ordinal);
    private readonly List<(string Operation, JsonElement Args)> calls = [];
    // Thrown for every call instead of answering, as when the service cannot be reached.
    public Exception? Unreachable { get; set; }
    public event Action<string, JsonElement>? Called;

    public FakeService Answer(string operation, Func<JsonElement, object?> value) {
        answers[operation] = args => new BrokerResult(true, value(args), null);
        return this;
    }

    public FakeService Refuse(string operation, string error) {
        answers[operation] = _ => new BrokerResult(false, null, error);
        return this;
    }

    public (string Operation, JsonElement Args)[] Calls {
        get {
            lock (calls)
                return [.. calls];
        }
    }

    public JsonElement[] CallsTo(string operation) => [.. Calls.Where(c => c.Operation == operation).Select(c => c.Args)];

    public Task<JsonElement> Call(string operation, object args, bool slow, CancellationToken cancellation) {
        var json = JsonSerializer.SerializeToElement(args);
        lock (calls)
            calls.Add((operation, json));
        Called?.Invoke(operation, json);
        if (Unreachable is not null)
            throw Unreachable;
        var result = answers.TryGetValue(operation, out var answer) ? answer(json) : new BrokerResult(true, null, null);
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }

    // A real broker behind the same port, for tests that need the service's own rules.
    public static McpSession.ServiceCall For(Broker broker)
        => (operation, args, _, _) => Task.FromResult(JsonSerializer.SerializeToElement(broker.Execute(operation, JsonSerializer.SerializeToElement(args))));
}

// Input like a host's stdin read through the console: Read blocks its thread until a line is sent
// and ignores cancellation. Close ends the input.
internal sealed class HostInput : TextReader {
    private readonly BlockingCollection<int> characters = [];

    public void Send(string line) {
        foreach (var character in line + "\n")
            characters.Add(character);
    }

    public void End() => characters.Add(-1);

    public override int Read() {
        var next = characters.Take();
        if (next == -1)
            characters.Add(-1);
        return next;
    }

    public override int Peek() => throw new NotSupportedException();

    protected override void Dispose(bool disposing) {
        End();
        base.Dispose(disposing);
    }
}

// What the server writes to the host, one JSON-RPC message per line.
internal sealed class HostOutput : TextWriter {
    private readonly BlockingCollection<string> lines = [];
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) {
        lines.Add(buffer.ToString(), CancellationToken.None);
        return Task.CompletedTask;
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // The next line the server writes that matches, waiting at most ten seconds.
    public JsonElement Next(Func<JsonElement, bool> match) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true) {
            var line = lines.Take(timeout.Token);
            var json = JsonDocument.Parse(line).RootElement.Clone();
            if (match(json))
                return json;
        }
    }
}

internal static class McpHost {
    public const string Initialize = "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{\"elicitation\":{}}}}";
    public const string Initialized = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";

    // The servers' polls, without waiting a second between them.
    public static Task Quickly(TimeSpan _, CancellationToken cancellation) => Task.Delay(5, cancellation);

    public static async Task<McpSession> Ready(McpSession session) {
        _ = await session.HandleAsync(Initialize);
        _ = await session.HandleAsync(Initialized);
        return session;
    }

    public static string ToolCall(int id, string tool, object args) => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = tool, arguments = args } });

    // The text and error flag of a tool call's result.
    public static async Task<(bool Error, string Text)> Call(McpSession session, string tool, object args) {
        using var response = JsonDocument.Parse((await session.HandleAsync(ToolCall(1, tool, args)))!);
        var result = response.RootElement.GetProperty("result");
        return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }
}
