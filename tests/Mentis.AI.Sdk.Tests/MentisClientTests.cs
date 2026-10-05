using Grpc.Core;
using Grpc.Core.Interceptors;

using Mentis.AI.Sdk.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mentis.AI.Sdk.Tests;

public class MentisClientTests
{
    [Test]
    public void Options_WithTenantIdAndSecret_BuildsApiKey()
    {
        var options = new MentisClientOptions { TenantId = TestIds.Tenant1, Secret = "secret" };

        options.GetValidatedApiKey().ShouldBe($"{TestIds.Tenant1}.secret");
    }

    [Test]
    public void Options_ApiKey_TakesPrecedence()
    {
        string apiKey = $"{TestIds.Tenant1}.from-api-key";
        var options = new MentisClientOptions { ApiKey = apiKey, TenantId = Guid.NewGuid(), Secret = "secret" };

        options.GetValidatedApiKey().ShouldBe(apiKey);
    }

    [TestCase("not-a-guid.secret")]
    [TestCase("00000000-0000-0000-0000-000000000000.secret")]
    [TestCase("7e700000-0000-0000-0000-000000000001")]
    [TestCase("7e700000-0000-0000-0000-000000000001.")]
    public void Constructor_WithMalformedApiKey_Throws(string apiKey)
    {
        Should.Throw<ArgumentException>(() => new MentisClient(new MentisClientOptions { ApiKey = apiKey }));
    }

    [Test]
    public void TenantIdOf_ReadsThePrincipal()
    {
        MentisClientOptions.TenantIdOf($"{TestIds.Tenant1}.secret.with.dots").ShouldBe(TestIds.Tenant1);
    }

    [Test]
    public void Constructor_WithoutCredentials_Throws()
    {
        Should.Throw<ArgumentException>(() => new MentisClient(new MentisClientOptions { TenantId = TestIds.Tenant1 }));
    }

    [Test]
    public void Constructor_WithNonHttpEndpoint_Throws()
    {
        var options = new MentisClientOptions { Endpoint = new Uri("ftp://localhost"), ApiKey = $"{TestIds.Tenant1}.secret" };

        Should.Throw<ArgumentException>(() => new MentisClient(options));
    }

    [Test]
    public async Task Constructor_WithValidOptions_ExposesServiceClients()
    {
        await using var client = new MentisClient(new MentisClientOptions { ApiKey = $"{TestIds.Tenant1}.secret" });

        client.Documents.ShouldNotBeNull();
        client.Conversations.UserId.ShouldBeNull();
        client.Billing.ShouldNotBeNull();
    }

    [Test]
    public async Task AddMentisClient_RegistersClientAndServiceClients()
    {
        var services = new ServiceCollection();
        services.AddMentisClient(o => o.ApiKey = $"{TestIds.Tenant1}.secret");

        await using ServiceProvider provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<MentisClient>();
        provider.GetRequiredService<IMentisClient>().ShouldBeSameAs(client);
        provider.GetRequiredService<IDocumentsClient>().ShouldBeSameAs(client.Documents);
        provider.GetRequiredService<IConversationsClient>().ShouldBeSameAs(client.Conversations);
        provider.GetRequiredService<IBillingClient>().ShouldBeSameAs(client.Billing);
    }

    [Test]
    public async Task AddMentisClient_WithCredentials_ConfiguresOptionsOnce()
    {
        var services = new ServiceCollection();
        services.AddMentisClient(
            new Uri("http://mentis:8080"),
            TestIds.Tenant1,
            "secret",
            o => o.Timeout = TimeSpan.FromSeconds(10));

        await using ServiceProvider provider = services.BuildServiceProvider();

        MentisClientOptions options = provider.GetRequiredService<IOptions<MentisClientOptions>>().Value;
        options.Endpoint.ShouldBe(new Uri("http://mentis:8080"));
        options.GetValidatedApiKey().ShouldBe($"{TestIds.Tenant1}.secret");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        provider.GetRequiredService<MentisClient>().ShouldBeSameAs(provider.GetRequiredService<MentisClient>());
    }

