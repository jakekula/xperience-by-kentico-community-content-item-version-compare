using System.Net;
using System.Text.RegularExpressions;

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Renders the non-textual field values of <see cref="VersionComparisonDialogPage"/>: taxonomy terms as
/// bubbles and assets as thumbnails, both carrying the colour of their change.
/// </summary>
public static class VersionFieldMarkup
{
    /// <summary>
    /// How a value changed between the two compared versions.
    /// </summary>
    public enum State
    {
        Unchanged,
        Added,
        Removed
    }

    private const string COLOR_ADDED = "#cdf3cd";
    private const string COLOR_REMOVED = "#ffd3d3";
    private const string COLOR_UNCHANGED = "#ebe9f0";

    private const string BORDER_ADDED = "#4ca64c";
    private const string BORDER_REMOVED = "#d35f5f";
    private const string BORDER_UNCHANGED = "#d5d2dd";

    private static readonly Regex styleAttributeRegex = new(
        @"\bstyle\s*=\s*(?<quote>[""'])(?<value>[^""']*)\k<quote>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Adds style declarations to an opening tag, keeping any the tag already carries.
    /// </summary>
    /// <remarks>
    /// Values written by the rich text editor arrive as finished markup, so anything the
    /// comparison needs to say about them - how an image is aligned, whether it changed - has to
    /// be written into the tags themselves.
    /// </remarks>
    /// <param name="tag">A single opening tag, including its angle brackets.</param>
    /// <param name="declarations">Declarations to add, ending in a semicolon.</param>
    public static string MergeStyle(string tag, string declarations)
    {
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(declarations))
        {
            return tag;
        }

        var style = styleAttributeRegex.Match(tag);

        if (style.Success)
        {
            string merged = $@"style=""{style.Groups["value"].Value.TrimEnd(' ', ';')};{declarations}""";

            return tag.Remove(style.Index, style.Length).Insert(style.Index, merged);
        }

        // Self-closing tags keep their slash on the end.
        return tag.Insert(tag.EndsWith("/>", StringComparison.Ordinal) ? tag.Length - 2 : tag.Length - 1, $@" style=""{declarations}""");
    }

    /// <summary>
    /// Renders a taxonomy term as a rounded bubble.
    /// </summary>
    public static string Bubble(string label, State state)
    {
        string decoration = state == State.Removed ? "text-decoration:line-through;" : string.Empty;

        return $"<span style=\"display:inline-block;margin:0 6px 6px 0;padding:3px 10px;" +
            $"border-radius:999px;font-size:12px;line-height:18px;background-color:{Background(state)};" +
            $"border:1px solid {Border(state)};{decoration}\">{WebUtility.HtmlEncode(label)}</span>";
    }

    /// <summary>
    /// Renders an asset as a thumbnail with its name, and optionally its content type, underneath.
    /// </summary>
    public static string Thumbnail(string url, string caption, State state, string? subtitle = null)
    {
        string type = string.IsNullOrWhiteSpace(subtitle)
            ? string.Empty
            : $"<div style=\"font-size:10px;line-height:14px;color:#6b6676;\">{WebUtility.HtmlEncode(subtitle)}</div>";

        return $"<figure style=\"display:inline-block;margin:0 8px 8px 0;padding:4px;text-align:center;" +
            $"border:2px solid {Border(state)};border-radius:6px;background-color:{Background(state)};\">" +
            $"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"{WebUtility.HtmlEncode(caption)}\" " +
            $"style=\"display:block;max-width:160px;max-height:110px;border-radius:4px;\" />" +
            $"<figcaption style=\"max-width:160px;font-size:11px;line-height:15px;margin-top:4px;" +
            $"overflow-wrap:anywhere;\">{WebUtility.HtmlEncode(caption)}{type}</figcaption></figure>";
    }

    /// <summary>
    /// Renders a linked content item that has no image, naming the item and its content type.
    /// </summary>
    public static string ReferenceChip(string name, string contentType, State state)
    {
        string decoration = state == State.Removed ? "text-decoration:line-through;" : string.Empty;
        string type = string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : $"<div style=\"font-size:10px;line-height:14px;color:#6b6676;\">{WebUtility.HtmlEncode(contentType)}</div>";

        return $"<span style=\"display:inline-block;margin:0 6px 6px 0;padding:4px 12px;" +
            $"border-radius:10px;font-size:12px;line-height:17px;background-color:{Background(state)};" +
            $"border:1px solid {Border(state)};{decoration}\">{WebUtility.HtmlEncode(name)}{type}</span>";
    }

    /// <summary>
    /// Renders what happened in a version and who did it, shown under the version's timestamp at
    /// the head of its column.
    /// </summary>
    public static string VersionSummary(string action, string author)
    {
        string status = string.IsNullOrWhiteSpace(action) ? VersionComparisonStrings.VERSION_FALLBACK_ACTION : action;

        return $"<div style=\"font-size:13px;line-height:19px;\">{WebUtility.HtmlEncode(status)}</div>" +
            $"<div style=\"font-size:13px;line-height:19px;color:#6b6676;\">" +
            $"{VersionComparisonStrings.AUTHOR_PREFIX}{WebUtility.HtmlEncode(author)}</div>";
    }

    /// <summary>
    /// Renders a muted aside above a value, used when the comparison has something to say about
    /// how the value is being shown rather than about the value itself.
    /// </summary>
    public static string Note(string text) =>
        $"<div style=\"margin-bottom:6px;font-size:11px;line-height:15px;color:#6b6676;" +
        $"font-style:italic;\">{WebUtility.HtmlEncode(text)}</div>";

    /// <summary>
    /// Wraps rendered items so that they wrap onto further lines instead of overflowing the card.
    /// </summary>
    public static string Wrap(IEnumerable<string> items)
    {
        string content = string.Concat(items);

        return string.IsNullOrEmpty(content)
            ? string.Empty
            : $"<div style=\"display:flex;flex-wrap:wrap;align-items:flex-start;\">{content}</div>";
    }

    private static string Background(State state) => state switch
    {
        State.Added => COLOR_ADDED,
        State.Removed => COLOR_REMOVED,
        State.Unchanged => throw new NotImplementedException(),
        _ => COLOR_UNCHANGED
    };

    private static string Border(State state) => state switch
    {
        State.Added => BORDER_ADDED,
        State.Removed => BORDER_REMOVED,
        State.Unchanged => throw new NotImplementedException(),
        _ => BORDER_UNCHANGED
    };
}
