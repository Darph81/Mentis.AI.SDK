using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

using Mentis.AI.Sdk.Internal;

using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>
/// Entry point of the SDK: one connection to a Mentis.AI Manager, authenticated as one tenant.
/// </summary>
/// <remarks>
/// The client is thread-safe. Create it once and reuse it for the lifetime of your application
/// (or register it as a singleton via <c>AddMentisClient</c>).
/// </remarks>
public sealed class MentisClient : IMentisClient, IAsyncDisposable, IDisposable
{
    private readonly GrpcChannel? _ownedChannel;

    /// <summary>Creates a client with its own connection to the Manager.</summary>
    /// <param name="options">Endpoint and tenant credentials.</param>
    public MentisClient(MentisClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string apiKey = options.GetValidatedApiKey();

        var channelOptions = new GrpcChannelOptions { ThrowOperationCanceledOnCancellation = true };
        if (options.MaxSendMessageSizeBytes is { } maxSend)
        {
            channelOptions.MaxSendMessageSize = maxSend;
        }

        if (options.MaxReceiveMessageSizeBytes is { } maxReceive)
        {
            channelOptions.MaxReceiveMessageSize = maxReceive;
        }

        _ownedChannel = GrpcChannel.ForAddress(options.Endpoint, channelOptions);
        CallInvoker invoker = _ownedChannel.Intercept(new MentisCallInterceptor(apiKey, options.Timeout));

        Documents = new DocumentsClient(new Proto.DocumentService.DocumentServiceClient(invoker), MentisClientOptions.TenantIdOf(apiKey));
        Conversations = new ConversationsClient(new Proto.ConversationService.ConversationServiceClient(invoker));
        Billing = new BillingClient(new Proto.BillingService.BillingServiceClient(invoker));
    }

    /// <summary>
    /// Creates a client on top of an existing channel, e.g. one with a custom <c>HttpHandler</c>.
    /// The channel is not disposed by this client.
    /// </summary>
    /// <param name="channel">The channel to use. Its address and size limits apply;
    /// <see cref="MentisClientOptions.Endpoint"/> and the size options are ignored.</param>
    /// <param name="options">Tenant credentials and default timeout.</param>
    public MentisClient(GrpcChannel channel, MentisClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        string apiKey = options.GetValidatedApiKey();

        CallInvoker invoker = channel.Intercept(new MentisCallInterceptor(apiKey, options.Timeout));

        Documents = new DocumentsClient(new Proto.DocumentService.DocumentServiceClient(invoker), MentisClientOptions.TenantIdOf(apiKey));
        Conversations = new ConversationsClient(new Proto.ConversationService.ConversationServiceClient(invoker));
        Billing = new BillingClient(new Proto.BillingService.BillingServiceClient(invoker));
    }

    /// <inheritdoc />
    public IDocumentsClient Documents { get; }

    /// <inheritdoc />
    public IConversationsClient Conversations { get; }

    /// <inheritdoc />
    public IBillingClient Billing { get; }

    /// <inheritdoc />
    public void Dispose() => _ownedChannel?.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
