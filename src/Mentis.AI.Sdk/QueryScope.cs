namespace Mentis.AI.Sdk;

/// <summary>
/// Which documents something covers: the tenant's own documents, the global documents shared with every
/// tenant, or both.
/// </summary>
/// <remarks>
/// Used in two places:
/// <list type="bullet">
/// <item><see cref="ChatMessage.QueryScope"/> - which documents the Manager searched to answer a message.</item>
/// <item>The <c>scope</c> filter of <see cref="IDocumentsClient.ListAsync"/> and
/// <see cref="IDocumentsClient.EnumerateAsync"/>.</item>
/// </list>
/// </remarks>
public enum QueryScope
{
    /// <summary>Not set or not known to this SDK version.</summary>
    Unknown = 0,

    /// <summary>Only the global documents shared with every tenant.</summary>
    Global = 1,

    /// <summary>Only the tenant's own documents.</summary>
    Tenant = 2,

    /// <summary>The tenant's own documents and the global documents.</summary>
    Both = 3,
}
