namespace Mentis.AI.Sdk;

/// <summary>Token usage of a tenant in one calendar month.</summary>
public sealed record TenantUsage
{
    /// <summary>The tenant the usage belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Calendar year.</summary>
    public required int Year { get; init; }

    /// <summary>Calendar month (1-12).</summary>
    public required int Month { get; init; }

    /// <summary>Tokens consumed in this month.</summary>
    public required long TotalTokens { get; init; }

    /// <summary>
    /// The tenant's current monthly token limit; <see langword="null"/> means unlimited.
    /// This is the current setting, not a historical value.
    /// </summary>
    public long? MonthlyTokenLimit { get; init; }
}
