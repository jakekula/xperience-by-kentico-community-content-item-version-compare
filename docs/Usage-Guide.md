# Usage Guide

## How it works

The library adds three things to the administration:

1. A **Compare versions** entry in a content item's action menu, next to the system's own *Show
   version history*.
2. A side panel that picks the two versions to compare.
3. A dialog showing the two versions side by side, one row of cards per field.

Nothing needs registering in `Program.cs`. The pages register themselves through assembly
attributes, the same way Xperience registers its own.

## Choosing versions

The panel lists, in order:

- **The current draft**, when the item has unpublished work. A draft is not a stored version, so it
  is read from the item's live data and run through the same serializer Xperience uses when it
  stores a version at publish time.
- **The current published version**, labelled as such whenever a draft sits above it.
- **The stored history**, newest first.

Picking the two the other way round is fine. The pair is always ordered by time, so the older
version is shown on the left.

## Reading the comparison

Each field is one row, with the older version on the left:

| Highlight | Meaning |
| --- | --- |
| Green | Added |
| Red, struck through | Removed |
| Orange | Replaced |

An image that was added, removed or replaced is outlined in the same colours, because a background
highlight behind an image is hidden by the image itself.

Fields that are empty in both versions are skipped, so a content type with many optional fields
does not produce rows of empty cards.

## Configuration

Everything has a working default. To change one, in `Program.cs`:

```csharp
builder.Services.Configure<VersionComparisonOptions>(options =>
{
    // Image variant used for thumbnails. Must be one your project generates.
    // Set to null to link the original file instead.
    options.ThumbnailVariantName = "Small";

    // Words compared per side of a field. Cost grows with the square of this number;
    // larger fields are rendered without inline highlighting.
    options.MaxDiffTokens = 800;
});
```

### Permissions

The pages are registered under the system's content item editing section, so they are governed by
the Content hub permissions of the item itself. Anyone who can open an item can compare its
versions.

### Wording and colours

Wording lives in `VersionComparisonStrings`, and the highlight colours are constants at the top of
`VersionFieldDiff` (text and image highlights) and `VersionFieldMarkup` (tags and thumbnails). The
legend shown above a comparison is generated from the same constants, so the two cannot drift
apart. Both are internal to the library; change them in a fork rather than from a consuming
project.

## Limitations

- **It depends on internal Xperience APIs.** Types under `CMS.ContentEngine.Internal`, plus two
  values read by reflection: the dropdown of a menu action, whose properties are init-only, and the
  folder an item was opened from, which the system keeps internal. These carry no
  backward-compatibility guarantee, so re-test after every Xperience upgrade.
- **Rich text is rendered unsanitised**, so that it displays as formatted content rather than
  escaped tags. The exposure matches what the rich text editor already allows. If your project
  accepts rich text from a less trusted source, strip scripts in `VersionFieldRenderer.RenderField`
  before rendering.
- **Only the default content language** is compared, whichever language variant you are editing.
- **Assets are not versioned by Xperience.** Uploaded files live outside the version history, so a
  thumbnail always shows the file as it exists now. Only the asset's metadata is versioned and
  diffed.
- *Current published* is the newest stored version, relabelled. If versioning was enabled after the
  item was last published there is no stored row for the live content, and no such entry appears.
- An item that has never been published offers only its draft, and a comparison needs two versions.

## Notes for contributors

Three things about the admin UI cost time to discover and are easy to undo by accident:

- **The menu entry follows *Show version history*, not the main button.** Which action is primary
  depends on the item's workflow step, and *Create new version* only exists once an item has been
  published. Anchoring there hides the entry on drafts.
- **The picker is a side panel and the comparison is a dialog, for the same reason.** A surface
  takes its size from the client template rendered inside it. The edit template pins a dialog to a
  minimum of `65vw` but sizes its side panel to 600px; the listing template sizes neither, which
  leaves a listing in a dialog as a small scrolling box.
- **The comparison is registered beside the picker, not beneath it.** The edit template carries no
  `RoutingContentPlaceholder` and cannot render child routes, so a page nested under the picker
  changes the URL and then mounts nothing.
