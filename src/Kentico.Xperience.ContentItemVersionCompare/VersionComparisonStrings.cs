namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Every piece of text the module shows, gathered in one place so that it can be reworded or
/// localized without hunting through the pages.
/// </summary>
/// <remarks>
/// To localize, replace the members with lookups through Xperience's
/// <c>CMS.Core.ILocalizationService</c> and add the corresponding resource strings to the project.
/// </remarks>
internal static class VersionComparisonStrings
{
    // Content hub action menu, and the panel that picks the pair
    public const string COMPARE_VERSIONS_ACTION = "Compare versions";
    public const string OLDER_VERSION_LABEL = "Older version";
    public const string NEWER_VERSION_LABEL = "Newer version";
    public const string VERSION_PLACEHOLDER = "Select a version";
    public const string SELECT_TWO_DIFFERENT = "Select two different versions to compare.";
    public const string COMPARE_ACTION = "Compare";
    public const string CURRENT_SUFFIX = " (Current)";
    public const string CURRENT_PUBLISHED_SUFFIX = " (Current published)";
    public const string DRAFT_ACTION = "Draft";
    public const string DRAFT_SUFFIX = " (Current draft)";
    public const string VERSIONS_NOT_LOADED = "The selected versions could not be loaded.";

    // Comparison
    public const string COMPARISON_CAPTION = "Version comparison";
    public const string COMPARISON_TIP_CONTENT = "Edits are highlighted in place:";
    public const string COMPARISON_SELECT_TWO = "Select two stored versions to compare.";
    public const string COMPARISON_DRAFT_NOT_LOADED = "The current draft of this item could not be loaded.";
    public const string COMPARISON_NO_CONTENT_TYPE = "The content type of the compared item could not be loaded.";
    public const string DIFF_TOO_LARGE = "Too much of this field changed to highlight the edits individually. Both versions are shown in full.";
    public const string EMPTY_VALUE = "—";
    public const string MISSING_REFERENCE = "Item no longer available";
    public const string VERSION_FALLBACK_ACTION = "Version";
    public const string AUTHOR_PREFIX = "by ";

    // Legend
    public const string LEGEND_ADDED = "Added";
    public const string LEGEND_REMOVED = "Removed";
    public const string LEGEND_REPLACED = "Replaced";

    // Version actions
    public const string ACTION_PUBLISHED = "Published";
    public const string ACTION_UNPUBLISHED = "Unpublished";
    public const string ACTION_PUBLISH_SCHEDULED = "Publish scheduled";
    public const string ACTION_UNPUBLISH_SCHEDULED = "Unpublish scheduled";
    public const string ACTION_PUBLISH_CANCELED = "Scheduled publish canceled";
    public const string ACTION_UNPUBLISH_CANCELED = "Scheduled unpublish canceled";
}
