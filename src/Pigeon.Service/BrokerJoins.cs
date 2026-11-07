using System.Text.Json;

using Pigeon.Core;
using Pigeon.Service.Peers;

namespace Pigeon.Service;

// Requests to join (see docs/remote-design.md, "Joining"): from another machine into a listed topic
// here (incoming), from a session here into a topic on another machine (outgoing), and into a local
// topic that requires approval. They live only in memory, so a restart cancels them, the safe
// direction. Each one turns into prompts that Pigeon's MCP servers show their person by elicitation,
// outside any tool call; the same answers can be given from a terminal.
public sealed partial class Broker {
    public const long RequestMilliseconds = 600000;
    private const int MaxIncoming = 32, MaxWrongCodes = 3;
    public const string AntigravityLocalOnly = "Remote topics and approval codes are not available in Antigravity, which cannot ask you for approval. Use it for local topics.";

    internal sealed class Incoming {
        public required string Id, Topic, Fingerprint, Name, Tool, Address, Commitment;
        public required bool Known, Listed;
        public required byte[] Nonce;
        public string? Code, ChangedName;
        public int WrongCodes;
        public long Expires;
        public string State = "committed"; // committed, open, allowed, denied
    }

    internal sealed class Outgoing {
        public required string Id, Session, Tool, Offered, Target;
        public required bool Known;
        public required byte[] Nonce;
        public string? Fingerprint, MachineName, RemoteId, Code, ListingTool;
        public bool CodeShown, NeedsCode;
        public long Expires;
        public string State = "confirm"; // confirm, contact, waiting, allowed, denied, failed
    }

    internal sealed class LocalJoin {
        public required string Id, Topic, Session, Tool, Opener, Code;
        public bool CodeShown;
        public int WrongCodes;
        public long Expires;
        public string State = "open";
    }

