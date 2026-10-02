using System.Runtime.CompilerServices;

namespace Mentis.AI.Sdk.Internal;

internal static class Paging
{
    /// <summary>Walks all pages, starting at page 1, until every item has been returned.</summary>
    /// <remarks>
    /// The Manager ignores the page number unless a page size is sent as well. When the caller leaves the
    /// page size to the server, the size the server applied to page 1 is therefore sent explicitly for every
    /// following page - otherwise each request would return page 1 again.
    /// </remarks>
    public static async IAsyncEnumerable<T> EnumerateAsync<T>(
        Func<int?, int?, CancellationToken, Task<PagedResult<T>>> fetchPage,
        int? pageSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int pageNumber = 1;
        long returned = 0;

        while (true)
        {
            PagedResult<T> page = await fetchPage(
                pageNumber == 1 ? null : pageNumber,
                pageSize,
                cancellationToken).ConfigureAwait(false);
            foreach (T item in page.Items)
            {
                yield return item;
            }

            returned += page.Items.Count;
            if (page.Items.Count == 0 || returned >= page.TotalCount)
            {
                yield break;
            }

            pageNumber++;
            pageSize ??= page.PageSize;
        }
    }

    public static void ValidatePaging(int? pageNumber, int? pageSize)
    {
        if (pageNumber is { } number)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number, nameof(pageNumber));

            // The Manager silently returns page 1 when only the page number is sent.
            if (pageSize is null)
            {
                throw new ArgumentException("pageSize must be set together with pageNumber.", nameof(pageSize));
            }
        }

        if (pageSize is { } size)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size, nameof(pageSize));
        }
    }
}
