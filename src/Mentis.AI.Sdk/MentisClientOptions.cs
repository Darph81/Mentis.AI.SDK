using Microsoft.Extensions.Logging;

namespace Mentis.AI.Sdk;

/// <summary>
/// Connection and authentication settings for <see cref="MentisClient"/>.
/// </summary>
/// <remarks>
/// Provide either <see cref="TenantId"/> and <see cref="Secret"/>, or a pre-built
/// <see cref="ApiKey"/> of the form <c>&lt;tenantId&gt;.&lt;secret&gt;</c>.
/// </remarks>
public sealed class MentisClientOptions
{
    /// <summary>
    /// Address of the Mentis.AI Manager. Use <c>http://</c> for a direct (h2c) connection and
    /// <c>https://</c> when a TLS reverse proxy is in front of it. Defaults to <c>http://localhost:8080</c>.
    /// </summary>
    public Uri Endpoint { get; set; } = new("http://localhost:8080");

    private const int MaxAllowedRateLimitRetries = 5;

    /// <summary>The tenant id issued by the Manager administrator.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The tenant secret issued by the Manager administrator.</summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Alternative to <see cref="TenantId"/> + <see cref="Secret"/>: the combined credential
    /// <c>&lt;tenantId&gt;.&lt;secret&gt;</c>. Takes precedence when set.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Default deadline applied to every call. <see langword="null"/> means no deadline.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Optional client-side limit for request size in bytes. <see langword="null"/> means unlimited.
    /// Independently, the Manager rejects requests above its <c>GrpcHost:MaxReceiveMessageSizeBytes</c>
    /// (32 MB in its default configuration), which limits upload size.
    /// </summary>
    public int? MaxSendMessageSizeBytes { get; set; }

    /// <summary>
    /// Maximum response size in bytes (relevant for downloading document content).
    /// <see langword="null"/> keeps the gRPC default of 4 MB.
    /// </summary>
    public int? MaxReceiveMessageSizeBytes { get; set; }

    /// <summary>
    /// How often an HTTP/2 keep-alive ping is sent while a call is in progress. Defaults to 30 seconds.
    /// <see langword="null"/> disables pings.
    /// </summary>
    /// <remarks>
    /// Chat calls can take minutes, and firewalls and load balancers drop connections they see as idle. Pings keep
    /// such a connection open and detect a dead one. They do not extend a proxy's own response timeout (e.g.
    /// nginx <c>grpc_read_timeout</c>) - raise that on the proxy. Only used when the SDK creates the connection;
    /// ignored when you pass your own <c>GrpcChannel</c>.
    /// </remarks>
    public TimeSpan? KeepAlivePingDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional logger factory. When set, the SDK's gRPC channel logs its calls (start, finish, failures,
    /// retries) through it under the <c>Grpc.Net.Client</c> categories. <see langword="null"/> (the default)
    /// logs nothing. With <c>AddMentisClient</c> the container's <see cref="ILoggerFactory"/> is used when this
    /// is not set.
    /// </summary>
    /// <remarks>
    /// The credential is never logged. Only used when the SDK creates the connection; ignored when you pass
    /// your own <c>GrpcChannel</c> (give that channel its own logger factory).
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// How often a call that the Manager rejected because of its rate limit is repeated. Defaults to 0 (off).
    /// </summary>
    /// <remarks>
    /// A rate-limited call was not processed, so repeating it is safe for every operation, including sending a
    /// message. The wait is the one the Manager reports (the start of its next rate-limit window, up to a minute),
    /// within the call's <see cref="Timeout"/> if set. Only this rejection is retried - never an unreachable
    /// Manager, where a call may have been processed. Calls whose request is larger than 1 MB (large uploads) are
    /// not retried. Only used when the SDK creates the connection; ignored when you pass your own
    /// <c>GrpcChannel</c> (configure a gRPC retry policy there). Range 0-5.
    /// </remarks>
    public int MaxRateLimitRetries { get; set; }

    /// <summary>Validates the options and returns the credential sent as Bearer token.</summary>
    internal string GetValidatedApiKey()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri
            || (Endpoint.Scheme != Uri.UriSchemeHttp && Endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Endpoint must be an absolute http:// or https:// URI.", "options");
        }

        if (Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be positive.", "options");
        }

        if (MaxRateLimitRetries is < 0 or > MaxAllowedRateLimitRetries)
        {
            throw new ArgumentException($"MaxRateLimitRetries must be between 0 and {MaxAllowedRateLimitRetries}.", "options");
        }

        if (KeepAlivePingDelay is { } pingDelay && pingDelay <= TimeSpan.Zero)
        {
            throw new ArgumentException("KeepAlivePingDelay must be positive or null.", "options");
        }

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            _ = TenantIdOf(ApiKey);
            return ApiKey;
        }

        if (TenantId is not { } tenantId || tenantId == Guid.Empty || string.IsNullOrWhiteSpace(Secret))
        {
            throw new ArgumentException("Either ApiKey or both TenantId and Secret must be set.", "options");
        }

        return $"{tenantId}.{Secret}";
    }

    /// <summary>The tenant id of an API key <c>&lt;tenantId&gt;.&lt;secret&gt;</c>.</summary>
    internal static Guid TenantIdOf(string apiKey)
    {
        int separator = apiKey.IndexOf('.', StringComparison.Ordinal);
        return separator > 0
            && Guid.TryParse(apiKey.AsSpan(0, separator), out Guid tenantId)
            && tenantId != Guid.Empty
            && separator < apiKey.Length - 1
            ? tenantId
            : throw new ArgumentException("ApiKey must have the form <tenantId>.<secret>.", nameof(apiKey));
    }
}
