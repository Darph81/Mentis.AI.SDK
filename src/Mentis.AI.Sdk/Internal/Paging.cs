using System.Runtime.CompilerServices;

namespace Mentis.AI.Sdk.Internal;

internal static class Paging
{
    /// <summary>Walks all pages, starting at page 1, until every item has been returned.</summary>
    public static async IAsyncEnumerable<T> EnumerateAsync<T>(
        Func<int, CancellationToken, Task<PagedResult<T>>> fetchPage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int pageNumber = 1;
        long returned = 0;

        while (true)
        {
            PagedResult<T> page = await fetchPage(pageNumber, cancellationToken).ConfigureAwait(false);
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
        }
    }

    public static void ValidatePaging(int? pageNumber, int? pageSize)
    {
        if (pageNumber is { } number)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number, nameof(pageNumber));
        }

        if (pageSize is { } size)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size, nameof(pageSize));
        }
    }
}
