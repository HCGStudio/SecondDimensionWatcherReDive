using System.Net;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class QBittorrentRuntimeConfigurationTests
{
    private static readonly Uri OldEndpoint = new("https://old-qbit.example/");
    private static readonly Uri NewEndpoint = new("https://new-qbit.example/");

    [TestMethod]
    public async Task ForbiddenResponse_ReloginRetriesBufferedRequestBodyExactlyOnce()
    {
        var options = CreateOptions(OldEndpoint, "alice", "secret");
        var server = new FakeQBittorrentServer();
        server.SetAcceptedPassword(OldEndpoint, "secret");
        using var client = CreateClient(options, server, OldEndpoint);

        using (var primeResponse = await client.GetAsync("api/v2/torrents/info"))
            Assert.IsTrue(primeResponse.IsSuccessStatusCode);

        server.ExpireSession(OldEndpoint);
        server.ClearRequests();
        var payload = Encoding.UTF8.GetBytes("hashes=abc123&deleteFiles=true");
        using var content = new StreamContent(new NonSeekableReadStream(payload));
        content.Headers.ContentType = new("application/x-www-form-urlencoded");

        using var response = await client.PostAsync("api/v2/torrents/delete", content);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var actionRequests = server.Requests
            .Where(request => request.Path == "/api/v2/torrents/delete")
            .ToArray();
        Assert.AreEqual(2, actionRequests.Length, "The action should be sent once before and once after login.");
        Assert.AreEqual(Encoding.UTF8.GetString(payload), actionRequests[0].Body);
        Assert.AreEqual(actionRequests[0].Body, actionRequests[1].Body,
            "A non-seekable body must survive the authenticated retry.");
        Assert.IsFalse(actionRequests[0].WasAuthorized);
        Assert.IsTrue(actionRequests[1].WasAuthorized);

        var login = server.Requests.Single(request => request.Path == "/api/v2/auth/login");
        Assert.IsTrue(login.Uri.IsAbsoluteUri);
        Assert.AreEqual(OldEndpoint.Host, login.Uri.Host);
        Assert.IsTrue(server.Requests.IndexOf(login) > server.Requests.IndexOf(actionRequests[0]));
        Assert.IsTrue(server.Requests.IndexOf(login) < server.Requests.IndexOf(actionRequests[1]));
    }

    [TestMethod]
    public async Task PasswordChange_LogsOutAndLogsInWithNewCredentialsBeforeAction()
    {
        var options = CreateOptions(OldEndpoint, "alice", "old-secret");
        var server = new FakeQBittorrentServer();
        server.SetAcceptedPassword(OldEndpoint, "old-secret");
        using var client = CreateClient(options, server, OldEndpoint);

        using (var primeResponse = await client.GetAsync("api/v2/torrents/info"))
            Assert.IsTrue(primeResponse.IsSuccessStatusCode);

        server.ClearRequests();
        server.SetAcceptedPassword(OldEndpoint, "new-secret");
        options.Set(new()
        {
            Url = OldEndpoint.AbsoluteUri,
            UserName = "alice",
            Password = "new-secret"
        });

        using var response = await client.GetAsync("api/v2/torrents/info");

        Assert.IsTrue(response.IsSuccessStatusCode);
        CollectionAssert.AreEqual(
            new[] { "/api/v2/auth/logout", "/api/v2/auth/login", "/api/v2/torrents/info" },
            server.Requests.Select(request => request.Path).ToArray());
        StringAssert.Contains(server.Requests[1].Body, "password=new-secret");
        Assert.IsTrue(server.Requests[2].WasAuthorized);
    }

    [TestMethod]
    public async Task PasswordClear_LogsOutOldCookieAndDoesNotReuseOldSession()
    {
        var options = CreateOptions(OldEndpoint, "alice", "secret");
        var server = new FakeQBittorrentServer();
        server.SetAcceptedPassword(OldEndpoint, "secret");
        using var client = CreateClient(options, server, OldEndpoint);

        using (var primeResponse = await client.GetAsync("api/v2/torrents/info"))
            Assert.IsTrue(primeResponse.IsSuccessStatusCode);

        server.ClearRequests();
        options.Set(new()
        {
            Url = OldEndpoint.AbsoluteUri,
            UserName = "alice",
            Password = null
        });

        using var response = await client.GetAsync("api/v2/torrents/info");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual("/api/v2/auth/logout", server.Requests[0].Path,
            "The old session must be invalidated before an empty password is used.");
        var action = server.Requests.Single(request => request.Path == "/api/v2/torrents/info");
        Assert.IsFalse(action.WasAuthorized, "The request must not retain authorization from the old cookie.");
        Assert.IsTrue(server.Requests
            .Where(request => request.Path == "/api/v2/auth/login")
            .All(request => request.Body.Contains("password=", StringComparison.Ordinal)
                            && !request.Body.Contains("secret", StringComparison.Ordinal)));
        Assert.IsFalse(server.HasActiveSession(OldEndpoint));
    }

    [TestMethod]
    public async Task LogoutFailure_BlocksRequestInsteadOfReusingOldSession()
    {
        var options = CreateOptions(OldEndpoint, "alice", "secret");
        var server = new FakeQBittorrentServer();
        server.SetAcceptedPassword(OldEndpoint, "secret");
        using var client = CreateClient(options, server, OldEndpoint);

        using (var primeResponse = await client.GetAsync("api/v2/torrents/info"))
            Assert.IsTrue(primeResponse.IsSuccessStatusCode);

        server.ClearRequests();
        server.FailLogout = true;
        options.Set(new()
        {
            Url = OldEndpoint.AbsoluteUri,
            UserName = null,
            Password = null
        });

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            client.GetAsync("api/v2/torrents/info"));

        StringAssert.Contains(exception.Message, "previous qBittorrent session");
        CollectionAssert.AreEqual(
            new[] { "/api/v2/auth/logout" },
            server.Requests.Select(request => request.Path).ToArray());
        Assert.IsTrue(server.HasActiveSession(OldEndpoint),
            "The fake server deliberately keeps the old cookie active to exercise fail-closed behavior.");
    }

    [TestMethod]
    public async Task EndpointChange_LogsOutOldOriginAndAuthenticatesNewOrigin()
    {
        var options = CreateOptions(OldEndpoint, "alice", "secret");
        var server = new FakeQBittorrentServer();
        server.SetAcceptedPassword(OldEndpoint, "secret");
        server.SetAcceptedPassword(NewEndpoint, "secret");
        using var client = CreateClient(options, server, OldEndpoint);

        using (var primeResponse = await client.GetAsync("api/v2/torrents/info"))
            Assert.IsTrue(primeResponse.IsSuccessStatusCode);

        server.ClearRequests();
        options.Set(new()
        {
            Url = NewEndpoint.AbsoluteUri,
            UserName = "alice",
            Password = "secret"
        });
        using var response = await client.GetAsync(new Uri(NewEndpoint, "api/v2/torrents/info"));

        Assert.IsTrue(response.IsSuccessStatusCode);
        Assert.AreEqual("/api/v2/auth/logout", server.Requests[0].Path);
        Assert.AreEqual(OldEndpoint.Host, server.Requests[0].Uri.Host);
        Assert.AreEqual("/api/v2/auth/login", server.Requests[1].Path);
        Assert.AreEqual(NewEndpoint.Host, server.Requests[1].Uri.Host);
        Assert.AreEqual(NewEndpoint.Host, server.Requests[2].Uri.Host);
        Assert.IsFalse(server.HasActiveSession(OldEndpoint));
        Assert.IsTrue(server.HasActiveSession(NewEndpoint));
    }

    [TestMethod]
    public async Task StaleClient_DoesNotSendNewCredentialsToPreviousOrigin()
    {
        var options = CreateOptions(OldEndpoint, "alice", "old-secret");
        var server = new FakeQBittorrentServer();
        server.SetAcceptedPassword(OldEndpoint, "old-secret");
        using var staleClient = CreateClient(options, server, OldEndpoint);

        options.Set(new()
        {
            Url = NewEndpoint.AbsoluteUri,
            UserName = "alice",
            Password = "new-secret"
        });

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            staleClient.GetAsync("api/v2/torrents/info"));
        Assert.AreEqual(0, server.Requests.Count,
            "A stale client must fail before sending the new password or action to the old origin.");
    }

    [TestMethod]
    public async Task WaitingOldRequest_CannotRestoreCredentialsAfterNewSessionRotation()
    {
        var options = CreateOptions(OldEndpoint, "alice", "old-secret");
        var server = new FakeQBittorrentServer();
        server.SetAcceptedPassword(OldEndpoint, "old-secret");
        var gate = new GatedPathHandler(server, "/api/v2/auth/logout");
        using var client = CreateClient(options, gate, OldEndpoint);

        using (var primeResponse = await client.GetAsync("api/v2/torrents/info"))
            Assert.IsTrue(primeResponse.IsSuccessStatusCode);

        server.ClearRequests();
        server.SetAcceptedPassword(OldEndpoint, "new-secret");
        options.Set(new()
        {
            Url = OldEndpoint.AbsoluteUri,
            UserName = "alice",
            Password = "new-secret"
        });

        var newRequest = client.GetAsync("api/v2/torrents/info");
        await gate.WaitUntilEnteredAsync();

        // Capture the old snapshot while the new request owns the session lock, then restore the
        // current options before releasing that lock. The old waiter must fail instead of rolling
        // the shared cookie session back.
        options.Set(new()
        {
            Url = OldEndpoint.AbsoluteUri,
            UserName = "alice",
            Password = "old-secret"
        });
        var staleRequest = client.GetAsync("api/v2/torrents/info");
        options.Set(new()
        {
            Url = OldEndpoint.AbsoluteUri,
            UserName = "alice",
            Password = "new-secret"
        });
        gate.Release();

        using (var response = await newRequest)
            Assert.IsTrue(response.IsSuccessStatusCode);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
        {
            using var response = await staleRequest;
        });

        var logins = server.Requests
            .Where(request => request.Path == "/api/v2/auth/login")
            .ToArray();
        Assert.AreEqual(1, logins.Length);
        StringAssert.Contains(logins[0].Body, "password=new-secret");
        Assert.IsTrue(server.HasActiveSession(OldEndpoint));
    }

    [TestMethod]
    public async Task DownloadClient_CreatesNewNamedClientForEachOperation()
    {
        var handler = new RecordingSuccessHandler();
        var factory = new DynamicHttpClientFactory(handler, OldEndpoint);
        var configuration = new ConfigurationManager
        {
            ["FileStore:Local"] = "/tmp/sdw-test-downloads"
        };
        var trackRequests = Channel.CreateUnbounded<RemoteTorrentTrackRequest>();
        var downloadClient = new RemoteTorrentDownloadClient(factory, configuration, trackRequests);

        var pauseSucceeded = await downloadClient.PauseDownloadTaskAsync(
            Guid.NewGuid(), string.Empty, [], "first-hash", CancellationToken.None);
        factory.Endpoint = NewEndpoint;
        var resumeSucceeded = await downloadClient.ResumeDownloadTaskAsync(
            Guid.NewGuid(), string.Empty, [], "second-hash", CancellationToken.None);

        Assert.IsTrue(pauseSucceeded);
        Assert.IsTrue(resumeSucceeded);
        Assert.AreEqual(2, factory.CreateCount);
        CollectionAssert.AreEqual(
            new[] { nameof(RemoteTorrentDownloadClient), nameof(RemoteTorrentDownloadClient) },
            factory.ClientNames.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "https://old-qbit.example/api/v2/torrents/stop",
                "https://new-qbit.example/api/v2/torrents/start"
            },
            handler.RequestUris.Select(uri => uri.AbsoluteUri).ToArray());
    }

    private static TestOptionsMonitor<QBittorrentRemoteOptions> CreateOptions(
        Uri endpoint,
        string userName,
        string password) =>
        new(new()
        {
            Url = endpoint.AbsoluteUri,
            UserName = userName,
            Password = password
        });

    private static HttpClient CreateClient(
        TestOptionsMonitor<QBittorrentRemoteOptions> options,
        HttpMessageHandler innerHandler,
        Uri endpoint)
    {
        var authHandler = new QBittorrentAuthHandler(
            options,
            NullLogger<QBittorrentAuthHandler>.Instance)
        {
            InnerHandler = innerHandler
        };
        return new(authHandler) { BaseAddress = endpoint };
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string Path,
        string Body,
        bool WasAuthorized);

    private sealed class FakeQBittorrentServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _acceptedPasswords = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeSessions = new(StringComparer.OrdinalIgnoreCase);

        public List<CapturedRequest> Requests { get; } = [];

        public bool FailLogout { get; set; }

        public void SetAcceptedPassword(Uri endpoint, string password) =>
            _acceptedPasswords[Origin(endpoint)] = password;

        public void ExpireSession(Uri endpoint) => _activeSessions.Remove(Origin(endpoint));

        public bool HasActiveSession(Uri endpoint) => _activeSessions.Contains(Origin(endpoint));

        public void ClearRequests() => Requests.Clear();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.IsNotNull(request.RequestUri);
            Assert.IsTrue(request.RequestUri.IsAbsoluteUri, "Inner requests must use absolute URIs.");
            var uri = request.RequestUri;
            var origin = Origin(uri);
            var body = request.Content is null
                ? string.Empty
                : await ReadWithoutSeekingAsync(request.Content, cancellationToken);

            if (uri.AbsolutePath == "/api/v2/auth/login")
            {
                var password = ReadFormValue(body, "password");
                var accepted = _acceptedPasswords.TryGetValue(origin, out var expected)
                               && string.Equals(password, expected, StringComparison.Ordinal);
                if (accepted) _activeSessions.Add(origin);
                Requests.Add(new(request.Method, uri, uri.AbsolutePath, body, accepted));
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(accepted ? "Ok." : "Fails.")
                };
            }

            if (uri.AbsolutePath == "/api/v2/auth/logout")
            {
                if (FailLogout)
                {
                    Requests.Add(new(request.Method, uri, uri.AbsolutePath, body, false));
                    return new(HttpStatusCode.InternalServerError);
                }

                var wasAuthorized = _activeSessions.Remove(origin);
                Requests.Add(new(request.Method, uri, uri.AbsolutePath, body, wasAuthorized));
                return new(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            }

            var authorized = _activeSessions.Contains(origin);
            Requests.Add(new(request.Method, uri, uri.AbsolutePath, body, authorized));
            return new(authorized ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        }

        private static async Task<string> ReadWithoutSeekingAsync(
            HttpContent content,
            CancellationToken cancellationToken)
        {
            await using var destination = new MemoryStream();
            await content.CopyToAsync(destination, cancellationToken);
            return Encoding.UTF8.GetString(destination.ToArray());
        }

        private static string ReadFormValue(string body, string key)
        {
            foreach (var field in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = field.Split('=', 2);
                if (!string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.Ordinal)) continue;
                return pair.Length == 1
                    ? string.Empty
                    : Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            }

            return string.Empty;
        }

        private static string Origin(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);
    }

    private sealed class GatedPathHandler(
        HttpMessageHandler innerHandler,
        string gatedPath) : DelegatingHandler(innerHandler)
    {
        private readonly TaskCompletionSource _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _gateNextRequest = 1;

        public Task WaitUntilEnteredAsync() =>
            _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == gatedPath &&
                Interlocked.Exchange(ref _gateNextRequest, 0) == 1)
            {
                _entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class DynamicHttpClientFactory(
        HttpMessageHandler handler,
        Uri endpoint) : IHttpClientFactory
    {
        public Uri Endpoint { get; set; } = endpoint;
        public int CreateCount { get; private set; }
        public List<string> ClientNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            CreateCount++;
            ClientNames.Add(name);
            return new(handler, disposeHandler: false) { BaseAddress = Endpoint };
        }
    }

    private sealed class RecordingSuccessHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.IsNotNull(request.RequestUri);
            RequestUris.Add(request.RequestUri);
            if (request.Content is not null)
                await request.Content.CopyToAsync(Stream.Null, cancellationToken);
            return new(HttpStatusCode.OK);
        }
    }

    private sealed class NonSeekableReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
