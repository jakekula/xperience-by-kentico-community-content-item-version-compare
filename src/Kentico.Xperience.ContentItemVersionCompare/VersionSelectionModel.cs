using Kentico.Xperience.Admin.Base.FormAnnotations;

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// The pair of versions chosen in <see cref="VersionSelectionPanelPage"/>. Both hold a
/// <c>ContentItemVersionID</c> as a string, which is what a drop-down binds to. The options are
/// filled in by the page, which is the only place that knows which content item is being edited.
/// </summary>
public class VersionSelectionModel
{
    [DropDownComponent(
        Label = VersionComparisonStrings.OLDER_VERSION_LABEL,
        Placeholder = VersionComparisonStrings.VERSION_PLACEHOLDER,
        Order = 1)]
    public string OlderVersionId { get; set; } = string.Empty;

    [DropDownComponent(
        Label = VersionComparisonStrings.NEWER_VERSION_LABEL,
        Placeholder = VersionComparisonStrings.VERSION_PLACEHOLDER,
        Order = 2)]
    public string NewerVersionId { get; set; } = string.Empty;
}
