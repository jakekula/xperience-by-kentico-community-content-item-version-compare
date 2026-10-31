using System.Globalization;

using CMS.Base;
using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.ContentItemVersionCompare;

using Microsoft.Extensions.Options;

[assembly: UIPage(
    parentType: typeof(ContentItemEditSection),
    slug: PageParameterConstants.PARAMETERIZED_SLUG,
    uiPageType: typeof(VersionComparisonDialogPage),
    name: "Version comparison",
    templateName: TemplateNames.OVERVIEW,
    order: 100)]

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Shows two versions of a content item side by side, one row of cards per field, with the edits
/// highlighted inside each value. Opened from <see cref="VersionSelectionPanelPage"/>.
/// </summary>
/// <remarks>
/// Registered beside the picker rather than beneath it. The edit template the picker is built on
/// carries no <c>RoutingContentPlaceholder</c> and so cannot render child routes: a comparison
/// nested under it would change the URL and then mount nothing. Both hang off the editing section,
/// which does render its children.
/// </remarks>
[UINavigation(false)]
[UIBreadcrumbs(false)]
[UIPageLocation(PageLocationEnum.Dialog)]
public class VersionComparisonDialogPage : OverviewPageBase
{
    private const char PARAMETER_SEPARATOR = '-';

    /// <summary>
    /// Stands for the item's current draft, which is not a stored version and so has no identifier
    /// of its own. Stored versions are numbered from one, and a negative number could not be told
    /// apart from the separator between the two compared identifiers.
    /// </summary>
    internal const int DRAFT_VERSION_ID = 0;






    private readonly IInfoProvider<ContentItemVersionInfo> contentItemVersionInfoProvider;
    private readonly IInfoProvider<ContentItemInfo> contentItemInfoProvider;
    private readonly IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider;
    private readonly IInfoProvider<UserInfo> userInfoProvider;
    private readonly IInfoProvider<ContentItemLanguageMetadataInfo> contentItemLanguageMetadataInfoProvider;
    private readonly IContentItemVersionDataSerializer versionDataSerializer;
    private readonly IContentItemDataInfoProviderAccessor contentItemDataInfoProviderAccessor;
    private readonly IInfoProvider<ContentItemCommonDataInfo> contentItemCommonDataInfoProvider;
    private readonly VersionFieldRenderer fieldRenderer;

    private bool draftUnavailable;

    public VersionComparisonDialogPage(
        IInfoProvider<ContentItemVersionInfo> contentItemVersionInfoProvider,
        IInfoProvider<ContentItemInfo> contentItemInfoProvider,
        IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
        IInfoProvider<UserInfo> userInfoProvider,
        IInfoProvider<ContentItemLanguageMetadataInfo> contentItemLanguageMetadataInfoProvider,
        IContentItemVersionDataSerializer versionDataSerializer,
        IContentItemDataInfoProviderAccessor contentItemDataInfoProviderAccessor,
        IInfoProvider<ContentItemCommonDataInfo> contentItemCommonDataInfoProvider,
        ITaxonomyRetriever taxonomyRetriever,
        IContentItemAssetUrlProvider assetUrlProvider,
        IOptions<VersionComparisonOptions> options)
    {
        fieldRenderer = new VersionFieldRenderer(
            taxonomyRetriever,
            assetUrlProvider,
            contentItemInfoProvider,
            contentItemLanguageMetadataInfoProvider,
            options.Value);

        this.contentItemDataInfoProviderAccessor = contentItemDataInfoProviderAccessor;
        this.contentItemCommonDataInfoProvider = contentItemCommonDataInfoProvider;
        this.contentItemVersionInfoProvider = contentItemVersionInfoProvider;
        this.contentItemInfoProvider = contentItemInfoProvider;
        this.contentLanguageInfoProvider = contentLanguageInfoProvider;
        this.userInfoProvider = userInfoProvider;
        this.contentItemLanguageMetadataInfoProvider = contentItemLanguageMetadataInfoProvider;
        this.versionDataSerializer = versionDataSerializer;
    }

    /// <summary>
    /// The two compared version identifiers, taken from the URL as "older-newer".
    /// </summary>
    [PageParameter(typeof(StringPageModelBinder), typeof(VersionComparisonDialogPage))]
    public string VersionIds { get; set; } = string.Empty;

