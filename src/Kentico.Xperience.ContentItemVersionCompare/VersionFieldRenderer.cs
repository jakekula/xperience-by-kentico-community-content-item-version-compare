using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.DataEngine;
using CMS.FormEngine;

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Renders one field of a content item as the pair of values the comparison shows side by side.
/// </summary>
/// <remarks>
/// How a value is presented depends on what it holds. Taxonomy terms and linked items are stored
/// as identifiers and have to be resolved before they mean anything to an editor; text is diffed
/// word by word so edits show up inside the value rather than as two blocks to read in full.
/// </remarks>
public sealed class VersionFieldRenderer
{
    private readonly ITaxonomyRetriever taxonomyRetriever;
    private readonly IContentItemAssetUrlProvider assetUrlProvider;
    private readonly IInfoProvider<ContentItemInfo> contentItemInfoProvider;
    private readonly IInfoProvider<ContentItemLanguageMetadataInfo> contentItemLanguageMetadataInfoProvider;
    private readonly VersionComparisonOptions options;

    public VersionFieldRenderer(
        ITaxonomyRetriever taxonomyRetriever,
        IContentItemAssetUrlProvider assetUrlProvider,
        IInfoProvider<ContentItemInfo> contentItemInfoProvider,
        IInfoProvider<ContentItemLanguageMetadataInfo> contentItemLanguageMetadataInfoProvider,
        VersionComparisonOptions options)
    {
        this.taxonomyRetriever = taxonomyRetriever;
        this.assetUrlProvider = assetUrlProvider;
        this.contentItemInfoProvider = contentItemInfoProvider;
        this.contentItemLanguageMetadataInfoProvider = contentItemLanguageMetadataInfoProvider;
        this.options = options;
    }

    /// <summary>
    /// Picks how a field is presented. Taxonomy terms become bubbles and assets become thumbnails;
    /// everything else is diffed as text so the edits show up inside the value.
    /// </summary>
    public async Task<(string Older, string Newer)> RenderField(
        FormFieldInfo field, string olderValue, string newerValue, ContentItemInfo item, string languageName, int languageId)
    {
        switch (field.DataType)
        {
            case FieldDataType.Taxonomy:
                return await RenderTaxonomy(olderValue, newerValue, languageName);

            case FieldDataType.ContentItemAsset:
                return RenderAsset(field, olderValue, newerValue, item, languageName);

            case FieldDataType.ContentItemReference:
                return await RenderReferences(olderValue, newerValue, languageName, languageId);

            default:
                bool isHtml = VersionRichText.IsHtml(olderValue) || VersionRichText.IsHtml(newerValue);

                if (!isHtml)
                {
                    return VersionFieldDiff.Render(olderValue, newerValue, isHtml: false, options.MaxDiffTokens);
                }

                // Both sides go through the same rewrites, so matching markup still diffs as
                // unchanged.
                var (older, newer) = VersionFieldDiff.Render(
                    VersionRichText.PrepareRichText(olderValue),
                    VersionRichText.PrepareRichText(newerValue),
                    isHtml: true,
                    options.MaxDiffTokens);

                return (VersionRichText.ContainFloats(older), VersionRichText.ContainFloats(newer));
        }
    }

