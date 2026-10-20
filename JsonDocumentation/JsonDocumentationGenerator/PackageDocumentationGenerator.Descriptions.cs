using System.Text.Json.Nodes;

namespace JsonDocumentationGenerator;

/// <summary>
/// Description-drift detection for the JSON documentation overlays.
/// <para>
/// The committed bundles are merged from two sources: the overlay (editable, the prose that ships) and
/// the source parse (parameter names, types, defaults — and a default description). Because the overlay
/// wins for prose, a wrong sentence in an overlay reproduces itself in the bundle, and
/// <c>GeneratedDocumentationMatchesCommittedTests</c> — which compares the bundle against a fresh run of
/// the same merge — happily agrees with it.
/// </para>
/// <para>
/// That is how the email packages shipped parameter descriptions shifted by one position: the overlays
/// were produced by an older parse that paired the XML-doc summaries in DECLARATION order against
/// parameters emitted ALPHABETICALLY, so <c>TmEmailImportDialog.OnClose</c> carried OnImport's sentence,
/// <c>OnImport</c> carried Show's, and <c>TmEmailTemplateEditor.TimeProvider</c> carried OnAutoSave's.
/// The shift is provable without guessing: an entry whose description is another entry's
/// source-derived description — while differing from its own — borrowed it. The same parse also DELETED
/// the text of every <c>&lt;see cref="…"/&gt;</c> it could not render, so the borrowed sentence is often a
/// hole-punched copy of the sibling's ("…overrides rendering." instead of "…overrides Field rendering.");
/// a description that is the sibling's sentence with characters deleted is just as provable. Curated
/// overlays are left alone (a human may deliberately reuse a sentence), so the repair only touches
/// overlays the generator itself produced (<c>"documentationStatus": "generated"</c>).
/// </para>
/// Kept in its own partial file because <c>PackageDocumentationGenerator.cs</c> already sits in the
/// frozen over-1200-line set and must not grow new bulk.
/// </summary>
internal static partial class PackageDocumentationGenerator
{
    private static readonly string[] NamedDescriptionArrays = ["parameters", "members"];

    /// <summary>
    /// The names in <paramref name="arrayName"/> whose overlay description was borrowed from a sibling
    /// parameter/member of the same item. Empty for a curated overlay, for an unknown array and for
    /// entries that already carry their own source description.
    /// </summary>
    internal static IReadOnlyList<string> FindShiftedNamedDescriptions(
        JsonObject overlay, JsonObject generated, string arrayName)
        => [.. FindShifted(overlay, generated, arrayName).Select(entry => entry.Name)];

    /// <summary>
    /// Rewrites every provably shifted description in place and returns how many entries changed.
    /// The replacement is the freshly parsed description of the same name — the shift is a pairing bug,
    /// never a documentation gap, so the source text is always the right answer.
    /// </summary>
    internal static int RepairShiftedNamedDescriptions(
        JsonObject overlay, JsonObject generated, string arrayName)
    {
        var shifted = FindShifted(overlay, generated, arrayName);
        foreach (var entry in shifted)
        {
            entry.Entry["description"] = entry.FreshDescription;
        }

        return shifted.Count;
    }

    /// <summary>
    /// The validate-side counterpart: one message per shifted description in the whole documentation set,
    /// so <c>validate --fail-on-drift</c> fails instead of publishing prose the source never said.
    /// </summary>
    internal static IReadOnlyList<string> ShiftedDescriptionErrors(
        IReadOnlyList<PackageDocumentationResult> results, bool enabled)
    {
        if (!enabled)
        {
            return [];
        }

        var errors = new List<string>();
        foreach (var result in results)
        {
            foreach (var pair in result.OverlayPairs)
            {
                foreach (var arrayName in NamedDescriptionArrays)
                {
                    foreach (var name in FindShiftedNamedDescriptions(pair.Overlay, pair.Generated, arrayName))
                    {
                        errors.Add(
                            $"{result.Config.PackageId}: {pair.Overlay.GetString("itemName")} {arrayName} " +
                            $"'{name}' carries a sibling's description — the overlay is shifted; run " +
                            "`JsonDocumentationGenerator repair` and commit the overlay with the bundles.");
                    }
                }
            }
        }

        return errors;
    }

    private static List<(string Name, string FreshDescription, JsonObject Entry)> FindShifted(
        JsonObject overlay, JsonObject generated, string arrayName)
    {
        var shifted = new List<(string Name, string FreshDescription, JsonObject Entry)>();
        if (overlay[arrayName] is not JsonArray overlayEntries
            || generated[arrayName] is not JsonArray generatedEntries)
        {
            return shifted;
        }

        // Never rewrite a curated overlay: its prose is the editable source, and two of its entries may
        // legitimately share a sentence the source words differently.
        if (!string.Equals(overlay.GetString("documentationStatus"), "generated", StringComparison.OrdinalIgnoreCase))
        {
            return shifted;
        }

        var sourceDescriptions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in generatedEntries.OfType<JsonObject>())
        {
            var name = entry.GetString("name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                sourceDescriptions.TryAdd(name, entry.GetString("description"));
            }
        }

        foreach (var entry in overlayEntries.OfType<JsonObject>())
        {
            var name = entry.GetString("name");
            var current = entry.GetString("description");
            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(current)
                || !sourceDescriptions.TryGetValue(name, out var own)
                || string.IsNullOrWhiteSpace(own)
                || string.Equals(current, own, StringComparison.Ordinal))
            {
                continue;
            }

            // The old parse paired the summaries with the wrong name AND deleted the text of every
            // <see cref="…"/> it could not render, so the borrowed sentence survives as a truncated
            // copy of the sibling's ("…overrides rendering." for "…overrides Field rendering."). Both
            // the exact copy and the hole-punched copy are provable; the entry's own description is
            // not, which is what keeps a legitimately similar sentence out of the repair.
            if (sourceDescriptions.Any(pair =>
                    !string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(pair.Value)
                    && (string.Equals(pair.Value, current, StringComparison.Ordinal)
                        || IsStaleGeneratedDescription(current, pair.Value!))
                    && !IsStaleGeneratedDescription(current, own)))
            {
                shifted.Add((name, own, entry));
            }
        }

        return shifted;
    }
}