    [Test]
    public void AddMentisClient_WithEmptyTenantId_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddMentisClient(new Uri("http://mentis:8080"), Guid.Empty, "secret"));
    }

    [Test]
    public void AddMentisClient_WithMissingSecret_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddMentisClient(new Uri("http://mentis:8080"), TestIds.Tenant1, " "));
    }

    [Test]
    public void Interceptor_AddsBearerHeaderAndDefaultDeadline()
    {
        var interceptor = new MentisCallInterceptor("tenant.secret", TimeSpan.FromSeconds(30));
        CallOptions captured = default;

        interceptor.AsyncUnaryCall(
            "request",
            CreateContext(new CallOptions(headers: new Metadata { { "x-custom", "1" } })),
            (_, ctx) =>
            {
                captured = ctx.Options;
                return GrpcTestCalls.Success("response");
            });

        captured.Headers!.GetValue("authorization").ShouldBe("Bearer tenant.secret");
        captured.Headers!.GetValue("x-custom").ShouldBe("1");
        captured.Deadline.ShouldNotBeNull();
        captured.Deadline!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(20));
    }

    [Test]
    public void Interceptor_KeepsExplicitDeadline()
    {
        var interceptor = new MentisCallInterceptor("tenant.secret", TimeSpan.FromSeconds(30));
        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        CallOptions captured = default;

        interceptor.AsyncUnaryCall(
            "request",
            CreateContext(new CallOptions(deadline: deadline)),
            (_, ctx) =>
            {
                captured = ctx.Options;
                return GrpcTestCalls.Success("response");
            });

        captured.Deadline.ShouldBe(deadline);
    }

    private static ClientInterceptorContext<string, string> CreateContext(CallOptions options)
    {
        var marshaller = Marshallers.Create(_ => [], _ => string.Empty);
        var method = new Method<string, string>(MethodType.Unary, "svc", "method", marshaller, marshaller);
        return new ClientInterceptorContext<string, string>(method, host: null, options);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Constructor_WithNonPositiveTimeout_Throws(int seconds)
    {
        var options = new MentisClientOptions { ApiKey = $"{TestIds.Tenant1}.secret", Timeout = TimeSpan.FromSeconds(seconds) };

        Should.Throw<ArgumentException>(() => new MentisClient(options));
    }

    [Test]
    public void Constructor_WithRelativeEndpoint_Throws()
    {
        var options = new MentisClientOptions { Endpoint = new Uri("/relative", UriKind.Relative), ApiKey = $"{TestIds.Tenant1}.secret" };

        Should.Throw<ArgumentException>(() => new MentisClient(options));
    }

    [Test]
    public void Constructor_WithEmptyTenantId_Throws()
    {
        Should.Throw<ArgumentException>(() => new MentisClient(new MentisClientOptions { TenantId = Guid.Empty, Secret = "secret" }));
    }

    [Test]
    public void Constructor_WithoutSecret_Throws()
    {
        Should.Throw<ArgumentException>(() => new MentisClient(new MentisClientOptions { TenantId = TestIds.Tenant1, Secret = " " }));
    }

    [Test]
    public void Constructor_WithNullOptions_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new MentisClient((MentisClientOptions)null!));
    }

    [Test]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        var client = new MentisClient(new MentisClientOptions { ApiKey = $"{TestIds.Tenant1}.secret" });

        await client.DisposeAsync();

        await Should.NotThrowAsync(async () => await client.DisposeAsync());
    }

    [Test]
    public void ErrorMessages_NeverContainTheSecret()
    {
        const string secret = "super-secret-value";

        ArgumentException ex = Should.Throw<ArgumentException>(
            () => new MentisClient(new MentisClientOptions { ApiKey = secret }));

        ex.Message.ShouldNotContain(secret);
    }
}
