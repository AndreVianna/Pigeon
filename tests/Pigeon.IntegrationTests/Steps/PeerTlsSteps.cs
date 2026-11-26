using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Pigeon.Service;
using Pigeon.Service.Peers;

using Reqnroll;

using Xunit;

namespace Pigeon.IntegrationTests.Steps;

// Ana's peer listener hosted in this process over real mutual TLS, on a free loopback port, with
// both identities in a temporary folder (never the user's certificate store).
[Binding]
[Scope(Tag = "peer_tls")]
public sealed class PeerTlsSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-peer-tls-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource stop = new();
    private readonly List<HttpStatusCode> answers = [];
    private Task server = Task.CompletedTask;
    private Broker? ana;
    private ServiceIdentity? anaId, strangerId;
    private int anaPort;
    private Exception? failure;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private string Url(string path) => $"https://127.0.0.1:{anaPort}{path}";
    private ServiceIdentity Ana => anaId!;
    private ServiceIdentity Stranger => strangerId!;

    private async Task<HttpStatusCode> Post(HttpClient client, string path, object body) {
        using var response = await client.PostAsJsonAsync(Url(path), body, Cancellation);
        return response.StatusCode;
    }

    [Given("Ana's peer listener on loopback and a machine she does not know")]
    public async Task Listening() {
        Directory.CreateDirectory(directory);
        ana = new Broker(Path.Combine(directory, "ana.db"));
        anaId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "ana")));
        strangerId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "stranger")));
        anaPort = int.Parse(TestPorts.FreePort(), CultureInfo.InvariantCulture);
        ana.SetPeerSetting("listen", $"127.0.0.1:{anaPort}");
        var peer = new PeerServer(ana, Ana, [new IPEndPoint(IPAddress.Loopback, anaPort)]);
        server = peer.RunAsync(stop.Token);
        await peer.Ready.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
    }

    [Given("Ana has a topic open to other machines")]
    public void TopicOpen() {
        var opened = ana!.Execute("join", JsonSerializer.SerializeToElement(new { tool = "claude-code", sessionId = "ana-session", topic = "open", remote = true }));
        Assert.True(opened.Ok, opened.Error);
    }

    [When("the stranger asks to start a join")]
    public async Task StrangerStartsJoin() {
        using var stranger = HttpsPeerLink.ClientFor(Stranger, Ana.Fingerprint);
        failure = await Record.ExceptionAsync(() => Post(stranger, PeerServer.JoinStartPath, new { }));
    }

    [When("the stranger asks to start a join, expecting another identity than Ana's")]
    public async Task StrangerExpectsAnotherIdentity() {
        using var misdirected = HttpsPeerLink.ClientFor(Stranger, Stranger.Fingerprint);
        failure = await Record.ExceptionAsync(() => Post(misdirected, PeerServer.JoinStartPath, new { }));
    }

    [When("the stranger, limited to TLS 1.2, asks for Ana's topics")]
    public async Task MacListsTopics() {
        using var mac = new HttpClient(new SocketsHttpHandler {
            SslOptions = new SslClientAuthenticationOptions {
                EnabledSslProtocols = SslProtocols.Tls12,
                ClientCertificates = [Stranger.Certificate],
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is X509Certificate2 presented && ServiceIdentity.FingerprintOf(presented) == Ana.Fingerprint
            },
            UseProxy = false
        });
        answers.Add(await Post(mac, PeerServer.TopicsPath, new { }));
    }

    [When("the stranger delivers a message and asks for Ana's topics")]
    public async Task StrangerDeliversAndLists() {
        using var stranger = HttpsPeerLink.ClientFor(Stranger, Ana.Fingerprint, port: 49999);
        var item = new PeerItem("message", "open", Guid.NewGuid().ToString("N"), "codex", "let me in", 60000);
        answers.Add(await Post(stranger, PeerServer.DeliverPath, new { Items = new[] { item } }));
        answers.Add(await Post(stranger, PeerServer.TopicsPath, new { }));
    }

    [When("the stranger asks to join Ana's open topic")]
    public async Task StrangerJoins() {
        using var stranger = HttpsPeerLink.ClientFor(Stranger, Ana.Fingerprint, port: 49999);
        answers.Clear();
        answers.Add(await Post(stranger, PeerServer.JoinStartPath, new { Topic = "open", Name = "stranger", Tool = "codex", Port = 49999, Commitment = JoinCode.Commit(JoinCode.NewNonce()) }));
    }

    [Then("the handshake fails")]
    public void HandshakeFails() => Assert.IsAssignableFrom<HttpRequestException>(failure);

    [Then("the requests are refused after the handshake")]
    public void Refused() {
        Assert.NotEmpty(answers);
        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Forbidden, answer));
    }

    // Starting a join adds nobody to the topic.
    [Then("the request is accepted, and Ana's topic still has only her in it")]
    public void AcceptedAndAlone() {
        Assert.True((int)Assert.Single(answers) is >= 200 and < 300);
        var topics = ana!.Execute("topics", JsonSerializer.SerializeToElement(new { sessionId = "ana-session" }));
        Assert.True(topics.Ok, topics.Error);
        Assert.Equal(1, JsonSerializer.SerializeToElement(topics.Value)[0].GetProperty("Members").GetArrayLength());
    }

    [AfterScenario]
    public async Task Cleanup() {
        await stop.CancelAsync();
        await server;
        ana?.Dispose();
        anaId?.Dispose();
        strangerId?.Dispose();
        stop.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
