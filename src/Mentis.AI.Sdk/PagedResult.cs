namespace Mentis.AI.Sdk;

/// <summary>One page of a list result.</summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>The items on this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Total number of items across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>The 1-based page number the server returned.</summary>
    public required int PageNumber { get; init; }

    /// <summary>The page size the server applied.</summary>
    public required int PageSize { get; init; }

    /// <summary>Whether more items exist after this page.</summary>
    public bool HasNextPage => PageSize > 0 && (long)PageNumber * PageSize < TotalCount;
}