    /// <summary>
    /// Builds the URL parameter identifying the compared pair.
    /// </summary>
    public static string BuildParameter(int olderVersionId, int newerVersionId) =>
        $"{olderVersionId}{PARAMETER_SEPARATOR}{newerVersionId}";

    public override async Task ConfigurePage()
    {
        PageConfiguration.Caption = VersionComparisonStrings.COMPARISON_CAPTION;

        var versions = await GetVersions();

        if (versions.Count != 2)
        {
            PageConfiguration.ErrorMessage = draftUnavailable
                ? VersionComparisonStrings.COMPARISON_DRAFT_NOT_LOADED
                : VersionComparisonStrings.COMPARISON_SELECT_TWO;

            await base.ConfigurePage();

            return;
        }

        string olderCaption = FormatTimestamp(versions[0]);
        string newerCaption = FormatTimestamp(versions[1]);

        PageConfiguration.Callouts =
        [
            new CalloutConfiguration
            {
                Headline = $"Comparing {olderCaption} with {newerCaption}",
                Content = VersionComparisonStrings.COMPARISON_TIP_CONTENT + VersionFieldDiff.Legend(),
                ContentAsHtml = true,
                Type = CalloutType.QuickTip,
                Placement = CalloutPlacement.OnDesk
            }
        ];

        var item = await GetContentItem(versions[0].ContentItemVersionContentItemID);
        var contentType = item is null ? null : DataClassInfoProvider.GetDataClassInfo(item.ContentItemContentTypeID);
        var (Name, Id) = await GetDefaultContentLanguage();
        string languageName = Name;

        if (contentType is null)
        {
            PageConfiguration.ErrorMessage = VersionComparisonStrings.COMPARISON_NO_CONTENT_TYPE;

            await base.ConfigurePage();

            return;
        }

        var olderData = versionDataSerializer.Deserialize(versions[0].ContentItemVersionData, contentType)?.Data;
        var newerData = versionDataSerializer.Deserialize(versions[1].ContentItemVersionData, contentType)?.Data;

        // Only the fields of the content type itself - the system columns carried in the same form
        // definition (identifiers, GUIDs) are not content and would just add empty rows.
        var fields = new FormInfo(contentType.ClassFormDefinition)
            .GetFields(true, true)
            .Where(field => !field.PrimaryKey && !field.System);

        // The two compared versions are described once, at the head of their columns.
        var headers = await BuildVersionHeaders(versions);

        PageConfiguration.CardGroups.AddCardGroup(group =>
        {
            group.UseEqualWidth = true;
            group.AddCard(headers[0]);
            group.AddCard(headers[1]);
        });

        foreach (var field in fields)
        {
            string olderValue = ReadValue(olderData, field.Name);
            string newerValue = ReadValue(newerData, field.Name);

            // A field holding nothing in either version has nothing to compare, and a row of two
            // dashes takes as much height as a row that says something.
            if (string.IsNullOrWhiteSpace(olderValue) && string.IsNullOrWhiteSpace(newerValue))
            {
                continue;
            }

            string caption = string.IsNullOrWhiteSpace(field.Caption) ? field.Name : field.Caption;

            var (olderMarkup, newerMarkup) = await fieldRenderer.RenderField(field, olderValue, newerValue, item!, languageName, Id);

            PageConfiguration.CardGroups.AddCardGroup(group =>
            {
                group.UseEqualWidth = true;
                group.AddCard(BuildValueCard(caption, olderMarkup));
                group.AddCard(BuildValueCard(caption, newerMarkup));
            });
        }

        await base.ConfigurePage();
    }









