using System.Text.Json.Nodes;
using FluentAssertions;
using JsonDocumentationGenerator;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// Guards the JSON documentation *pipeline* rather than one component's entry.
/// <para>
/// <see cref="GeneratedDocumentationMatchesCommittedTests"/> proves the committed bundles equal what
/// the generator emits — but the generator merges the overlays in, so a wrong sentence in an overlay
/// reproduces itself in the bundle and both sides agree on the wrong text. That is exactly how the
/// email packages shipped parameter descriptions shifted by one position: the overlays were produced by
/// an older parse that paired the XML-doc summaries in DECLARATION order against parameters emitted
/// ALPHABETICALLY, so <c>TmEmailImportDialog.OnClose</c> carried OnImport's sentence and
/// <c>OnImport</c> carried Show's. Nothing compared a description against its own source line.
/// </para>
/// <para>
/// Two guards close it: the validate pass must be clean on the committed set (missing overlays and
/// provably shifted descriptions are errors, and <c>--fail-on-drift</c> makes them fatal), and the shift
/// detector itself is pinned on synthetic input so a rewrite cannot quietly stop detecting.
/// </para>
/// </summary>
public sealed class DocumentationValidationTests
{
    private static string JsonDocumentationDir =>
        Path.Combine(ThemeCss.RepositoryRoot().FullName, "JsonDocumentation");

    [Fact]
    public void Validate_FailOnDrift_PassesOnTheCommittedDocumentationSet()
    {
        var originalError = Console.Error;
        var errors = new StringWriter();
        int exit;
        try
        {
            Console.SetError(errors);
            exit = PackageDocumentationGenerator.Run(
                [JsonDocumentationDir, "validate", "--fail-on-drift"]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        exit.Should().Be(0,
            "the committed documentation set must validate (every generated item has an overlay, no " +
            "shifted parameter description):\n{0}", errors.ToString());
    }

    [Fact]
    public void A_description_borrowed_from_a_sibling_parameter_is_reported_as_shifted()
    {
        var generated = Parse("""
            {
              "parameters": [
                { "name": "OnClose", "description": "Raised when the dialog is closed." },
                { "name": "OnImport", "description": "Raised with the imported document when the user confirms." },
                { "name": "Show", "description": "Whether the dialog is shown." }
              ]
            }
            """);
        var overlay = Parse("""
            {
              "documentationStatus": "generated",
              "parameters": [
                { "name": "Show", "description": "Whether the dialog is shown." },
                { "name": "OnClose", "description": "Raised with the imported document when the user confirms." },
                { "name": "OnImport", "description": "Whether the dialog is shown." }
              ]
            }
            """);

        var shifted = PackageDocumentationGenerator.FindShiftedNamedDescriptions(overlay, generated, "parameters");

        shifted.Should().BeEquivalentTo(["OnClose", "OnImport"]);
    }

    [Fact]
    public void A_hand_written_overlay_may_reuse_a_sentence_without_being_rewritten()
    {
        // A curated overlay is the editable source: two of its parameters may deliberately share a
        // sentence. Only machine-generated overlays are repaired, so a human's prose is never replaced.
        var generated = Parse("""
            {
              "parameters": [
                { "name": "Show", "description": "Whether the dialog is shown." },
                { "name": "ShowChanged", "description": "Raised when the dialog is shown or hidden." }
              ]
            }
            """);
        var overlay = Parse("""
            {
              "documentationStatus": "merged",
              "parameters": [
                { "name": "Show", "description": "Whether the dialog is shown." },
                { "name": "ShowChanged", "description": "Whether the dialog is shown." }
              ]
            }
            """);

        PackageDocumentationGenerator.FindShiftedNamedDescriptions(overlay, generated, "parameters")
            .Should().BeEmpty();
    }

    [Fact]
    public void A_shifted_description_with_a_dropped_cref_hole_is_reported()
    {
        // The old parse did not only pair the summaries by the wrong name — it also DELETED the text of
        // every <see cref="…"/> it could not render. So the overlay holds a truncated version of a
        // sibling's sentence ("…overrides rendering." instead of "…overrides Field rendering."), which
        // an exact match misses. A description that is the sibling's sentence with characters deleted
        // (or one that still shows a hole) is just as provably borrowed.
        var generated = Parse("""
            {
              "parameters": [
                { "name": "CellTemplate", "description": "Optional custom cell template. When set, overrides Field rendering." },
                { "name": "Editable", "description": "Whether this column supports inline editing." },
                { "name": "Format", "description": "Display format for the cell value." }
              ]
            }
            """);
        var overlay = Parse("""
            {
              "documentationStatus": "generated",
              "parameters": [
                { "name": "CellTemplate", "description": "Optional custom cell template. When set, overrides Field rendering." },
                { "name": "Editable", "description": "Optional custom cell template. When set, overrides rendering." },
                { "name": "Format", "description": "Display format for the cell value." }
              ]
            }
            """);

        PackageDocumentationGenerator.FindShiftedNamedDescriptions(overlay, generated, "parameters")
            .Should().BeEquivalentTo(["Editable"]);
    }

    [Fact]
    public void A_description_that_matches_its_own_source_is_not_shifted()
    {
        var generated = Parse("""
            {
              "parameters": [
                { "name": "Show", "description": "Whether the dialog is shown." },
                { "name": "ShowChanged", "description": "Raised when the dialog is shown or hidden." }
              ]
            }
            """);
        var overlay = Parse("""
            {
              "documentationStatus": "generated",
              "parameters": [
                { "name": "Show", "description": "Whether the dialog is shown." },
                { "name": "ShowChanged", "description": "Raised when the dialog is shown or hidden." }
              ]
            }
            """);

        PackageDocumentationGenerator.FindShiftedNamedDescriptions(overlay, generated, "parameters")
            .Should().BeEmpty();
    }

    [Fact]
    public void A_shifted_description_is_repaired_from_the_parse()
    {
        var generated = Parse("""
            {
              "parameters": [
                { "name": "OnClose", "description": "Raised when the dialog is closed." },
                { "name": "Show", "description": "Whether the dialog is shown." }
              ]
            }
            """);
        var overlay = Parse("""
            {
              "documentationStatus": "generated",
              "parameters": [
                { "name": "Show", "description": "Raised when the dialog is closed." },
                { "name": "OnClose", "description": "Whether the dialog is shown." }
              ]
            }
            """);

        var repaired = PackageDocumentationGenerator.RepairShiftedNamedDescriptions(overlay, generated, "parameters");

        repaired.Should().Be(2);
        overlay["parameters"]!.AsArray()[0]!["description"]!.GetValue<string>()
            .Should().Be("Whether the dialog is shown.");
        overlay["parameters"]!.AsArray()[1]!["description"]!.GetValue<string>()
            .Should().Be("Raised when the dialog is closed.");
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
