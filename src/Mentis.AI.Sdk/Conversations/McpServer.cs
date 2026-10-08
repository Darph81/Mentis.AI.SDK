using Proto = Mentis.AI.Sdk.Internal.Grpc;

namespace Mentis.AI.Sdk;

/// <summary>
/// A remote MCP (Model Context Protocol) server the model may use for <b>one</b>
/// <see cref="IConversationsClient.SendMessageAsync"/> call (Streamable HTTP transport only).
/// </summary>
/// <remarks>
/// The configuration is ephemeral: the Manager neither stores nor logs the URL or the auth header. The
/// Manager executes the tool calls automatically (bounded by its own round limits), audits every call by tool
/// name and host, and is not available to admin tokens. Failures worth knowing (<see cref="MentisException.ErrorCode"/>):
/// <c>Mcp.ServerNotAllowed</c> (host not on the deployment's allow-list), <c>Mcp.InsecureServerUrl</c>
/// (plain <c>http</c> while the deployment requires https), <c>Mcp.InvalidServerUrl</c>,
/// <c>Mcp.NoAllowedToolsAvailable</c> (none of <see cref="AllowedToolNames"/> is offered by the server) and
/// <c>Mcp.Timeout</c>.
/// </remarks>
public sealed record McpServer
{
    /// <summary>
    /// Absolute URL of the MCP endpoint (normally <c>https</c>). Its host must be on the deployment's
    /// allow-list, otherwise the call fails with <c>PermissionDenied</c> before any connection is made.
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Complete <c>Authorization</c> header value sent to the MCP server, e.g. <c>"Bearer abc123"</c>.
    /// Optional. Never included in <see cref="ToString"/>.
    /// </summary>
    public string? AuthHeader { get; init; }

    /// <summary>
    /// Names of the tools the model may call. Only tools that are in this list <b>and</b> offered by the server
    /// can be called. Must not be empty.
    /// </summary>
    public required IReadOnlyList<string> AllowedToolNames { get; init; }

    /// <summary>Redacted: neither the URL (it may carry tokens) nor the auth header is printed.</summary>
    public override string ToString() => $"McpServer {{ Host = {(Url.IsAbsoluteUri ? Url.Authority : "?")}, Tools = {AllowedToolNames.Count} }}";

    internal Proto.McpServer ToProto()
    {
        if (!Url.IsAbsoluteUri)
        {
            throw new ArgumentException("The MCP server URL must be absolute.", nameof(Url));
        }

        if (AllowedToolNames is null || AllowedToolNames.Count == 0 || AllowedToolNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-blank tool name is required.", nameof(AllowedToolNames));
        }

        var proto = new Proto.McpServer { Url = Url.AbsoluteUri };
        proto.AllowedToolNames.AddRange(AllowedToolNames);
        if (!string.IsNullOrEmpty(AuthHeader))
        {
            proto.AuthHeader = AuthHeader;
        }

        return proto;
    }
}