    /// <summary>
    /// Builds the column headers describing each compared version: when it was created, what
    /// happened, who did it, and whether it is the item's most recent stored version.
    /// </summary>
    private async Task<IList<OverviewCard>> BuildVersionHeaders(IList<ContentItemVersionInfo> versions)
    {
        int currentVersionId = await GetCurrentVersionId(versions[0].ContentItemVersionContentItemID);
        var authors = await GetAuthors(versions.Select(version => version.ContentItemVersionCreatedByUserID));

        return [.. versions.Select(version =>
        {
            bool isDraft = version.ContentItemVersionID == DRAFT_VERSION_ID;
            bool isCurrent = version.ContentItemVersionID == currentVersionId;
            string author = authors.TryGetValue(version.ContentItemVersionCreatedByUserID, out string? name)
                ? name
                : "Unknown user";

            string suffix = isDraft
                ? VersionComparisonStrings.DRAFT_SUFFIX
                : isCurrent
                    ? VersionComparisonStrings.CURRENT_SUFFIX
                    : string.Empty;

            return new OverviewCard
            {
                Headline = FormatTimestamp(version) + suffix,
                Description = new StringContentCardComponent
                {
                    Content = VersionFieldMarkup.VersionSummary(
                        isDraft
                            ? VersionComparisonStrings.DRAFT_ACTION
                            : VersionActionNames.Describe(version.ContentItemVersionAction),
                        author),
                    ContentAsHtml = true
                }
            };
        })];
    }

    private async Task<int> GetCurrentVersionId(int contentItemId)
    {
        var latest = await contentItemVersionInfoProvider
            .Get()
            .WhereEquals(nameof(ContentItemVersionInfo.ContentItemVersionContentItemID), contentItemId)
            .OrderByDescending(nameof(ContentItemVersionInfo.ContentItemVersionCreatedWhen))
            .Columns(nameof(ContentItemVersionInfo.ContentItemVersionID))
            .TopN(1)
            .GetEnumerableTypedResultAsync();

        return latest.FirstOrDefault()?.ContentItemVersionID ?? 0;
    }

    private async Task<Dictionary<int, string>> GetAuthors(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().Where(id => id > 0).ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var users = await userInfoProvider
            .Get()
            .WhereIn(nameof(UserInfo.UserID), ids)
            .Columns(
                nameof(UserInfo.UserID),
                nameof(UserInfo.UserName),
                nameof(UserInfo.FirstName),
                nameof(UserInfo.LastName))
            .GetEnumerableTypedResultAsync();

        return users.ToDictionary(
            user => user.UserID,
            user => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName);
    }

    private static OverviewCard BuildValueCard(string headline, string markup) =>
        new()
        {
            Headline = headline,
            Description = new StringContentCardComponent
            {
                Content = string.IsNullOrWhiteSpace(markup) ? VersionComparisonStrings.EMPTY_VALUE : markup,
                // The diff output is always markup, and a field holding HTML has to render as HTML
                // anyway - otherwise the cards would show tag names instead of formatted content.
                ContentAsHtml = true
            }
        };







