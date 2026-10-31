using System.Reflection;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.UIPages;
using Kentico.Xperience.ContentItemVersionCompare;

[assembly: PageExtender(typeof(ContentItemEditCompareVersionsExtender))]

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Adds a <em>Compare versions</em> entry to the action menu of a content item in the Content hub,
/// next to the system's own <em>Show version history</em>. Selecting it opens
/// <see cref="VersionSelectionPanelPage"/>.
/// </summary>
public class ContentItemEditCompareVersionsExtender : PageExtender<ContentItemEdit>
{
    /// <summary>
    /// Anchor for the new entry. Anything with a version history has versions to compare, so
    /// following this action keeps the two together in every workflow step. Anchoring on the
    /// <em>Create new version</em> button instead would hide the entry on unpublished items.
    /// </summary>
    /// <remarks>
    /// Duplicated from the internal <c>ContentItemActionNames</c>, which project code cannot
    /// reference.
    /// </remarks>
    private const string VERSIONS_ACTION = "Versions";

    /// <summary>
    /// The menu is built from init-only types, so an existing one can only be extended through
    /// these setters. Rebuilding it would mean copying every property of the action and of
    /// <see cref="ContentItemState"/> by hand, silently dropping any the next Xperience version
    /// adds, including the version timestamp that guards concurrent saves.
    /// </summary>
    private static readonly PropertyInfo? actionsProperty = typeof(ContentItemAction).GetProperty(nameof(ContentItemAction.Actions));
    private static readonly PropertyInfo? menuActionsProperty = typeof(ContentItemState).GetProperty(nameof(ContentItemState.MenuActions));

    /// <summary>
    /// The folder segment the panel's URL has to repeat. The system binds it on the extended page
    /// but keeps it internal. It is read rather than derived from the item, because an item opened
    /// through a smart folder would otherwise return the editor to a different folder.
    /// </summary>
    private static readonly PropertyInfo? folderIdProperty = typeof(ContentItemEdit)
        .GetProperty("FolderId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly IPageLinkGenerator pageLinkGenerator;

    public ContentItemEditCompareVersionsExtender(IPageLinkGenerator pageLinkGenerator) =>
        this.pageLinkGenerator = pageLinkGenerator;

    public override Task<TemplateClientProperties> ConfigureTemplateProperties(TemplateClientProperties properties)
    {
        if (properties is ContentItemClientProperties contentItemProperties)
        {
            AddCompareVersionsAction(contentItemProperties.State);
        }

        return base.ConfigureTemplateProperties(properties);
    }

    /// <summary>
    /// Opens the version selection panel for the edited content item.
    /// </summary>
    [PageCommand]
    public Task<ICommandResponse> CompareVersions()
    {
        // The generator needs a value for every parameterized page above its target; it does not
        // fall back to the current route. Each value is what that page binds from its own URL
        // segment, since the generator has no binder and writes segments through ToString.
        string path = pageLinkGenerator.GetPath<VersionSelectionPanelPage>(new PageParameterValues
        {
            { typeof(ContentHubWorkspace), Page.WorkspaceUrlIdentifier.WorkspaceID },
            { typeof(ContentHubContentLanguage), Page.ContentLanguageIdentifier.LanguageName },
            { typeof(ContentHubFolder), GetFolderId() },
            { typeof(ContentItemEditSection), Page.ItemID }
        });

        return Task.FromResult<ICommandResponse>(NavigateTo(path, false));
    }

    private ContentFolderId GetFolderId() =>
        folderIdProperty?.GetValue(Page) is ContentFolderId folderId ? folderId : ContentFolderId.AllContentItems;

    /// <summary>
    /// Places the entry directly after the system's <em>Show version history</em>, wherever that
    /// sits. Leaves the menu untouched when that action is absent: an item with no version history
    /// has nothing to compare.
    /// </summary>
    private static void AddCompareVersionsAction(ContentItemState state)
    {
        var menuActions = state?.MenuActions;

        if (menuActions is null || actionsProperty?.SetMethod is null || menuActionsProperty?.SetMethod is null)
        {
            return;
        }

        // Version history sits either in the menu itself or inside a button's dropdown,
        // depending on the workflow step, so both are searched.
        if (Contains(menuActions, VERSIONS_ACTION))
        {
            menuActionsProperty.SetValue(state, InsertAfterVersionHistory(menuActions));

            return;
        }

        foreach (var action in menuActions.OfType<ContentItemAction>())
        {
            if (action.Actions is not null && Contains(action.Actions, VERSIONS_ACTION))
            {
                actionsProperty.SetValue(action, InsertAfterVersionHistory(action.Actions));

                return;
            }
        }
    }

    private static bool Contains(IEnumerable<IContentItemAction> actions, string name) =>
        actions.OfType<ContentItemAction>().Any(action => action.Name == name);

    /// <summary>
    /// Returns the collection with the entry inserted after the version history action, matching
    /// how that action is presented so the two read as a pair.
    /// </summary>
    private static IReadOnlyCollection<IContentItemAction> InsertAfterVersionHistory(IReadOnlyCollection<IContentItemAction> actions)
    {
        // Guards against a double insert if the same action instance is ever rendered twice.
        if (Contains(actions, nameof(CompareVersions)))
        {
            return actions;
        }

        var extended = new List<IContentItemAction>(actions.Count + 1);

        foreach (var action in actions)
        {
            extended.Add(action);

            if (action is ContentItemAction versionHistory && versionHistory.Name == VERSIONS_ACTION)
            {
                extended.Add(new ContentItemAction
                {
                    Name = nameof(CompareVersions),
                    Label = VersionComparisonStrings.COMPARE_VERSIONS_ACTION,
                    IsDropdown = versionHistory.IsDropdown
                });
            }
        }

        return extended;
    }
}
