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

    /// <summary>The tenant id (a GUID) issued by the Manager administrator.</summary>
    public string? TenantId { get; set; }

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
    /// (4 MB by default), which limits upload size.
    /// </summary>
    public int? MaxSendMessageSizeBytes { get; set; }

    /// <summary>
    /// Maximum response size in bytes (relevant for downloading document content).
    /// <see langword="null"/> keeps the gRPC default of 4 MB.
    /// </summary>
    public int? MaxReceiveMessageSizeBytes { get; set; }

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

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey;
        }

        if (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(Secret))
        {
            throw new ArgumentException("Either ApiKey or both TenantId and Secret must be set.", "options");
        }

        return $"{TenantId}.{Secret}";
    }
}
