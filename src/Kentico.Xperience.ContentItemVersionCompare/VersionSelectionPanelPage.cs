using System.Globalization;
using System.Text;

using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.Membership;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.ContentItemVersionCompare;

[assembly: UIPage(
    parentType: typeof(ContentItemEditSection),
    slug: "compare-versions",
    uiPageType: typeof(VersionSelectionPanelPage),
    name: "Compare versions",
    templateName: TemplateNames.EDIT,
    order: UIPageOrder.NoOrder)]

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Picks the two versions to compare, in a side panel over a content item in the Content hub.
/// Opened by the <em>Compare versions</em> entry that
/// <see cref="ContentItemEditCompareVersionsExtender"/> adds to the item's action menu.
/// </summary>
/// <remarks>
/// <para>
/// The pair is chosen through two drop-downs rather than by ticking rows in a listing, because a
/// listing cannot size the surface it sits in: it scrolls its table instead of letting it grow,
/// which leaves a small scrolling box no server-side setting can widen. The edit template used
/// here sizes itself, and is the same template behind the <em>Unpublish options</em> panel in
/// this very menu.
/// </para>
/// <para>
/// It is a side panel rather than a dialog because the edit template pins a dialog to a minimum
/// of <c>65vw</c>, far wider than two drop-downs need, and not adjustable from here. Its side
/// panel is a fixed 600px.
/// </para>
/// </remarks>
[UINavigation(false)]
[UIBreadcrumbs(false)]
[UIPageLocation(PageLocationEnum.SidePanel)]
public class VersionSelectionPanelPage : ModelEditPage<VersionSelectionModel>
{
    private const char OPTION_VALUE_SEPARATOR = ';';

    private readonly IInfoProvider<ContentItemVersionInfo> contentItemVersionInfoProvider;
    private readonly IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider;
    private readonly IInfoProvider<UserInfo> userInfoProvider;
    private readonly IInfoProvider<ContentItemLanguageMetadataInfo> contentItemLanguageMetadataInfoProvider;
    private readonly IPageLinkGenerator pageLinkGenerator;

    private readonly VersionSelectionModel model = new();

