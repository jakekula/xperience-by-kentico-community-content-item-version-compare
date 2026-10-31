using System.Globalization;

using CMS.ContentEngine.Internal;

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Turns the stored version action into the wording used in the administration.
/// </summary>
internal static class VersionActionNames
{
    public static string Describe(ContentItemVersionAction action) => action switch
    {
        ContentItemVersionAction.Publish => VersionComparisonStrings.ACTION_PUBLISHED,
        ContentItemVersionAction.Unpublish => VersionComparisonStrings.ACTION_UNPUBLISHED,
        ContentItemVersionAction.SchedulePublish => VersionComparisonStrings.ACTION_PUBLISH_SCHEDULED,
        ContentItemVersionAction.ScheduleUnpublish => VersionComparisonStrings.ACTION_UNPUBLISH_SCHEDULED,
        ContentItemVersionAction.CancelScheduledPublish => VersionComparisonStrings.ACTION_PUBLISH_CANCELED,
        ContentItemVersionAction.CancelScheduledUnpublish => VersionComparisonStrings.ACTION_UNPUBLISH_CANCELED,
        _ => string.Empty
    };

    /// <summary>
    /// Describes a value coming from a listing cell, which is handed over as the underlying number.
    /// </summary>
    public static string Describe(object value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        int action = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        return Enum.IsDefined(typeof(ContentItemVersionAction), action)
            ? Describe((ContentItemVersionAction)action)
            : string.Empty;
    }
}
