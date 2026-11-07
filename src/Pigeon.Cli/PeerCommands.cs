using System.Text.Json;

using Pigeon.Service;

// `pigeon peer …`, `pigeon requests`, `pigeon allow` and `pigeon deny`: other machines and the
// answers to join requests (see docs/remote-design.md). Agents use the MCP tools instead; these are
// for people at a terminal, and the fallback when a host cannot ask its person.
internal static class PeerCommands {
    private static async Task<JsonElement> Call(string port, string operation, object args, bool slow = false) {
        var result = await LocalHttpClient.ExecuteAsync(port, operation, args, slow);
        return result.GetProperty("Ok").GetBoolean()
            ? result.GetProperty("Value")
            : throw new CommandException(result.GetProperty("Error").GetString() ?? "The request failed.");
    }

    private static string Ago(JsonElement unixMs) => unixMs.ValueKind != JsonValueKind.Number ? "never"
        : (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs.GetInt64())) switch {
            { TotalMinutes: < 1 } => "just now",
            { TotalHours: < 1 } span => $"{(int)span.TotalMinutes} min ago",
            { TotalDays: < 1 } span => $"{(int)span.TotalHours} h ago",
            var span => $"{(int)span.TotalDays} days ago"
        };

    private static string Left(JsonElement unixMs) {
        var minutes = (int)Math.Ceiling((DateTimeOffset.FromUnixTimeMilliseconds(unixMs.GetInt64()) - DateTimeOffset.UtcNow).TotalMinutes);
        return minutes <= 1 ? "under a minute left" : $"{minutes} min left";
    }

    public static async Task<int> RunAsync(string port, string[] a) {
        switch (a[0]) {
            case "requests" when a.Length == 3: {
                var shown = await Call(port, "request_show", new { id = a[2] });
                Console.WriteLine($"Join code: {shown.GetProperty("code").GetString()}\nGive it to the person at {shown.GetProperty("to").GetString()}; the join completes when they enter it.");
                return 0;
            }
            case "requests": {
                var requests = await Call(port, "requests", new { });
                var incoming = requests.GetProperty("incoming").EnumerateArray().ToArray();
                var outgoing = requests.GetProperty("outgoing").EnumerateArray().ToArray();
                var local = requests.GetProperty("local").EnumerateArray().ToArray();
                if (incoming.Length + outgoing.Length + local.Length == 0)
                    Console.WriteLine("No requests are waiting.");
                foreach (var r in incoming)
                    Console.WriteLine($"{r.GetProperty("id").GetString()}  {r.GetProperty("from").GetString()} ({r.GetProperty("address").GetString()}) wants to join {r.GetProperty("topic").GetString()} with {r.GetProperty("tool").GetString()} | {Left(r.GetProperty("expires"))}\n" +
                        (r.GetProperty("needsCode").GetBoolean() ? $"    A new machine: allow with the code its person gives you: pigeon allow {r.GetProperty("id").GetString()} <code>" : $"    pigeon allow {r.GetProperty("id").GetString()}") + $"   or   pigeon deny {r.GetProperty("id").GetString()}");
                foreach (var r in local)
                    Console.WriteLine($"{r.GetProperty("id").GetString()}  {r.GetProperty("from").GetString()} wants to join {r.GetProperty("topic").GetString()}, which requires approval | {Left(r.GetProperty("expires"))}\n    pigeon allow {r.GetProperty("id").GetString()} <code>   or   pigeon deny {r.GetProperty("id").GetString()}   (the joiner's code: pigeon requests --show {r.GetProperty("id").GetString()})");
                foreach (var r in outgoing) {
                    var id = r.GetProperty("id").GetString();
                    Console.WriteLine($"{id}  joining {r.GetProperty("topic").GetString()}: " + r.GetProperty("state").GetString() switch {
                        "confirm" => $"a machine you have not joined before. Contact it: pigeon allow {id}   or refuse: pigeon deny {id}",
                        "contact" => "contacting it",
                        _ => r.GetProperty("hasCode").GetBoolean() ? $"waiting for the person there. Your join code: pigeon requests --show {id}" : "waiting for the person there to allow it"
                    } + $" | {Left(r.GetProperty("expires"))}");
                }

                return 0;
            }
            case "allow" or "deny": {
                var answer = await Call(port, a[0] == "allow" ? "request_allow" : "request_deny", new { id = a[1], code = a.Length == 3 ? a[2] : null });
                var result = answer.GetProperty("result").GetString()!;
                Console.WriteLine(result switch {
                    "allowed" => "Allowed. The other session is in the topic now.",
                    "contacting" => $"Contacting it. When it answers, your join code is shown by: pigeon requests --show {a[1]}",
                    "denied" or "refused" => "Refused.",
                    "wrong code" => "That code does not match. Check it with the other person and try again.",
                    _ => char.ToUpperInvariant(result[0]) + result[1..] + "."
                });
                return result is "wrong code" or "denied after 3 wrong codes" ? 1 : 0;
            }
        }

        switch (a[1]) {
            case "name": {
                var name = await Call(port, "peer_name", new { name = a.Length == 3 ? a[2] : null });
                Console.WriteLine($"This machine is \"{name.GetProperty("name").GetString()}\" to other machines.");
                return 0;
            }
            case "listen": {
                var listen = await Call(port, "peer_listen", new { address = a.Length == 3 ? a[2] : null });
                var addresses = listen.GetProperty("addresses").EnumerateArray().Select(e => e.GetString()).ToArray();
                Console.WriteLine(listen.GetProperty("listen").GetString() is null
                    ? "This machine no longer accepts other machines."
                    : $"Other machines can reach \"{listen.GetProperty("name").GetString()}\" at {string.Join(", ", addresses)}. Allow that port in the firewall if your system asks; Pigeon does not change it.\n" +
                      $"Someone on another machine joins one of your topics as <topic>@{addresses.FirstOrDefault()?.Split(':')[0]} the first time.");
                return 0;
            }
            case "remove": {
                var removed = await Call(port, "peer_remove", new { name = a[2] });
                Console.WriteLine(removed.GetProperty("removed").GetBoolean() ? $"Removed \"{a[2]}\"; its topics here ended." : $"No machine is named \"{a[2]}\".");
                return 0;
            }
            case "address": {
                var set = await Call(port, "peer_address", new { name = a[2], address = a[3] });
                Console.WriteLine($"\"{a[2]}\" is now reached at {set.GetProperty("address").GetString()}.");
                return 0;
            }
            default: {
                var status = await Call(port, "peer_status", new { });
                var addresses = status.GetProperty("addresses").EnumerateArray().Select(e => e.GetString()).ToArray();
                Console.WriteLine($"This machine: {status.GetProperty("name").GetString()} | listening: {(addresses.Length == 0 ? "off" : string.Join(", ", addresses))}");
                if (status.GetProperty("fingerprint").GetString() is { } fingerprint)
                    Console.WriteLine($"Fingerprint: {fingerprint}");
                var peers = status.GetProperty("peers").EnumerateArray().ToArray();
                Console.WriteLine($"Known machines: {peers.Length}");
                foreach (var p in peers)
                    Console.WriteLine($"  {p.GetProperty("Name").GetString()} at {p.GetProperty("Address").GetString() ?? "(no address)"} | last contact: {Ago(p.GetProperty("LastContactUnixMs"))} | waiting to send: {p.GetProperty("outbox").GetInt32()}");
                foreach (var topic in status.GetProperty("listed").EnumerateArray())
                    Console.WriteLine($"  open to other machines: {topic.GetString()}");
                foreach (var s in status.GetProperty("shares").EnumerateArray())
                    Console.WriteLine(s.GetProperty("Role").GetString() == "offer"
                        ? $"  {s.GetProperty("Topic").GetString()} with {s.GetProperty("PeerName").GetString()}"
                        : $"  {s.GetProperty("Topic").GetString()} (on {s.GetProperty("PeerName").GetString()})");
                if (status.GetProperty("requests").GetInt32() is > 0 and var waiting)
                    Console.WriteLine($"Requests waiting: {waiting} (pigeon requests)");
                return 0;
            }
        }
    }
}