    private async Task<(string Older, string Newer)> RenderTaxonomy(string olderValue, string newerValue, string languageName)
    {
        var olderIds = VersionFieldValues.ParseIdentifiers(olderValue);
        var newerIds = VersionFieldValues.ParseIdentifiers(newerValue);
        var allIds = olderIds.Union(newerIds).ToList();

        if (allIds.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var tags = await taxonomyRetriever.RetrieveTags(allIds, languageName);
        var titles = tags.ToDictionary(
            tag => tag.Identifier,
            tag => string.IsNullOrWhiteSpace(tag.Title) ? tag.Name : tag.Title);

        string Side(List<Guid> ids, List<Guid> other, VersionFieldMarkup.State changed) =>
            VersionFieldMarkup.Wrap(ids.Select(id => VersionFieldMarkup.Bubble(
                titles.TryGetValue(id, out string? title) ? title : id.ToString(),
                other.Contains(id) ? VersionFieldMarkup.State.Unchanged : changed)));

        return (
            Side(olderIds, newerIds, VersionFieldMarkup.State.Removed),
            Side(newerIds, olderIds, VersionFieldMarkup.State.Added));
    }

    private (string Older, string Newer) RenderAsset(
        FormFieldInfo field, string olderValue, string newerValue, ContentItemInfo item, string languageName)
    {
        var older = VersionFieldValues.ParseAsset(olderValue, options.ThumbnailVariantName);
        var newer = VersionFieldValues.ParseAsset(newerValue, options.ThumbnailVariantName);
        bool sameFile = string.Equals(older?.Name, newer?.Name, StringComparison.OrdinalIgnoreCase);

        string Side(VersionFieldValues.AssetReference? asset, VersionFieldMarkup.State changed)
        {
            if (asset is null)
            {
                return string.Empty;
            }

            var state = sameFile ? VersionFieldMarkup.State.Unchanged : changed;
            string? url = BuildAssetUrl(item.ContentItemGUID, field.Guid, asset, languageName);

            return url is null || !VersionFieldValues.IsImage(asset.Name)
                ? VersionFieldMarkup.Bubble(asset.Name, state)
                : VersionFieldMarkup.Wrap([VersionFieldMarkup.Thumbnail(url, asset.Name, state)]);
        }

        return (Side(older, VersionFieldMarkup.State.Removed), Side(newer, VersionFieldMarkup.State.Added));
    }

    /// <summary>
    /// Renders referenced content items by name and content type, showing a thumbnail for those
    /// whose content type carries an asset.
    /// </summary>
    private async Task<(string Older, string Newer)> RenderReferences(
        string olderValue, string newerValue, string languageName, int languageId)
    {
        var olderIds = VersionFieldValues.ParseIdentifiers(olderValue);
        var newerIds = VersionFieldValues.ParseIdentifiers(newerValue);
        var allIds = olderIds.Union(newerIds).ToList();

        if (allIds.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        var referenced = await GetReferencedItems(allIds, languageId);

        // Each referenced content type is resolved once - building a FormInfo parses the class
        // form definition, which is far too expensive to repeat for every referenced item.
        var assetFields = referenced.Values
            .Select(reference => reference.ContentTypeId)
            .Distinct()
            .ToDictionary(contentTypeId => contentTypeId, GetAssetField);

        string Side(List<Guid> ids, List<Guid> other, VersionFieldMarkup.State changed) =>
            VersionFieldMarkup.Wrap(ids.Select(id =>
            {
                var state = other.Contains(id) ? VersionFieldMarkup.State.Unchanged : changed;

                if (!referenced.TryGetValue(id, out var reference))
                {
                    return VersionFieldMarkup.ReferenceChip(id.ToString(), VersionComparisonStrings.MISSING_REFERENCE, state);
                }

                var asset = assetFields.GetValueOrDefault(reference.ContentTypeId);
                string? url = asset is null
                    ? null
                    : BuildAssetUrl(
                        id,
                        asset.Guid,
                        new VersionFieldValues.AssetReference(reference.Name, options.ThumbnailVariantName),
                        languageName);

                return url is null
                    ? VersionFieldMarkup.ReferenceChip(reference.Name, reference.ContentTypeName, state)
                    : VersionFieldMarkup.Thumbnail(url, reference.Name, state, reference.ContentTypeName);
            }));

        return (
            Side(olderIds, newerIds, VersionFieldMarkup.State.Removed),
            Side(newerIds, olderIds, VersionFieldMarkup.State.Added));
    }

    /// <summary>
    /// Resolves linked items to the name editors see and the display name of their content type,
    /// so that the comparison never falls back to showing a raw identifier.
    /// </summary>
    private async Task<Dictionary<Guid, ReferencedItem>> GetReferencedItems(List<Guid> identifiers, int languageId)
    {
        var items = await contentItemInfoProvider
            .Get()
            .WhereIn(nameof(ContentItemInfo.ContentItemGUID), identifiers)
            .Columns(
                nameof(ContentItemInfo.ContentItemID),
                nameof(ContentItemInfo.ContentItemGUID),
                nameof(ContentItemInfo.ContentItemName),
                nameof(ContentItemInfo.ContentItemContentTypeID))
            .GetEnumerableTypedResultAsync();

        var list = items.ToList();

        if (list.Count == 0)
        {
            return [];
        }

        var metadata = await contentItemLanguageMetadataInfoProvider
            .Get()
            .WhereIn(
                nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentItemID),
                list.Select(item => item.ContentItemID).ToList())
            .WhereEquals(
                nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentLanguageID),
                languageId)
            .Columns(
                nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataContentItemID),
                nameof(ContentItemLanguageMetadataInfo.ContentItemLanguageMetadataDisplayName))
            .GetEnumerableTypedResultAsync();

        var displayNames = metadata
            .GroupBy(entry => entry.ContentItemLanguageMetadataContentItemID)
            .ToDictionary(group => group.Key, group => group.First().ContentItemLanguageMetadataDisplayName);

        var contentTypeNames = list
            .Select(item => item.ContentItemContentTypeID)
            .Distinct()
            .ToDictionary(
                contentTypeId => contentTypeId,
                contentTypeId => DataClassInfoProvider.GetDataClassInfo(contentTypeId)?.ClassDisplayName ?? string.Empty);

        return list.ToDictionary(
            item => item.ContentItemGUID,
            item => new ReferencedItem(
                displayNames.TryGetValue(item.ContentItemID, out string? displayName) && !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : item.ContentItemName,
                item.ContentItemContentTypeID,
                contentTypeNames.GetValueOrDefault(item.ContentItemContentTypeID, string.Empty)));
    }

    private sealed record ReferencedItem(string Name, int ContentTypeId, string ContentTypeName);

    private static FormFieldInfo? GetAssetField(int contentTypeId)
    {
        var contentType = DataClassInfoProvider.GetDataClassInfo(contentTypeId);

        if (contentType is null)
        {
            return null;
        }

        return new FormInfo(contentType.ClassFormDefinition)
            .GetFields(true, true)
            .FirstOrDefault(field => field.DataType == FieldDataType.ContentItemAsset);
    }

    private string? BuildAssetUrl(Guid contentItemGuid, Guid fieldGuid, VersionFieldValues.AssetReference asset, string languageName)
    {
        // Assets are not versioned, so the thumbnail always shows the file as it is stored now.
        var url = string.IsNullOrEmpty(asset.Variant)
            ? assetUrlProvider.Get(contentItemGuid, fieldGuid, languageName, asset.Name)
            : assetUrlProvider.GetForVariant(contentItemGuid, fieldGuid, asset.Variant, languageName, asset.Name);

        return url is null ? null : $"/{url.RelativePath}{url.QueryString}";
    }
}
