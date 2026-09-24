using Grpc.Core;
using Grpc.Core.Interceptors;

using Mentis.AI.Sdk.Internal;

using Microsoft.Extensions.DependencyInjection;

namespace Mentis.AI.Sdk.Tests;

public class MentisClientTests
{
    [Test]
    public void Options_WithTenantIdAndSecret_BuildsApiKey()
    {
        var options = new MentisClientOptions { TenantId = "tenant", Secret = "secret" };

        options.GetValidatedApiKey().ShouldBe("tenant.secret");
    }

    [Test]
    public void Options_ApiKey_TakesPrecedence()
    {
        var options = new MentisClientOptions { ApiKey = "a.b", TenantId = "tenant", Secret = "secret" };

        options.GetValidatedApiKey().ShouldBe("a.b");
    }

    [Test]
    public void Constructor_WithoutCredentials_Throws()
    {
        Should.Throw<ArgumentException>(() => new MentisClient(new MentisClientOptions { TenantId = "tenant" }));
    }

    [Test]
    public void Constructor_WithNonHttpEndpoint_Throws()
    {
        var options = new MentisClientOptions { Endpoint = new Uri("ftp://localhost"), ApiKey = "a.b" };

        Should.Throw<ArgumentException>(() => new MentisClient(options));
    }

    [Test]
    public async Task Constructor_WithValidOptions_ExposesServiceClients()
    {
        await using var client = new MentisClient(new MentisClientOptions { ApiKey = "a.b" });

        client.Documents.ShouldNotBeNull();
        client.Conversations.UserId.ShouldBeNull();
        client.Billing.ShouldNotBeNull();
    }

    [Test]
    public async Task AddMentisClient_RegistersClientAndServiceClients()
    {
        var services = new ServiceCollection();
        services.AddMentisClient(o => o.ApiKey = "a.b");

        await using ServiceProvider provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<MentisClient>();
        provider.GetRequiredService<DocumentsClient>().ShouldBeSameAs(client.Documents);
        provider.GetRequiredService<ConversationsClient>().ShouldBeSameAs(client.Conversations);
        provider.GetRequiredService<BillingClient>().ShouldBeSameAs(client.Billing);
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
}