    private static string ReadValue(IDataContainer? data, string fieldName)
    {
        if (data is null || !data.ContainsColumn(fieldName))
        {
            return string.Empty;
        }

        return Convert.ToString(data.GetValue(fieldName), CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private async Task<IList<ContentItemVersionInfo>> GetVersions()
    {
        var ids = ParseVersionIds(VersionIds);

        if (ids.Count != 2)
        {
            return [];
        }

        var storedIds = ids.Where(static id => id != DRAFT_VERSION_ID).ToList();

        var loaded = (await contentItemVersionInfoProvider
            .Get()
            .WhereIn(nameof(ContentItemVersionInfo.ContentItemVersionID), storedIds)
            .GetEnumerableTypedResultAsync())
            .ToList();

        if (storedIds.Count < ids.Count && loaded.Count > 0)
        {
            var draft = await GetDraftVersion(loaded[0].ContentItemVersionContentItemID);

            // Distinguished from an unreadable pair of identifiers, which is a different problem
            // with a different remedy.
            draftUnavailable = draft is null;

            if (draft is not null)
            {
                loaded.Add(draft);
            }
        }

        // Ordered oldest first so that the left-hand card always holds the earlier version.
        return [.. loaded.OrderBy(version => version.ContentItemVersionCreatedWhen)];
    }

    /// <summary>
    /// Builds a stand-in version from the item's current draft, so that work in progress can be
    /// compared against what is published.
    /// </summary>
    /// <remarks>
    /// A draft is not a stored version - it is the item's live data, which Xperience only copies
    /// into the version history when the item is published. It is turned into the same serialized
    /// shape a stored version holds, using the serializer Xperience itself uses when it stores
    /// one, so that everything downstream reads a draft and a version the same way.
    /// </remarks>
    private async Task<ContentItemVersionInfo?> GetDraftVersion(int contentItemId)
    {
        var item = await GetContentItem(contentItemId);
        var contentType = item is null ? null : DataClassInfoProvider.GetDataClassInfo(item.ContentItemContentTypeID);

        var (_, Id) = await GetDefaultContentLanguage();

        if (contentType is null || Id == 0)
        {
            return null;
        }

        var commonData = (await contentItemCommonDataInfoProvider
            .Get()
            .WhereEquals(nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentItemID), contentItemId)
            .WhereEquals(nameof(ContentItemCommonDataInfo.ContentItemCommonDataContentLanguageID), Id)
            .WhereTrue(nameof(ContentItemCommonDataInfo.ContentItemCommonDataIsLatest))
            .TopN(1)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        if (commonData is null)
        {
            return null;
        }

        // The row holding the content type's own fields, which the serializer needs alongside
        // the common data to produce what a stored version holds.
        var data = (await contentItemDataInfoProviderAccessor
            .Get(contentType.ClassName)
            .Get()
            .WhereEquals(nameof(ContentItemDataInfo.ContentItemDataCommonDataID), commonData.ContentItemCommonDataID)
            .TopN(1)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        if (data is null)
        {
            return null;
        }

        var metadata = await GetLanguageMetadata(contentItemId, Id);

        return new ContentItemVersionInfo
        {
            ContentItemVersionID = DRAFT_VERSION_ID,
            ContentItemVersionContentItemID = contentItemId,
            ContentItemVersionContentLanguageID = Id,
            // A draft has no version of its own, so it is described by when the item was last
            // touched and by whoever touched it.
            ContentItemVersionCreatedWhen = metadata?.ContentItemLanguageMetadataModifiedWhen ?? DateTime.Now,
            ContentItemVersionCreatedByUserID = metadata?.ContentItemLanguageMetadataModifiedByUserID ?? 0,
            ContentItemVersionData = versionDataSerializer.Serialize(commonData, data, contentType)
        };
    }

    private async Task<ContentItemLanguageMetadataInfo?> GetLanguageMetadata(int contentItemId, int contentLanguageId)
    {
        var metadata = await contentItemLanguageMetadataInfoProvider
            .Get()
            .WhereEquals(nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentItemID), contentItemId)
            .WhereEquals(nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentLanguageID), contentLanguageId)
            .TopN(1)
            .GetEnumerableTypedResultAsync();

        return metadata.FirstOrDefault();
    }

    private async Task<ContentItemInfo?> GetContentItem(int contentItemId)
    {
        var contentItems = await contentItemInfoProvider
            .Get()
            .WhereEquals(nameof(ContentItemInfo.ContentItemID), contentItemId)
            .Columns(
                nameof(ContentItemInfo.ContentItemID),
                nameof(ContentItemInfo.ContentItemGUID),
                nameof(ContentItemInfo.ContentItemContentTypeID))
            .TopN(1)
            .GetEnumerableTypedResultAsync();

        return contentItems.FirstOrDefault();
    }

    private async Task<(string Name, int Id)> GetDefaultContentLanguage()
    {
        var languages = await contentLanguageInfoProvider
            .Get()
            .WhereTrue(nameof(ContentLanguageInfo.ContentLanguageIsDefault))
            .Columns(
                nameof(ContentLanguageInfo.ContentLanguageName),
                nameof(ContentLanguageInfo.ContentLanguageID))
            .TopN(1)
            .GetEnumerableTypedResultAsync();

        var language = languages.FirstOrDefault();

        return (language?.ContentLanguageName ?? string.Empty, language?.ContentLanguageID ?? 0);
    }

    /// <summary>
    /// Reads the compared pair out of the URL. Anything that is not a number is dropped, but zero
    /// is kept: it is <see cref="DRAFT_VERSION_ID"/>, not a failure to parse.
    /// </summary>
    private static List<int> ParseVersionIds(string parameter) =>
        string.IsNullOrWhiteSpace(parameter)
            ? []
            : [.. parameter
                .Split(PARAMETER_SEPARATOR, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out int id) ? id : -1)
                .Where(id => id >= 0)];

    // Seconds are included because versions published in the same minute would otherwise be
    // indistinguishable, making it unclear which two versions are on screen.
    private static string FormatTimestamp(ContentItemVersionInfo version) =>
        version.ContentItemVersionCreatedWhen.ToString("G", CultureInfo.CurrentCulture);
}
