using System.Text.RegularExpressions;

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Prepares stored rich text for display outside the channel it was written for.
/// </summary>
/// <remarks>
/// Stored rich text assumes a rendering context the administration does not provide: URLs are
/// application-relative, and image alignment is carried by a class styled only on the live site.
/// Both are rewritten so the value renders as the editor saw it.
/// </remarks>
public static class VersionRichText
{
    private static readonly Regex htmlTagRegex = new(
        @"<\s*/?\s*[a-zA-Z][a-zA-Z0-9]*(\s[^<>]*)?/?\s*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches the application-relative prefix of a URL held in a markup attribute.
    /// </summary>
    private static readonly Regex appRelativeUrlRegex = new(
        @"(?<attribute>\b(?:src|href|poster)\s*=\s*[""'])~/",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches an element that may carry the rich text editor's alignment classes, and the two
    /// attributes that decide what to do with it.
    /// </summary>
    private static readonly Regex alignableElementRegex = new(
        @"<(?:img|span)\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex classAttributeRegex = new(
        @"\bclass\s*=\s*(?<quote>[""'])(?<value>[^""']*)\k<quote>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsHtml(string value) =>
        !string.IsNullOrWhiteSpace(value) && htmlTagRegex.IsMatch(value);

    /// <summary>
    /// Rewrites the application-relative URLs that rich text stores into root-relative ones.
    /// </summary>
    /// <remarks>
    /// Assets are stored as <c>src="~/getContentAsset/..."</c>. Xperience resolves the <c>~</c>
    /// when rendering on a channel; nothing resolves it here, so the browser would request the
    /// image beneath the comparison's own URL and show it broken.
    /// </remarks>
    public static string ResolveAppRelativeUrls(string html) =>
        string.IsNullOrEmpty(html) ? html : appRelativeUrlRegex.Replace(html, "${attribute}/");

    /// <summary>
    /// Makes a stored rich text value renderable outside the channel it was written for.
    /// </summary>
    public static string PrepareRichText(string html) =>
        ApplyRichTextAlignment(ResolveAppRelativeUrls(html));

    /// <summary>
    /// Turns the rich text editor's alignment classes into inline styles.
    /// </summary>
    /// <remarks>
    /// Alignment is recorded as a class: <c>fr-fil</c> left, <c>fr-fir</c> right, <c>fr-dib</c>
    /// centred. The rules shipped with the admin are scoped to a <c>.fr-view</c> container the
    /// comparison is not part of, so without this the copy beside an image never wraps.
    /// </remarks>
    public static string ApplyRichTextAlignment(string html) =>
        string.IsNullOrEmpty(html) ? html : alignableElementRegex.Replace(html, static element =>
        {
            string tag = element.Value;
            var classes = classAttributeRegex.Match(tag);

            if (!classes.Success)
            {
                return tag;
            }

            string declarations = AlignmentDeclarations(classes.Groups["value"].Value);

            if (declarations.Length == 0)
            {
                return tag;
            }

            // The width the editor stored lives in the same attribute, so the alignment is added
            // to it rather than replacing it.
            return VersionFieldMarkup.MergeStyle(tag, declarations);
        });

    private static string AlignmentDeclarations(string classes)
    {
        string[] names = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (names.Contains("fr-fil"))
        {
            return "float:left;margin:4px 16px 8px 0;";
        }

        if (names.Contains("fr-fir"))
        {
            return "float:right;margin:4px 0 8px 16px;";
        }

        // An inline image needs nothing: it already sits in the flow where the editor put it.
        return names.Contains("fr-dib") ? "display:block;margin-left:auto;margin-right:auto;" : string.Empty;
    }

    /// <summary>
    /// Keeps a floated image inside the card it belongs to instead of letting it hang into the
    /// rows below.
    /// </summary>
    public static string ContainFloats(string markup) =>
        string.IsNullOrEmpty(markup) ? markup : $@"<div style=""display:flow-root;"">{markup}</div>";
}
