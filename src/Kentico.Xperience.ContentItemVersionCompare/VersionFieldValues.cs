using System.Text.Json;

namespace Kentico.Xperience.ContentItemVersionCompare;

/// <summary>
/// Reads the serialized values of the field types that are not plain text: taxonomy selections,
/// content item references and content item assets are all stored as JSON.
/// </summary>
public static class VersionFieldValues
{
    /// <summary>
    /// Metadata of a content item asset needed to render its thumbnail.
    /// </summary>
    public sealed record AssetReference(string Name, string? Variant);

    /// <summary>
    /// Reads the identifiers of a taxonomy or content item reference value, which are both stored
    /// as an array of objects carrying an "Identifier" property.
    /// </summary>
    public static List<Guid> ParseIdentifiers(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(value);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. document.RootElement
                .EnumerateArray()
                .Select(element => element.TryGetProperty("Identifier", out var identifier)
                    && identifier.TryGetGuid(out var parsed) ? parsed : Guid.Empty)
                .Where(identifier => identifier != Guid.Empty)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads the file name and the preferred variant of a content item asset value.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="preferredVariant">
    /// Variant to use when the asset provides it. When it is not available the first generated
    /// variant is used, and when the asset has none the original file is linked.
    /// </param>
    public static AssetReference? ParseAsset(string? value, string? preferredVariant)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? name = document.RootElement.TryGetProperty("Name", out var nameElement)
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return new AssetReference(name, ReadVariant(document.RootElement, preferredVariant));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Indicates whether the file is one the browser can show as a thumbnail.
    /// </summary>
    public static bool IsImage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string extension = System.IO.Path.GetExtension(fileName);

        return extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg";
    }

    /// <summary>
    /// Prefers a generated variant over the original so that the comparison does not download
    /// full-size images.
    /// </summary>
    private static string? ReadVariant(JsonElement root, string? preferredVariant)
    {
        if (!root.TryGetProperty("Variants", out var variants) || variants.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var names = variants.EnumerateObject().Select(variant => variant.Name).ToList();

        return names.FirstOrDefault(name => string.Equals(name, preferredVariant, StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault();
    }
}