    public VersionSelectionPanelPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        IInfoProvider<ContentItemVersionInfo> contentItemVersionInfoProvider,
        IInfoProvider<ContentLanguageInfo> contentLanguageInfoProvider,
        IInfoProvider<UserInfo> userInfoProvider,
        IInfoProvider<ContentItemLanguageMetadataInfo> contentItemLanguageMetadataInfoProvider,
        IPageLinkGenerator pageLinkGenerator)
        : base(formItemCollectionProvider, formDataBinder)
    {
        this.contentItemVersionInfoProvider = contentItemVersionInfoProvider;
        this.contentLanguageInfoProvider = contentLanguageInfoProvider;
        this.userInfoProvider = userInfoProvider;
        this.contentItemLanguageMetadataInfoProvider = contentItemLanguageMetadataInfoProvider;
        this.pageLinkGenerator = pageLinkGenerator;
    }

    /// <summary>
    /// Identifier of the edited content item, taken from the editing section in the URL.
    /// </summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ContentItemEditSection))]
    public int ItemID { get; set; }

    /// <summary>
    /// Workspace the item is edited in. Bound only so that the path of the comparison can repeat
    /// it: the link generator requires a value for every parameterized page above its target and
    /// does not read them from the route of the page asking for the link.
    /// </summary>
    [PageParameter(typeof(IntPageModelBinder), typeof(ContentHubWorkspace))]
    public int WorkspaceId { get; set; }

    /// <summary>
    /// Language variant being edited, bound for the same reason as <see cref="WorkspaceId"/>.
    /// </summary>
    [PageParameter(typeof(StringPageModelBinder), typeof(ContentHubContentLanguage))]
    public string LanguageName { get; set; } = string.Empty;

    /// <summary>
    /// Folder the item was opened from, bound for the same reason as <see cref="WorkspaceId"/>.
    /// Taken as the raw URL segment rather than as a <see cref="ContentFolderId"/>, because the
    /// binder that parses it is internal to Xperience. The segment is handed straight back to the
    /// generator, which writes it out the same way.
    /// </summary>
    [PageParameter(typeof(StringPageModelBinder), typeof(ContentHubFolder))]
    public string FolderId { get; set; } = string.Empty;

    protected override VersionSelectionModel Model => model;

    public override async Task ConfigurePage()
    {
        await base.ConfigurePage();

        // The headline is what the edit template puts in the dialog's own header.
        PageConfiguration.Headline = VersionComparisonStrings.COMPARE_VERSIONS_ACTION;
        PageConfiguration.SubmitConfiguration.Label = VersionComparisonStrings.COMPARE_ACTION;
    }

    /// <summary>
    /// Allows the panel to anyone who can reach the content item, rather than requiring a
    /// permission of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The edit template this panel is built on requires the <c>Update</c> permission by default,
    /// and evaluates it as an <em>application</em> permission of whichever application the page
    /// belongs to - here the Content hub. The Content hub declares only
    /// <c>WorkspaceDataPermission</c>s and no application permissions at all, so that check can
    /// never pass for a non-administrator and the panel renders disabled, however the user's roles
    /// are configured.
    /// </para>
    /// <para>
    /// Comparing two stored versions reads data and changes nothing, so the check is answered as
    /// granted. Reaching this panel already requires access to the content item it belongs to.
    /// </para>
    /// </remarks>
    protected override Task<Func<string, Task<PermissionEvaluationResult>>> GetPagePermissionEvaluator() =>
        Task.FromResult<Func<string, Task<PermissionEvaluationResult>>>(
            static _ => Task.FromResult<PermissionEvaluationResult>(UIPermissionEvaluationResult.Success));

    /// <summary>
    /// Fills both drop-downs with the item's stored versions, newest first.
    /// </summary>
    protected override async Task<ICollection<IFormItem>> GetFormItems()
    {
        var items = await base.GetFormItems();
        string options = await GetVersionOptions();

        foreach (var component in items.OfType<IFormComponent>())
        {
            if (component.Properties is DropDownProperties dropDown)
            {
                dropDown.Options = options;
            }
        }

        return items;
    }

    /// <summary>
    /// Opens the comparison for the chosen pair instead of saving anything.
    /// </summary>
    protected override async Task<ICommandResponse> ProcessFormData(VersionSelectionModel selection, ICollection<IFormItem> formItems)
    {
        if (!int.TryParse(selection.OlderVersionId, out int firstId)
            || !int.TryParse(selection.NewerVersionId, out int secondId)
            || firstId == secondId)
        {
            return Response().AddErrorMessage(VersionComparisonStrings.SELECT_TWO_DIFFERENT);
        }

        // The draft is the item as it stands now, so it is always the later of the pair and has
        // no stored row to order by.
        if (firstId == VersionComparisonDialogPage.DRAFT_VERSION_ID || secondId == VersionComparisonDialogPage.DRAFT_VERSION_ID)
        {
            int published = firstId == VersionComparisonDialogPage.DRAFT_VERSION_ID ? secondId : firstId;

            return NavigateTo(GetComparisonPath(published, VersionComparisonDialogPage.DRAFT_VERSION_ID));
        }

        var versions = await contentItemVersionInfoProvider
            .Get()
            .WhereIn(nameof(ContentItemVersionInfo.ContentItemVersionID), new[] { firstId, secondId })
            .Columns(
                nameof(ContentItemVersionInfo.ContentItemVersionID),
                nameof(ContentItemVersionInfo.ContentItemVersionCreatedWhen))
            .GetEnumerableTypedResultAsync();

        // The older version is shown in the left-hand column of the comparison, whichever way
        // round the editor picked them.
        var orderedIds = versions
            .OrderBy(version => version.ContentItemVersionCreatedWhen)
            .Select(version => version.ContentItemVersionID)
            .ToList();

        if (orderedIds.Count != 2)
        {
            return Response().AddErrorMessage(VersionComparisonStrings.VERSIONS_NOT_LOADED);
        }

        return NavigateTo(GetComparisonPath(orderedIds[0], orderedIds[1]));
    }

    private string GetComparisonPath(int olderVersionId, int newerVersionId) =>
        pageLinkGenerator.GetPath<VersionComparisonDialogPage>(new PageParameterValues
        {
            { typeof(ContentHubWorkspace), WorkspaceId },
            { typeof(ContentHubContentLanguage), LanguageName },
            { typeof(ContentHubFolder), FolderId },
            { typeof(ContentItemEditSection), ItemID },
            { typeof(VersionComparisonDialogPage), VersionComparisonDialogPage.BuildParameter(olderVersionId, newerVersionId) }
        });

    /// <summary>
    /// Builds the drop-down options as Xperience expects them: one "id;label" pair per line. The
    /// label carries what the listing shows in its columns, since a drop-down has only one.
    /// </summary>
    private async Task<string> GetVersionOptions()
    {
        int defaultLanguageId = await GetDefaultContentLanguageId();

        // Versions are stored per language variant, so the list is limited to a single language
        // to keep one entry per stored version.
        var versions = (await contentItemVersionInfoProvider
            .Get()
            .WhereEquals(nameof(ContentItemVersionInfo.ContentItemVersionContentItemID), ItemID)
            .WhereEquals(nameof(ContentItemVersionInfo.ContentItemVersionContentLanguageID), defaultLanguageId)
            .OrderByDescending(nameof(ContentItemVersionInfo.ContentItemVersionCreatedWhen))
            .Columns(
                nameof(ContentItemVersionInfo.ContentItemVersionID),
                nameof(ContentItemVersionInfo.ContentItemVersionCreatedWhen),
                nameof(ContentItemVersionInfo.ContentItemVersionCreatedByUserID),
                nameof(ContentItemVersionInfo.ContentItemVersionAction))
            .GetEnumerableTypedResultAsync())
            .ToList();

        var userNames = await GetUserNames(versions);
        var draft = await GetDraftMetadata(defaultLanguageId);
        var options = new StringBuilder();

        // Work in progress is what an editor most often wants to compare, so it leads the list.
        if (draft is not null)
        {
            options
                .Append(VersionComparisonDialogPage.DRAFT_VERSION_ID)
                .Append(OPTION_VALUE_SEPARATOR)
                .Append(BuildDraftLabel(draft, await GetUserName(draft.ContentItemLanguageMetadataModifiedByUserID)))
                .AppendLine();
        }

        for (int i = 0; i < versions.Count; i++)
        {
            options
                .Append(versions[i].ContentItemVersionID)
                .Append(OPTION_VALUE_SEPARATOR)
                // The newest stored version is what is live. It says so explicitly once there is
                // a draft above it to tell it apart from.
                .Append(BuildLabel(versions[i], userNames, isNewest: i == 0, hasDraft: draft is not null))
                .AppendLine();
        }

        return options.ToString();
    }

    private static string BuildDraftLabel(ContentItemLanguageMetadataInfo draft, string? userName)
    {
        var label = new StringBuilder(draft.ContentItemLanguageMetadataModifiedWhen.ToString("G", CultureInfo.CurrentCulture))
            .Append(" — ")
            .Append(VersionComparisonStrings.DRAFT_ACTION);

        if (!string.IsNullOrWhiteSpace(userName))
        {
            label.Append(' ').Append(VersionComparisonStrings.AUTHOR_PREFIX).Append(userName);
        }

        return label.Replace(OPTION_VALUE_SEPARATOR, ',').ToString();
    }

    private static string BuildLabel(ContentItemVersionInfo version, IReadOnlyDictionary<int, string> userNames, bool isNewest, bool hasDraft)
    {
        var label = new StringBuilder(version.ContentItemVersionCreatedWhen.ToString("G", CultureInfo.CurrentCulture))
            .Append(" — ")
            .Append(VersionActionNames.Describe(version.ContentItemVersionAction));

        if (userNames.TryGetValue(version.ContentItemVersionCreatedByUserID, out string? userName))
        {
            label.Append(' ').Append(VersionComparisonStrings.AUTHOR_PREFIX).Append(userName);
        }

        if (isNewest)
        {
            label.Append(hasDraft
                ? VersionComparisonStrings.CURRENT_PUBLISHED_SUFFIX
                : VersionComparisonStrings.CURRENT_SUFFIX);
        }

        // A semicolon in the label would be read as the end of the option's value.
        return label.Replace(OPTION_VALUE_SEPARATOR, ',').ToString();
    }

    /// <summary>
    /// The item's language metadata when it is carrying unpublished work, or null when what is
    /// published is all there is.
    /// </summary>
    /// <remarks>
    /// A draft is not a stored version, so the version history alone cannot say whether one
    /// exists. The item's latest version status can: it reads as published only while nothing has
    /// been edited since, and as a draft both for an edited item and for one never published.
    /// </remarks>
    private async Task<ContentItemLanguageMetadataInfo?> GetDraftMetadata(int contentLanguageId)
    {
        var metadata = (await contentItemLanguageMetadataInfoProvider
            .Get()
            .WhereEquals(nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentItemID), ItemID)
            .WhereEquals(nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentLanguageID), contentLanguageId)
            .TopN(1)
            .GetEnumerableTypedResultAsync())
            .FirstOrDefault();

        return metadata?.ContentItemLanguageMetadataLatestVersionStatus
            is VersionStatus.Draft or VersionStatus.InitialDraft
            ? metadata
            : null;
    }

    private async Task<string?> GetUserName(int userId)
    {
        if (userId == 0)
        {
            return null;
        }

        var users = await userInfoProvider
            .Get()
            .WhereEquals(nameof(UserInfo.UserID), userId)
            .Columns(nameof(UserInfo.UserName))
            .TopN(1)
            .GetEnumerableTypedResultAsync();

        return users.FirstOrDefault()?.UserName;
    }

    private async Task<IReadOnlyDictionary<int, string>> GetUserNames(IEnumerable<ContentItemVersionInfo> versions)
    {
        var userIds = versions
            .Select(version => version.ContentItemVersionCreatedByUserID)
            .Where(static id => id != 0)
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var users = await userInfoProvider
            .Get()
            .WhereIn(nameof(UserInfo.UserID), userIds)
            .Columns(nameof(UserInfo.UserID), nameof(UserInfo.UserName))
            .GetEnumerableTypedResultAsync();

        return users.ToDictionary(user => user.UserID, user => user.UserName);
    }

    private async Task<int> GetDefaultContentLanguageId()
    {
        var languages = await contentLanguageInfoProvider
            .Get()
            .WhereTrue(nameof(ContentLanguageInfo.ContentLanguageIsDefault))
            .Columns(nameof(ContentLanguageInfo.ContentLanguageID))
            .TopN(1)
            .GetEnumerableTypedResultAsync();

        return languages.FirstOrDefault()?.ContentLanguageID ?? 0;
    }
}
