namespace Mentis.AI.Sdk;

/// <summary>
/// One connection to a Mentis.AI Manager, authenticated as one tenant.
/// Implemented by <see cref="MentisClient"/>; depend on this interface to keep your code testable.
/// </summary>
public interface IMentisClient
{
    /// <summary>Upload, search and manage documents.</summary>
    IDocumentsClient Documents { get; }

    /// <summary>
    /// Tenant-global conversations. Use <see cref="IConversationsClient.ForUser"/> for the conversations
    /// of a single end user.
    /// </summary>
    IConversationsClient Conversations { get; }

    /// <summary>Token usage of the tenant.</summary>
    IBillingClient Billing { get; }
}
