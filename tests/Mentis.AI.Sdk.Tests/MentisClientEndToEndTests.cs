using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;

using Google.Protobuf;

using Grpc.Core;
using Grpc.Net.Client;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk.Tests;

/// <summary>
/// Runs <see cref="MentisClient"/> over a real <see cref="GrpcChannel"/> whose HTTP transport is a fake,
/// so the interceptor, serialization, status handling and options are exercised together - no Manager needed.
/// </summary>
public class MentisClientEndToEndTests
{
    private static readonly string ApiKey = $"{TestIds.Tenant1}.the-secret";

    [Test]
    public async Task Call_SendsBearerTokenToTheRightMethodAndMapsTheResponse()
    {
        using var handler = new FakeGrpcHandler((_, _) => Task.FromResult(GrpcResponse(new Proto.GetTenantUsageResponse
        {
            Usage = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString(), Year = 2026, Month = 10, TotalTokens = 42, MonthlyTokenLimit = 100 },
        })));
        using GrpcChannel channel = CreateChannel(handler);
        using var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey });

        TenantUsage usage = await client.Billing.GetUsageAsync();

        usage.TotalTokens.ShouldBe(42);
        usage.MonthlyTokenLimit.ShouldBe(100);
        HttpRequestMessage request = handler.Requests.Single();
        request.RequestUri!.AbsolutePath.ShouldBe("/mentis.ai.v1.BillingService/GetTenantUsage");
        request.Headers.GetValues("authorization").Single().ShouldBe($"Bearer {ApiKey}");
    }

    [Test]
    public async Task Options_TenantIdAndSecret_AreSentAsTheSameBearerToken()
    {
        using var handler = new FakeGrpcHandler((_, _) => Task.FromResult(GrpcResponse(new Proto.GetTenantUsageResponse
        {
            Usage = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString() },
        })));
        using GrpcChannel channel = CreateChannel(handler);
        using var client = new MentisClient(channel, new MentisClientOptions { TenantId = TestIds.Tenant1, Secret = "the-secret" });

        await client.Billing.GetUsageAsync();

        handler.Requests.Single().Headers.GetValues("authorization").Single().ShouldBe($"Bearer {ApiKey}");
    }

    [Test]
    public async Task Timeout_IsSentAsDeadlineOnEveryCall()
    {
        using var handler = new FakeGrpcHandler((_, _) => Task.FromResult(GrpcResponse(new Proto.GetTenantUsageResponse
        {
            Usage = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString() },
        })));
        using GrpcChannel channel = CreateChannel(handler);
        using var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey, Timeout = TimeSpan.FromSeconds(30) });

        await client.Billing.GetUsageAsync();
        await client.Billing.GetUsageHistoryAsync();

        handler.Requests.Count.ShouldBe(2);
        handler.Requests.ShouldAllBe(r => r.Headers.Contains("grpc-timeout"));
    }

    [Test]
    public async Task WithoutTimeout_NoDeadlineIsSent()
    {
        using var handler = new FakeGrpcHandler((_, _) => Task.FromResult(GrpcResponse(new Proto.GetTenantUsageResponse
        {
            Usage = new Proto.TenantUsage { TenantId = TestIds.Tenant1.ToString() },
        })));
        using GrpcChannel channel = CreateChannel(handler);
        using var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey });

        await client.Billing.GetUsageAsync();

        handler.Requests.Single().Headers.Contains("grpc-timeout").ShouldBeFalse();
    }

    // false: how MentisClient creates its own channel; true: an external channel that opts into OperationCanceledException.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Timeout_WhenTheServerIsTooSlow_ThrowsDeadlineExceeded(bool throwOperationCanceled)
    {
        using var handler = new FakeGrpcHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return GrpcResponse(new Proto.GetTenantUsageResponse());
        });
        using GrpcChannel channel = CreateChannel(handler, throwOperationCanceled);
        using var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey, Timeout = TimeSpan.FromMilliseconds(300) });

        MentisException ex = await Should.ThrowAsync<MentisException>(() => client.Billing.GetUsageAsync());

        ex.StatusCode.ShouldBe(MentisStatusCode.DeadlineExceeded);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CallerCancellation_ThrowsOperationCanceledNotMentisException(bool throwOperationCanceled)
    {
        using var handler = new FakeGrpcHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return GrpcResponse(new Proto.GetTenantUsageResponse());
        });
        using GrpcChannel channel = CreateChannel(handler, throwOperationCanceled);
        using var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Exception? thrown = null;
        try
        {
            await client.Billing.GetUsageAsync(cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        thrown.ShouldBeAssignableTo<OperationCanceledException>();
        thrown.ShouldNotBeOfType<MentisException>();
    }

    [Test]
    public async Task RateLimitResponse_BecomesMentisExceptionWithErrorCode()
    {
        // The Manager answers a rate-limited call with a Trailers-Only response: HTTP 200, status in the headers.
        using var handler = new FakeGrpcHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.Headers.Add("grpc-status", Code(StatusCode.ResourceExhausted));
            response.Headers.Add("grpc-message", "RateLimit.Exceeded: Too many requests, retry in 60 s.");
            response.Headers.Add("grpc-retry-pushback-ms", "60000");
            return Task.FromResult(response);
        });
        using GrpcChannel channel = CreateChannel(handler);
        using var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey });

        MentisException ex = await Should.ThrowAsync<MentisException>(() => client.Billing.GetUsageAsync());

        ex.StatusCode.ShouldBe(MentisStatusCode.ResourceExhausted);
        ex.ErrorCode.ShouldBe("RateLimit.Exceeded");
        ex.Message.ShouldBe("Too many requests, retry in 60 s.");
    }

    [Test]
    public async Task ValidationFailure_ExposesFieldErrorsFromTrailers()
    {
        using var handler = new FakeGrpcHandler((_, _) =>
        {
            var response = GrpcResponse(message: null);
            response.TrailingHeaders.Add("grpc-status", Code(StatusCode.InvalidArgument));
            response.TrailingHeaders.Add("grpc-message", "Validation.Failed: Invalid request.");
            response.TrailingHeaders.Add("validation-error-newtitle", "The length of 'New Title' must be 200 characters or fewer.");
            return Task.FromResult(response);
        });
        using GrpcChannel channel = CreateChannel(handler);
        using var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey });

        MentisException ex = await Should.ThrowAsync<MentisException>(() => client.Documents.RenameAsync(TestIds.Document1, "x"));

        ex.StatusCode.ShouldBe(MentisStatusCode.InvalidArgument);
        ex.ErrorCode.ShouldBe("Validation.Failed");
        ex.ValidationErrors["newtitle"].ShouldBe(["The length of 'New Title' must be 200 characters or fewer."]);
    }

    [Test]
    public async Task ServerUnreachable_ThrowsMentisExceptionUnavailable()
    {
        // A real client on a port nobody listens on: the connection is refused immediately.
        using var client = new MentisClient(new MentisClientOptions { Endpoint = new Uri("http://127.0.0.1:1"), ApiKey = ApiKey });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        MentisException ex = await Should.ThrowAsync<MentisException>(() => client.Billing.GetUsageAsync(cancellationToken: cts.Token));

        ex.StatusCode.ShouldBe(MentisStatusCode.Unavailable);
    }

    [Test]
    public async Task OwnedChannel_Timeout_ThrowsDeadlineExceeded()
    {
        // Same as above on the channel MentisClient creates itself: nothing answers within the Timeout.
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new MentisClient(new MentisClientOptions
        {
            Endpoint = new Uri($"http://127.0.0.1:{port}"),
            ApiKey = ApiKey,
            Timeout = TimeSpan.FromMilliseconds(500),
        });

        MentisException ex = await Should.ThrowAsync<MentisException>(() => client.Billing.GetUsageAsync());

        ex.StatusCode.ShouldBe(MentisStatusCode.DeadlineExceeded);
    }

    [Test]
    public void ExternalChannel_IsNotDisposedByTheClient()
    {
        using var handler = new FakeGrpcHandler((_, _) => Task.FromResult(GrpcResponse(new Proto.GetTenantUsageResponse())));
        using GrpcChannel channel = CreateChannel(handler);
        var client = new MentisClient(channel, new MentisClientOptions { ApiKey = ApiKey });

        client.Dispose();

        // A disposed channel throws ObjectDisposedException when a call is created on it.
        Should.NotThrow(() => channel.CreateCallInvoker().AsyncUnaryCall(
            new Method<Proto.GetTenantUsageRequest, Proto.GetTenantUsageResponse>(
                MethodType.Unary,
                "mentis.ai.v1.BillingService",
                "GetTenantUsage",
                Marshallers.Create<Proto.GetTenantUsageRequest>(r => r.ToByteArray(), Proto.GetTenantUsageRequest.Parser.ParseFrom),
                Marshallers.Create<Proto.GetTenantUsageResponse>(r => r.ToByteArray(), Proto.GetTenantUsageResponse.Parser.ParseFrom)),
            host: null,
            new CallOptions(),
            new Proto.GetTenantUsageRequest()).Dispose());
    }

    private static string Code(StatusCode statusCode) => ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static GrpcChannel CreateChannel(HttpMessageHandler handler, bool throwOperationCanceled = false) =>
        GrpcChannel.ForAddress("http://mentis.test", new GrpcChannelOptions
        {
            HttpHandler = handler,
            ThrowOperationCanceledOnCancellation = throwOperationCanceled,
        });

    /// <summary>A successful gRPC response: length-prefixed message plus <c>grpc-status: 0</c> trailer.</summary>
    private static HttpResponseMessage GrpcResponse(IMessage? message)
    {
        byte[] payload = message?.ToByteArray() ?? [];
        byte[] framed = new byte[5 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1, 4), (uint)payload.Length);
        payload.CopyTo(framed, 5);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(message is null ? [] : framed),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        if (message is not null)
        {
            response.TrailingHeaders.Add("grpc-status", "0");
        }

        return response;
    }

    private sealed class FakeGrpcHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request, cancellationToken);
        }
    }
}
