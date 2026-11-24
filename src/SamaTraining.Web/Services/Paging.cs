using System.Globalization;
using System.Text;

namespace SamaTraining.Web.Services;

/// <summary>
/// Cursor (keyset) pagination: each page is read with "WHERE (sort key) after (last row of the previous page)"
/// and TAKE page size + 1, so the database never returns a whole table and deep pages stay fast.
/// The cursor is an opaque, URL-safe string; a damaged or forged cursor simply restarts at the first page.
/// </summary>
public static class Paging
{
    public const int DefaultSize = 25;
    public const int DetailSize = 50;

    public static string Encode(params object[] parts) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('|', parts.Select(p => p switch
        {
            DateTime d => d.Ticks.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => (p?.ToString() ?? "").Replace("|", "")
        }))))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The cursor's parts, or null when it is missing or not a cursor of this shape.</summary>
    public static string[]? Decode(string? cursor, int parts)
    {
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 200) return null;
        try
        {
            var s = cursor.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            var values = Encoding.UTF8.GetString(Convert.FromBase64String(s)).Split('|');
            return values.Length == parts ? values : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static bool TryDate(string s, out DateTime value)
    {
        value = default;
        if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
        value = new DateTime(ticks);
        return true;
    }

    public static bool TryInt(string s, out int value) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>Keeps the first <paramref name="size"/> rows and says whether more exist (the query took size + 1).</summary>
    public static (List<T> Items, bool HasMore) Trim<T>(List<T> rows, int size) =>
        rows.Count > size ? (rows.Take(size).ToList(), true) : (rows, false);
}

/// <summary>What the pager under a table needs.</summary>
public class PageLinks
{
    /// <summary>Query-string name of the cursor (each list on a page has its own).</summary>
    public string Param { get; set; } = "cursor";

    /// <summary>Cursor for the next page, or null on the last page.</summary>
    public string? Next { get; set; }

    /// <summary>True when this is not the first page.</summary>
    public bool IsPaged { get; set; }

    public int Shown { get; set; }
    public int Total { get; set; }

    /// <summary>Tab to reopen on the workshop page.</summary>
    public string? Tab { get; set; }
}
