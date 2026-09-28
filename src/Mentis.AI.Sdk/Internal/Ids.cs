using System.Runtime.CompilerServices;

namespace Mentis.AI.Sdk.Internal;

/// <summary>Argument checks and wire conversion for ids (the Manager uses GUIDs for every id).</summary>
internal static class Ids
{
    public static void ThrowIfEmpty(Guid id, [CallerArgumentExpression(nameof(id))] string? paramName = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The id must not be Guid.Empty.", paramName);
        }
    }

    /// <summary>Validates and converts an id list; the list is materialized so it is enumerated once.</summary>
    public static IEnumerable<string> ToWire(IEnumerable<Guid> ids, [CallerArgumentExpression(nameof(ids))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(ids, paramName);

        var result = new List<string>();
        foreach (Guid id in ids)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("The ids must not contain Guid.Empty.", paramName);
            }

            result.Add(id.ToString());
        }

        return result;
    }
}
