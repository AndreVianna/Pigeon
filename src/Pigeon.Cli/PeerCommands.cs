using System.Text.Json;

using Pigeon.Service;

// `pigeon peer …` and `pigeon topic share|unshare`: connecting to agents on other machines (see
// docs/remote-design.md). These are for people only; no agent tool offers them.
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

    public static async Task<int> RunAsync(string port, string[] a) {
        if (a[0] == "topic") {
            if (a[1] == "share") {
                _ = await Call(port, "topic_share", new { topic = a[2], peer = a[4], shareTool = !a.Contains("--no-tool") });
                var self = (await Call(port, "peer_status", new { })).GetProperty("name").GetString();
                Console.WriteLine($"Shared \"{a[2]}\" with {a[4]}. On that machine, an agent joins it as \"{a[2]}@{self}\"; it appears there once {a[4]} is reachable.");
            }
            else {
                var done = await Call(port, "topic_unshare", new { topic = a[2] });
                Console.WriteLine(done.GetProperty("unshared").GetBoolean() ? $"Stopped sharing \"{a[2]}\"." : $"\"{a[2]}\" is not a topic this machine shared.");
            }

            return 0;
        }

        switch (a[1]) {
            case "name": {
                var name = await Call(port, "peer_name", new { name = a.Length == 3 ? a[2] : null });
                Console.WriteLine(name.GetProperty("name").GetString() is { } current ? $"This service is \"{current}\" to its peers." : "This service has no name yet: pigeon peer name <name>, for example ana-laptop.");
                return 0;
            }
            case "listen": {
                var listen = await Call(port, "peer_listen", new { address = a[2] });
                Console.WriteLine(listen.GetProperty("listen").GetString() is { } address
                    ? $"Listening for paired peers on {address}. Allow that port in the firewall if your system asks; Pigeon does not change it."
                    : "The peer listener is off.");
                return 0;
            }
            case "invite": {
                var invite = await Call(port, "peer_invite", new { });
                Console.WriteLine($"Invitation from \"{invite.GetProperty("name").GetString()}\", valid for {invite.GetProperty("minutes").GetInt32()} minutes and one use:\n\n{invite.GetProperty("invitation").GetString()}\n");
                Console.WriteLine("Send it to the other person over a channel you trust. They run: pigeon peer accept <invitation>");
                Console.WriteLine("They then read you a 6-digit check code. Finish pairing with: pigeon peer confirm <code>");
                return 0;
            }
            case "accept": {
                var accept = await Call(port, "peer_accept", new { invitation = a[2], name = a.Length == 5 ? a[4] : null }, slow: true);
                var name = accept.GetProperty("name").GetString()!;
                Console.WriteLine($"Reached \"{accept.GetProperty("inviter").GetString()}\" at {accept.GetProperty("address").GetString()}; its certificate matches the invitation.");
                Console.WriteLine($"\nCheck code: {accept.GetProperty("check").GetString()}\n");
                Console.Write("Read this code to the person who invited you. Do they run 'pigeon peer confirm' with the same code? Pair only if so [y/N]: ");
                var answer = Console.In.ReadLine()?.Trim();
                if (answer is not ("y" or "Y" or "yes")) {
                    Console.WriteLine("Not paired.");
                    return 1;
                }

                _ = await Call(port, "peer_accept_confirm", new { fingerprint = accept.GetProperty("fingerprint").GetString(), name });
                Console.WriteLine($"Paired with \"{name}\" on this side. Pairing completes when they confirm the code.");
                return 0;
            }
            case "confirm": {
                var peer = await Call(port, "peer_confirm", new { check = a[2], name = a.Length == 5 ? a[4] : null });
                Console.WriteLine($"Paired with \"{peer.GetProperty("Name").GetString()}\". Share a topic with it: pigeon topic share <topic> with {peer.GetProperty("Name").GetString()}");
                return 0;
            }
            case "remove": {
                var removed = await Call(port, "peer_remove", new { name = a[2] });
                Console.WriteLine(removed.GetProperty("removed").GetBoolean() ? $"Removed \"{a[2]}\"; its shared topics ended here." : $"No peer is named \"{a[2]}\".");
                return 0;
            }
            case "address": {
                _ = await Call(port, "peer_address", new { name = a[2], address = a[3] });
                Console.WriteLine($"\"{a[2]}\" is now reached at {a[3]}.");
                return 0;
            }
            default: {
                var status = await Call(port, "peer_status", new { });
                Console.WriteLine($"This service: {status.GetProperty("name").GetString() ?? "(no name; pigeon peer name <name>)"} | listening: {status.GetProperty("listen").GetString() ?? "off"}");
                if (status.GetProperty("fingerprint").GetString() is { } fingerprint)
                    Console.WriteLine($"Fingerprint: {fingerprint}");
                var peers = status.GetProperty("peers").EnumerateArray().ToArray();
                Console.WriteLine($"Peers: {peers.Length}");
                foreach (var p in peers)
                    Console.WriteLine($"  {p.GetProperty("Name").GetString()} at {p.GetProperty("Address").GetString() ?? "(no address)"} | last contact: {Ago(p.GetProperty("LastContactUnixMs"))} | waiting to send: {p.GetProperty("outbox").GetInt32()}");
                foreach (var p in status.GetProperty("pending").EnumerateArray())
                    Console.WriteLine($"  {p.GetProperty("Name").GetString()} (accepted your invitation; confirm with: pigeon peer confirm <code they read you>)");
                foreach (var s in status.GetProperty("shares").EnumerateArray())
                    Console.WriteLine(s.GetProperty("Role").GetString() == "offer"
                        ? $"  shared {s.GetProperty("Topic").GetString()} with {s.GetProperty("PeerName").GetString()}{(s.GetProperty("ShareTool").GetBoolean() ? "" : " (tool name withheld)")}"
                        : $"  {s.GetProperty("Topic").GetString()} shared by {s.GetProperty("PeerName").GetString()}");
                return 0;
            }
        }
    }
}
