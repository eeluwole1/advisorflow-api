using System.Text;

namespace AdvisorFlow.Infrastructure.Crm.Salesforce;

/// <summary>
/// Builds SOQL string literals from untrusted input. Every value that comes from a
/// request (search text, status, record ids) goes through these helpers, so a value
/// can never end the literal and change the query (SOQL injection).
/// </summary>
public static class Soql
{
    /// <summary>A quoted literal for <c>=</c> and <c>IN</c> comparisons, e.g. <c>'O\'Brien'</c>.</summary>
    public static string Quote(string value) => $"'{Escape(value, escapeWildcards: false)}'";

    /// <summary>A quoted <c>LIKE</c> pattern that matches <paramref name="value"/> anywhere,
    /// with <c>%</c> and <c>_</c> in the input matched literally.</summary>
    public static string LikeContains(string value) => $"'%{Escape(value, escapeWildcards: true)}%'";

    /// <summary>A comma-separated list of quoted literals for an <c>IN (...)</c> clause.</summary>
    public static string QuoteList(IEnumerable<string> values) => string.Join(", ", values.Select(Quote));

    private static string Escape(string value, bool escapeWildcards)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '\'': sb.Append(@"\'"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case '\b': sb.Append(@"\b"); break;
                case '\f': sb.Append(@"\f"); break;
                case '%' when escapeWildcards: sb.Append(@"\%"); break;
                case '_' when escapeWildcards: sb.Append(@"\_"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