    private readonly Dictionary<string, Incoming> incoming = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Outgoing> outgoing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LocalJoin> localJoins = new(StringComparer.Ordinal);
    // A prompt is shown by one live MCP server at a time; another may show it if that one goes away.
    private readonly Dictionary<string, (string Bridge, long At)> promptHolders = new(StringComparer.Ordinal);

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];
    private static bool Bool(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private void ExpireRequests() {
        var now = clock();
        foreach (var r in incoming.Values.Where(r => r.State is "committed" or "open" && r.Expires <= now))
            r.State = "denied";
        foreach (var r in outgoing.Values.Where(r => r.State is "confirm" or "contact" or "waiting" && r.Expires <= now)) {
            r.State = "denied";
            mailbox.Notice(r.Session, r.Offered + "@" + (r.MachineName ?? r.Target), $"Nobody allowed joining {r.Offered} on {r.MachineName ?? r.Target} within 10 minutes. If nobody answered, check the topic name and address, and ask the person there.");
        }

        foreach (var r in localJoins.Values.Where(r => r.State == "open" && r.Expires <= now)) {
            r.State = "denied";
            mailbox.Notice(r.Session, r.Topic, $"Nobody allowed joining {r.Topic} within 10 minutes.");
        }

        foreach (var id in incoming.Where(p => p.Value.State is "allowed" or "denied" && p.Value.Expires < now - RequestMilliseconds).Select(p => p.Key).ToArray())
            incoming.Remove(id);
        foreach (var id in outgoing.Where(p => p.Value.State is "allowed" or "denied" or "failed" && p.Value.Expires < now - RequestMilliseconds).Select(p => p.Key).ToArray())
            outgoing.Remove(id);
        foreach (var id in localJoins.Where(p => p.Value.State != "open" && p.Value.Expires < now - RequestMilliseconds).Select(p => p.Key).ToArray())
            localJoins.Remove(id);
    }

    // "join" from the CLI or an agent: a local topic, a topic on another machine ("<topic>@<machine>"),
    // or a local topic that requires approval, whose joiner waits for the opener's person.
    private object Join(JsonElement args) {
        var tool = Str(args, "tool");
        var session = Str(args, "sessionId");
        var topic = Str(args, "topic");
        var remote = Bool(args, "remote");
        var approval = Bool(args, "approval");
        // False when the host cannot show Pigeon's prompts: the person answers in a terminal instead.
        // The command line sends no flag; its person is at a terminal already.
        var prompts = !args.TryGetProperty("prompts", out var p) || p.ValueKind != JsonValueKind.False;
        var antigravity = Mailbox.ToolName(tool) == "antigravity";
        if ((remote || approval) && antigravity)
            throw new InvalidOperationException(AntigravityLocalOnly);
        if (remote && mailbox.Setting("listen") is null)
            throw new InvalidOperationException("Other machines cannot reach this one yet. A person turns that on once, in a terminal: pigeon peer listen");
        if (topic.Contains('@') && !mailbox.Topics().Any(t => t.Topic == topic))
            return antigravity ? throw new InvalidOperationException(AntigravityLocalOnly) : WithTerminal(RemoteJoin(tool, session, topic), prompts);
        var (_, needsApproval) = mailbox.TopicOptions(topic);
        if (needsApproval && !mailbox.IsMember(session, topic) && mailbox.MemberCount(topic) == 1 && mailbox.LocalMemberOf(topic) is { } opener) {
            if (antigravity)
                throw new InvalidOperationException($"Topic \"{topic}\" requires approval, which Antigravity cannot ask for. {AntigravityLocalOnly}");
            mailbox.EnsureSession(tool, session);
            var pending = localJoins.Values.FirstOrDefault(r => r.State == "open" && r.Topic == topic);
            if (pending is not null && pending.Session != session)
                throw new InvalidOperationException($"Someone else is asking to join {topic}; try again in a few minutes.");
            if (pending is null) {
                pending = new LocalJoin { Id = NewId(), Topic = topic, Session = session, Tool = Mailbox.ToolName(tool)!, Opener = opener.SessionId, Code = JoinCode.Local(), Expires = checked(clock() + RequestMilliseconds) };
                localJoins[pending.Id] = pending;
            }
            return new { pending = true, request = pending.Id, text = $"Request submitted. {topic} requires approval: your person is shown a join code to give to the person at the session that opened it. The join completes when they enter it. Do not call pigeon_join again; you will be told the outcome." };
        }

        return mailbox.Join(tool, session, topic, remote, approval);
    }

    // A topic on another machine. Known machines are asked straight away; for a new machine the
    // requesting person first confirms the contact, and is then shown the join code.
    private object RemoteJoin(string tool, string session, string topic) {
        var at = topic.LastIndexOf('@');
        var offered = topic[..at];
        var target = topic[(at + 1)..];
        if (offered.Length == 0 || target.Length == 0 || offered.Contains('@'))
            throw new InvalidOperationException("Name a topic on another machine as <topic>@<machine>, for example project-1@dev-laptop, or <topic>@<address> the first time.");
        if (mailbox.Setting("listen") is null)
            throw new InvalidOperationException("This machine cannot receive replies yet. A person turns that on once, in a terminal: pigeon peer listen");
        mailbox.EnsureSession(tool, session);
        var known = mailbox.PeerByName(target);
        string address;
        if (known is not null)
            address = known.Address ?? throw new InvalidOperationException($"No address is known for {target}; set it with pigeon peer address {target} <address>.");
        else {
            address = PeerHost.ParseTarget(target);
            known = mailbox.PeerByAddress(address);
        }

        var existing = outgoing.Values.FirstOrDefault(r => r.Session == session && r.Offered == offered && r.State is "confirm" or "contact" or "waiting");
        if (existing is not null)
            return Pending(existing);
        var request = new Outgoing {
            Id = NewId(), Session = session, Tool = Mailbox.ToolName(tool)!, Offered = offered, Target = address, Known = known is not null,
            Nonce = JoinCode.NewNonce(), Fingerprint = known?.Fingerprint, MachineName = known?.Name, Expires = checked(clock() + RequestMilliseconds),
            State = known is null ? "confirm" : "contact"
        };
        outgoing[request.Id] = request;
        return Pending(request);
    }

    // A host that cannot show Pigeon's prompts: tell the agent where its person answers instead.
    private const string TerminalAnswer = " This host cannot show Pigeon's prompts, so tell your person to answer in a terminal: pigeon requests.";

    private static object WithTerminal(object result, bool prompts) {
        if (prompts || result is not { } value)
            return result;
        var json = JsonSerializer.SerializeToElement(value);
        if (!json.TryGetProperty("text", out var text) || !json.TryGetProperty("request", out var request))
            return result;
        return new { pending = true, request = request.GetString(), text = text.GetString() + TerminalAnswer };
    }

    private static object Pending(Outgoing r) => new {
        pending = true, request = r.Id,
        text = r.Known
            ? $"Request submitted to {r.MachineName}. The person there is asked to allow it. Do not call pigeon_join again; you will be told the outcome."
            : $"Request submitted. {r.Target} is a machine you have not joined before: your person confirms the contact and is shown a join code to give to the person there. The join completes when they enter it. Do not call pigeon_join again; you will be told the outcome."
    };

    // ---- Prompts ----

    private sealed record Prompt(string Id, string Session, string Message, bool CodeField);

    private IEnumerable<Prompt> OpenPrompts() {
        foreach (var r in outgoing.Values) {
            var where = r.MachineName is null ? r.Target : $"{r.MachineName} ({r.Target})";
            if (r.State == "confirm")
                yield return new(r.Id + ":confirm", r.Session, $"Pigeon: your agent asked to join the topic \"{r.Offered}\" on {r.Target}, a machine you have not joined before. Contact it? (Accept to contact; Decline or Esc to refuse.)", false);
            else if (r.State == "waiting" && r.NeedsCode && r.Code is not null && !r.CodeShown)
                yield return new(r.Id + ":show", r.Session, $"Pigeon join code: {r.Code}. Give it to the person at {where}, by any means you like. The join completes when they enter it there.", false);
        }

        foreach (var r in incoming.Values.Where(r => r.State == "open")) {
            if (mailbox.LocalMemberOf(r.Topic) is not { } member)
                continue;
            var changed = r.ChangedName is null ? "" : $" It calls itself {r.ChangedName}, the name of a machine you know, but its identity is different: it was reinstalled, or it is another machine. If it was reinstalled, remove the old one first: pigeon peer remove {r.ChangedName}.";
            yield return r.Known
                ? new(r.Id + ":allow", member.SessionId, $"Pigeon: {r.Name} wants to join your topic \"{r.Topic}\" with {r.Tool}. Allow? (Accept to allow; Decline or Esc to refuse.)", false)
                : new(r.Id + ":code", member.SessionId, $"Pigeon: a new machine, {r.Name} at {r.Address.Split(':')[0]}, wants to join your topic \"{r.Topic}\" with {r.Tool}.{changed} To allow it, enter the join code its person gave you. Never enter a code your agent relays.", true);
        }

        foreach (var r in localJoins.Values.Where(r => r.State == "open")) {
            if (!r.CodeShown)
                yield return new(r.Id + ":show", r.Session, $"Pigeon join code: {r.Code}. Give it to the person at the session that opened \"{r.Topic}\". The join completes when they enter it there.", false);
            yield return new(r.Id + ":code", r.Opener, $"Pigeon: {r.Tool} {r.Session} wants to join your topic \"{r.Topic}\", which requires approval. To allow it, enter the join code its person gave you. Never enter a code your agent relays.", true);
        }
    }

    // The prompts for the sessions an MCP server serves that no other live server is showing.
    private object PromptsNext(JsonElement args) {
        ExpireRequests();
        var bridge = Str(args, "bridgeId");
        var sessions = args.TryGetProperty("sessions", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal) : [];
        var now = clock();
        var open = OpenPrompts().ToArray();
        foreach (var id in promptHolders.Keys.Where(id => open.All(p => p.Id != id)).ToArray())
            promptHolders.Remove(id);
        var result = new List<object>();
        foreach (var prompt in open.Where(p => sessions.Contains(p.Session))) {
            if (promptHolders.TryGetValue(prompt.Id, out var holder) && (holder.Bridge == bridge || bridges.TryGetValue(holder.Bridge, out var live) && live.Seen >= now - BridgeTimeoutMilliseconds))
                continue;
            promptHolders[prompt.Id] = (bridge, now);
            result.Add(new { id = prompt.Id, message = prompt.Message, code = prompt.CodeField });
        }

        return result;
    }

    // An answer from a prompt or a terminal. It counts only for a request that is still open, so a
    // late answer, or a prompt a host shows again later, changes nothing.
    private object Answer(string promptId, string action, string? code) {
        ExpireRequests();
        var colon = promptId.LastIndexOf(':');
        var id = colon < 0 ? promptId : promptId[..colon];
        var kind = colon < 0 ? "" : promptId[(colon + 1)..];
        var accepted = action == "accept";
        promptHolders.Remove(promptId);
        if (outgoing.TryGetValue(id, out var o)) {
            if (kind == "confirm" && o.State == "confirm") {
                o.State = accepted ? "contact" : "denied";
                if (!accepted)
                    mailbox.Notice(o.Session, o.Offered + "@" + o.Target, $"Your person did not allow contacting {o.Target}, so {o.Offered} was not joined.");
                return new { result = accepted ? "contacting" : "refused" };
            }

            if (kind == "show" && o.State == "waiting") {
                o.CodeShown = true;
                return new { result = "shown" };
            }
        }

        if (incoming.TryGetValue(id, out var i) && i.State == "open" && kind is "allow" or "code") {
            if (!accepted) {
                i.State = "denied";
                return new { result = "denied" };
            }

            if (!i.Known && !JoinCode.Same(i.Code!, code)) {
                if (++i.WrongCodes >= MaxWrongCodes)
                    i.State = "denied";
                return new { result = i.State == "denied" ? "denied after 3 wrong codes" : "wrong code" };
            }

            try {
                if (!i.Known)
                    _ = mailbox.AddPeer(i.Fingerprint, i.Name, i.Address);
                _ = mailbox.LinkTopic(i.Fingerprint, i.Topic, i.Tool);
                i.State = "allowed";
            }
            catch (InvalidOperationException) {
                i.State = "denied";
            }

            return new { result = i.State };
        }

        if (localJoins.TryGetValue(id, out var l) && l.State == "open") {
            if (kind == "show") {
                l.CodeShown = true;
                return new { result = "shown" };
            }

            if (!accepted) {
                l.State = "denied";
                mailbox.Notice(l.Session, l.Topic, $"The person at the session that opened {l.Topic} did not allow you to join.");
                return new { result = "denied" };
            }

            if (!JoinCode.Same(l.Code, code)) {
                if (++l.WrongCodes >= MaxWrongCodes) {
                    l.State = "denied";
                    mailbox.Notice(l.Session, l.Topic, $"Joining {l.Topic} was refused after 3 wrong codes.");
                }

                return new { result = l.State == "denied" ? "denied after 3 wrong codes" : "wrong code" };
            }

            l.State = "allowed";
            _ = mailbox.Join(l.Tool, l.Session, l.Topic);
            mailbox.Notice(l.Session, l.Topic, $"You joined {l.Topic}.");
            return new { result = "allowed" };
        }

        return new { result = "ignored: the request is no longer open" };
    }

    // `pigeon requests`: what waits for an answer here, never the codes.
    private object Requests() {
        ExpireRequests();
        return new {
            incoming = incoming.Values.Where(r => r.State == "open").Select(r => new { id = r.Id, topic = r.Topic, from = r.Name, address = r.Address, tool = r.Tool, needsCode = !r.Known, expires = r.Expires }).ToArray(),
            outgoing = outgoing.Values.Where(r => r.State is "confirm" or "contact" or "waiting").Select(r => new { id = r.Id, topic = r.Offered + "@" + (r.MachineName ?? r.Target), state = r.State, hasCode = r.NeedsCode && r.Code is not null, expires = r.Expires }).ToArray(),
            local = localJoins.Values.Where(r => r.State == "open").Select(r => new { id = r.Id, topic = r.Topic, from = r.Tool + " " + r.Session, expires = r.Expires }).ToArray()
        };
    }

    // `pigeon requests --show <id>`: the requesting side's code, for a person at a terminal.
    private object RequestCode(string id) {
        ExpireRequests();
        if (outgoing.TryGetValue(id, out var o) && o.State == "waiting" && o.Code is not null && o.NeedsCode)
            return new { code = o.Code, to = o.MachineName ?? o.Target };
        if (localJoins.TryGetValue(id, out var l) && l.State == "open")
            return new { code = l.Code, to = "the session that opened " + l.Topic };
        throw new InvalidOperationException(outgoing.TryGetValue(id, out var waiting) && waiting.State is "confirm" or "contact"
            ? "There is no code yet: the contact is not confirmed or the other machine has not answered. Confirm it with pigeon allow " + id + "."
            : $"No request {id} here has a code to show.");
    }

    // `pigeon allow <id> [<code>]` and `pigeon deny <id>`.
    private object RequestAnswer(string id, bool allow, string? code) {
        ExpireRequests();
        var kind = outgoing.TryGetValue(id, out var o) ? o.State == "confirm" ? "confirm" : null
            : incoming.TryGetValue(id, out var i) ? i.Known ? "allow" : "code"
            : localJoins.ContainsKey(id) ? "code"
            : null;
        if (kind is null)
            throw new InvalidOperationException($"No open request {id} waits for an answer here; see pigeon requests.");
        if (allow && kind == "code" && code is null)
            throw new InvalidOperationException("This request needs the join code the other person gave you: pigeon allow " + id + " <code>");
        return Answer(id + ":" + kind, allow ? "accept" : "decline", code);
    }

    // ---- The listing side, called by the peer listener ----

    internal (string Id, byte[] Nonce, string Name, bool NeedsCode)? JoinStart(string fingerprint, string address, string topic, string name, string? tool, string commitment) {
        lock (gate) {
            ExpireRequests();
            if (incoming.Values.Count(r => r.State is "committed" or "open") >= MaxIncoming || topic.Length is 0 or > 256 || commitment.Length != 64)
                return null;
            var known = mailbox.PeerByFingerprint(fingerprint);
            var listed = mailbox.IsListed(topic) && !incoming.Values.Any(r => r.Topic == topic && r.State is "committed" or "open" && r.Fingerprint != fingerprint);
            var safeName = Mailbox.IsValidPeerName(name) ? name : Mailbox.TidyName(name);
            var request = new Incoming {
                Id = NewId(), Topic = topic, Fingerprint = fingerprint, Name = known?.Name ?? safeName, Tool = Mailbox.ToolName(tool) is { Length: > 0 and <= 64 } t && !t.Any(char.IsControl) ? t : "an agent",
                Address = address, Commitment = commitment, Known = known is not null, Listed = listed, Nonce = JoinCode.NewNonce(), Expires = checked(clock() + RequestMilliseconds),
                ChangedName = known is null && mailbox.PeerByName(safeName) is not null ? safeName : null
            };
            incoming[request.Id] = request;
            if (known is not null)
                mailbox.TouchPeer(fingerprint, address);
            return (request.Id, request.Nonce, mailbox.Setting("name") ?? PeerHost.DefaultName(), known is null);
        }
    }

    // The requester revealed its value: the code is fixed, and the request reaches the person here,
    // unless the topic was not listed, which looks the same from outside.
    internal bool JoinReveal(string listing, string fingerprint, string id, byte[] nonce) {
        lock (gate) {
            if (!incoming.TryGetValue(id, out var r) || r.Fingerprint != fingerprint || r.State != "committed" || !JoinCode.Matches(r.Commitment, nonce))
                return false;
            r.Code = JoinCode.Derive(nonce, r.Nonce, listing, fingerprint, r.Topic);
            r.State = r.Listed ? "open" : "denied";
            return true;
        }
    }

    internal (string State, string? Tool) JoinStatus(string fingerprint, string id) {
        lock (gate) {
            ExpireRequests();
            if (!incoming.TryGetValue(id, out var r) || r.Fingerprint != fingerprint)
                return ("denied", null);
            // An unlisted topic answers "pending" until it expires, like an unanswered request.
            return r.State switch {
                "allowed" => ("allowed", mailbox.LocalMemberOf(r.Topic)?.Tool),
                "denied" when r.Listed => ("denied", null),
                "denied" when r.Expires > clock() => ("pending", null),
                "denied" => ("denied", null),
                _ => ("pending", null)
            };
        }
    }

    // ---- The requesting side, driven by the peer host ----

    internal Outgoing[] OutgoingDue(params string[] states) {
        lock (gate) {
            ExpireRequests();
            return [.. outgoing.Values.Where(r => states.Contains(r.State))];
        }
    }

    internal void OutgoingFailed(Outgoing r, string reason) {
        lock (gate) {
            if (r.State is "allowed" or "denied" or "failed")
                return;
            r.State = "failed";
            mailbox.Notice(r.Session, r.Offered + "@" + (r.MachineName ?? r.Target), $"Could not join {r.Offered} on {r.MachineName ?? r.Target}: {reason}");
        }
    }

    // Contact made: the other machine answered, and the code (for a new machine) is known.
    internal void OutgoingContacted(Outgoing r, string fingerprint, string machineName, string remoteId, string code, bool needsCode) {
        lock (gate) {
            if (r.State != "contact")
                return;
            var known = mailbox.PeerByFingerprint(fingerprint);
            r.Fingerprint = fingerprint;
            r.MachineName = known?.Name ?? (Mailbox.IsValidPeerName(machineName) ? machineName : Mailbox.TidyName(machineName));
            r.Known = known is not null;
            r.RemoteId = remoteId;
            r.Code = code;
            r.NeedsCode = needsCode;
            r.State = "waiting";
        }
    }

    internal void OutgoingAnswered(Outgoing r, string state, string? listingTool) {
        lock (gate) {
            if (r.State != "waiting")
                return;
            if (state == "denied") {
                r.State = "denied";
                mailbox.Notice(r.Session, r.Offered + "@" + r.MachineName, $"The person at {r.MachineName} did not allow joining {r.Offered}.");
                return;
            }

            try {
                var peer = mailbox.AddPeer(r.Fingerprint!, r.MachineName!, r.Target);
                r.MachineName = peer.Name;
                _ = mailbox.LinkJoin(r.Fingerprint!, r.Offered, listingTool, r.Session, r.Tool);
                r.State = "allowed";
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException) {
                r.State = "failed";
                mailbox.Notice(r.Session, r.Offered + "@" + r.MachineName, $"Joining {r.Offered} on {r.MachineName} was allowed, but could not be completed here: {e.Message}");
            }
        }
    }

    internal PeerInfo[] Peers() {
        lock (gate)
            return mailbox.Peers();
    }

    internal string[] ListedTopics() {
        lock (gate)
            return mailbox.ListedTopics();
    }

    internal bool AnyListed() {
        lock (gate)
            return mailbox.ListedTopics().Length != 0 || incoming.Values.Any(r => r.State is "committed" or "open" or "allowed" && r.Expires > clock());
    }
}
