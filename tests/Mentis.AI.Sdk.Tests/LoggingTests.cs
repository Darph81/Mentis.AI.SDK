using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;

using Google.Protobuf;

using Grpc.Net.Client;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk.Tests;

public class LoggingTests
{
    private const string Secret = "the-very-secret-value";
    private static readonly string ApiKey = $"{TestIds.Tenant1}.{Secret}";

    [Test]
    public void CreateChannelOptions_PassesTheLoggerFactoryOn()
    {
        using var factory = new CapturingLoggerFactory();

        GrpcChannelOptions options = MentisClient.CreateChannelOptions(new MentisClientOptions { LoggerFactory = factory });

        options.LoggerFactory.ShouldBeSameAs(factory);
    }

    [Test]
    public void CreateChannelOptions_WithoutLoggerFactory_LogsNothing()
    {
        MentisClient.CreateChannelOptions(new MentisClientOptions()).LoggerFactory.ShouldBeNull();
    }

    [Test]
    public async Task Calls_AreLogged_ButNeverWithTheCredential()
    {
        using var factory = new CapturingLoggerFactory();
        using var ok = new FakeHandler(_ => GrpcResponse(new Proto.GetTenantUsageResponse { Usage = new Proto.TenantUsage() }));
        using var failing = new FakeHandler(_ => Failure());

        GrpcChannelOptions channelOptions = MentisClient.CreateChannelOptions(new MentisClientOptions { LoggerFactory = factory });
        channelOptions.HttpHandler = ok;
        using var okChannel = GrpcChannel.ForAddress("http://mentis.test", channelOptions);
        using var okClient = new MentisClient(okChannel, new MentisClientOptions { ApiKey = ApiKey });
        await okClient.Billing.GetUsageAsync();

        channelOptions = MentisClient.CreateChannelOptions(new MentisClientOptions { LoggerFactory = factory });
        channelOptions.HttpHandler = failing;
        using var failingChannel = GrpcChannel.ForAddress("http://mentis.test", channelOptions);
        using var failingClient = new MentisClient(failingChannel, new MentisClientOptions { ApiKey = ApiKey });
        await Should.ThrowAsync<MentisException>(() => failingClient.Billing.GetUsageAsync());

        factory.Messages.ShouldNotBeEmpty();
        factory.Messages.ShouldAllBe(m => !m.Contains(Secret, StringComparison.Ordinal));
        factory.Messages.ShouldAllBe(m => !m.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task AddMentisClient_UsesTheContainersLoggerFactory()
    {
        using var factory = new CapturingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddMentisClient(o => o.ApiKey = ApiKey);

        await using ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<MentisClient>();

        provider.GetRequiredService<IOptions<MentisClientOptions>>().Value.LoggerFactory.ShouldBeSameAs(factory);
    }

    [Test]
    public async Task AddMentisClient_ExplicitLoggerFactory_WinsOverTheContainers()
    {
        using var fromContainer = new CapturingLoggerFactory();
        using var explicitFactory = new CapturingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(fromContainer);
        services.AddMentisClient(o =>
        {
            o.ApiKey = ApiKey;
            o.LoggerFactory = explicitFactory;
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<MentisClient>();

        provider.GetRequiredService<IOptions<MentisClientOptions>>().Value.LoggerFactory.ShouldBeSameAs(explicitFactory);
    }

    [Test]
    public async Task AddMentisClient_WithoutLogging_Works()
    {
        var services = new ServiceCollection();
        services.AddMentisClient(o => o.ApiKey = ApiKey);

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<MentisClient>().ShouldNotBeNull();
        provider.GetRequiredService<IOptions<MentisClientOptions>>().Value.LoggerFactory.ShouldBeNull();
    }

    private static HttpResponseMessage Failure()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        response.Headers.Add("grpc-status", "16");
        response.Headers.Add("grpc-message", "Auth.Invalid: Missing or invalid token.");
        return response;
    }

    private static HttpResponseMessage GrpcResponse(IMessage message)
    {
        byte[] payload = message.ToByteArray();
        byte[] framed = new byte[5 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1, 4), (uint)payload.Length);
        payload.CopyTo(framed, 5);

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent(framed) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        response.TrailingHeaders.Add("grpc-status", "0");
        return response;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    /// <summary>Records every message, including the exception text, at every level.</summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private void Add(string message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerFactory owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Add($"{category} [{logLevel}] {formatter(state, exception)} {exception}");
            }
        }
    }
}
