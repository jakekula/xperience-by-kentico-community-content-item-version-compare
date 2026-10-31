using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Builds a word level diff of two field values and renders each side as HTML with the edits
/// highlighted in place: additions green, deletions red, replacements orange.
/// </summary>
public static class VersionFieldDiff
{
    private const string STYLE_ADDED = "background-color:#cdf3cd;border-radius:2px;padding:0 2px;";
    private const string STYLE_REMOVED = "background-color:#ffd3d3;border-radius:2px;padding:0 2px;text-decoration:line-through;";
    private const string STYLE_UPDATED = "background-color:#ffdfb3;border-radius:2px;padding:0 2px;";

    // The same three states for an image, which hides any background drawn behind it. An outline
    // is used rather than a border so that adding it cannot shift the image or the text flowing
    // around it, and the colours are the saturated forms of the three above so that the swatches
    // in the legend still read as the same three meanings.
    private const string IMAGE_ADDED = "outline:3px solid #4ca64c;outline-offset:2px;";
    private const string IMAGE_REMOVED = "outline:3px solid #d35f5f;outline-offset:2px;opacity:.65;";
    private const string IMAGE_UPDATED = "outline:3px solid #e2922f;outline-offset:2px;";

    private static readonly Regex imageTagRegex = new(
        @"^<img\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex htmlTokenRegex = new(
        @"<[^>]+>|[^<\s]+|\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex textTokenRegex = new(
        @"\S+|\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private enum Op
    {
        Equal,
        Delete,
        Insert,
        Replace
    }

    private sealed record Token(string Text, bool IsTag);

    /// <summary>
    /// How one kind of change is drawn, on a run of words and on an image.
    /// </summary>
    private sealed record Highlight(string Text, string Image);

    private static readonly Highlight added = new(STYLE_ADDED, IMAGE_ADDED);
    private static readonly Highlight removed = new(STYLE_REMOVED, IMAGE_REMOVED);
    private static readonly Highlight updated = new(STYLE_UPDATED, IMAGE_UPDATED);

    private sealed record Change(Op Op, List<Token> Older, List<Token> Newer);

    /// <summary>
    /// Renders both sides of the comparison. <paramref name="isHtml"/> keeps the markup of an HTML
    /// value intact - its tags are passed through unhighlighted so the content still renders.
    /// </summary>
    /// <param name="olderValue"></param>
    /// <param name="newerValue"></param>
    /// <param name="isHtml"></param>
    /// <param name="maxTokens">
    /// Upper bound on the words compared per side, applied to the part of the value that actually
    /// differs. The comparison allocates a table of older x newer words, so a value whose edited
    /// region exceeds this is rendered without highlighting instead.
    /// </param>
    public static (string Older, string Newer) Render(string olderValue, string newerValue, bool isHtml, int maxTokens)
    {
        var olderTokens = Tokenize(olderValue, isHtml);
        var newerTokens = Tokenize(newerValue, isHtml);

        // Matching runs at the start and the end of the two values cannot contain an edit, so they
        // are set aside and only the differing middle is compared. Without this, an edit inside a
        // long rich text field costs a table the size of the whole field - a few hundred words of
        // body text already exceed any sane limit once tags and whitespace are counted - and the
        // field would silently fall back to being shown with no highlighting at all.
        int prefix = CommonPrefix(olderTokens, newerTokens);
        int suffix = CommonSuffix(olderTokens, newerTokens, prefix);

        int olderChanged = olderTokens.Count - prefix - suffix;
        int newerChanged = newerTokens.Count - prefix - suffix;

        if (olderChanged > maxTokens || newerChanged > maxTokens)
        {
            // Say so rather than quietly showing two unmarked values that look identical.
            string notice = VersionFieldMarkup.Note(VersionComparisonStrings.DIFF_TOO_LARGE);

            return (notice + Plain(olderTokens, isHtml), notice + Plain(newerTokens, isHtml));
        }

        var changes = new List<Change>();

        if (prefix > 0)
        {
            changes.Add(new Change(Op.Equal, olderTokens.GetRange(0, prefix), []));
        }

        changes.AddRange(Diff(
            olderTokens.GetRange(prefix, olderChanged),
            newerTokens.GetRange(prefix, newerChanged)));

        if (suffix > 0)
        {
            changes.Add(new Change(Op.Equal, olderTokens.GetRange(olderTokens.Count - suffix, suffix), []));
        }

        return (RenderSide(changes, isHtml, older: true), RenderSide(changes, isHtml, older: false));
    }

    /// <summary>
    /// Counts the tokens the two values share from the start.
    /// </summary>
    private static int CommonPrefix(List<Token> older, List<Token> newer)
    {
        int limit = Math.Min(older.Count, newer.Count);
        int count = 0;

        while (count < limit && string.Equals(older[count].Text, newer[count].Text, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Counts the tokens the two values share from the end, without reaching back into the shared
    /// prefix already accounted for.
    /// </summary>
    private static int CommonSuffix(List<Token> older, List<Token> newer, int prefix)
    {
        int limit = Math.Min(older.Count, newer.Count) - prefix;
        int count = 0;

        while (count < limit && string.Equals(
            older[older.Count - 1 - count].Text,
            newer[newer.Count - 1 - count].Text,
            StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Renders a swatch of each highlight, for the legend shown above the comparison. The samples
    /// are built from the same styles as the diff itself so the two cannot drift apart.
    /// </summary>
    public static string Legend()
    {
        static string Sample(string style, string label) =>
            $"<span style=\"display:inline-block;margin:0 14px 0 0;\">" +
            $"<span style=\"{style}\">{label}</span></span>";

        return "<div style=\"margin-top:6px;\">" +
            Sample(STYLE_ADDED, VersionComparisonStrings.LEGEND_ADDED) +
            Sample(STYLE_REMOVED, VersionComparisonStrings.LEGEND_REMOVED) +
            Sample(STYLE_UPDATED, VersionComparisonStrings.LEGEND_REPLACED) +
            "</div>";
    }

    private static List<Token> Tokenize(string value, bool isHtml)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var regex = isHtml ? htmlTokenRegex : textTokenRegex;

        return [.. regex.Matches(value)
            .Select(match => new Token(match.Value, isHtml && match.Value.StartsWith('<')))];
    }

    private static List<Change> Diff(List<Token> older, List<Token> newer)
    {
        int[,] lengths = new int[older.Count + 1, newer.Count + 1];

        for (int i = older.Count - 1; i >= 0; i--)
        {
            for (int j = newer.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(older[i].Text, newer[j].Text, StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var changes = new List<Change>();
        int x = 0;
        int y = 0;

        while (x < older.Count || y < newer.Count)
        {
            if (x < older.Count && y < newer.Count &&
                string.Equals(older[x].Text, newer[y].Text, StringComparison.Ordinal))
            {
                Append(changes, Op.Equal, older[x], null);
                x++;
                y++;
            }
            else if (y < newer.Count && (x == older.Count || lengths[x, y + 1] >= lengths[x + 1, y]))
            {
                Append(changes, Op.Insert, null, newer[y]);
                y++;
            }
            else
            {
                Append(changes, Op.Delete, older[x], null);
                x++;
            }
        }

        return Pair(changes);
    }

    private static void Append(List<Change> changes, Op op, Token? older, Token? newer)
    {
        var last = changes.Count > 0 ? changes[^1] : null;

        if (last is null || last.Op != op)
        {
            last = new Change(op, [], []);
            changes.Add(last);
        }

        if (older is not null)
        {
            last.Older.Add(older);
        }

        if (newer is not null)
        {
            last.Newer.Add(newer);
        }
    }

    /// <summary>
    /// Turns a deletion immediately followed by an insertion into a single replacement, so that
    /// rewritten text reads as updated rather than as an unrelated removal and addition.
    /// </summary>
    private static List<Change> Pair(List<Change> changes)
    {
        var paired = new List<Change>();

        for (int i = 0; i < changes.Count; i++)
        {
            var current = changes[i];
            var next = i + 1 < changes.Count ? changes[i + 1] : null;

            // The two can come out of the comparison in either order, depending on which side the
            // backtracking stepped through first.
            bool isPair = next is not null &&
                ((current.Op == Op.Delete && next.Op == Op.Insert) ||
                 (current.Op == Op.Insert && next.Op == Op.Delete));

            if (isPair)
            {
                paired.Add(new Change(
                    Op.Replace,
                    current.Op == Op.Delete ? current.Older : next!.Older,
                    current.Op == Op.Insert ? current.Newer : next!.Newer));
                i++;

                continue;
            }

            paired.Add(current);
        }

        return paired;
    }

    private static string RenderSide(List<Change> changes, bool isHtml, bool older)
    {
        var builder = new StringBuilder();

        foreach (var change in changes)
        {
            switch (change.Op)
            {
                case Op.Equal:
                    Write(builder, change.Older, isHtml, null);
                    break;

                case Op.Delete when older:
                    Write(builder, change.Older, isHtml, removed);
                    break;

                case Op.Insert when !older:
                    Write(builder, change.Newer, isHtml, added);
                    break;

                case Op.Replace:
                    Write(builder, older ? change.Older : change.Newer, isHtml, updated);
                    break;
                case Op.Delete:
                    break;
                case Op.Insert:
                    break;
                default:
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes tokens, wrapping only runs of text in the highlight. Tags are emitted as they are -
    /// wrapping a block element in a span would break the rendered markup - except that an image
    /// carries its own highlight, since it is a change an editor should be able to see.
    /// </summary>
    private static void Write(StringBuilder builder, List<Token> tokens, bool isHtml, Highlight? highlight)
    {
        bool open = false;

        foreach (var token in tokens)
        {
            if (token.IsTag)
            {
                if (open)
                {
                    builder.Append("</span>");
                    open = false;
                }

                builder.Append(highlight is not null && imageTagRegex.IsMatch(token.Text)
                    ? VersionFieldMarkup.MergeStyle(token.Text, highlight.Image)
                    : token.Text);

                continue;
            }

            if (highlight is not null && !open)
            {
                builder.Append("<span style=\"").Append(highlight.Text).Append("\">");
                open = true;
            }

            builder.Append(isHtml ? token.Text : WebUtility.HtmlEncode(token.Text));
        }

        if (open)
        {
            builder.Append("</span>");
        }
    }

    private static string Plain(List<Token> tokens, bool isHtml)
    {
        var builder = new StringBuilder();

        Write(builder, tokens, isHtml, null);

        return builder.ToString();
    }
}
