using System.Text.Json;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Tests.Localization;

public class TmDocumentEditorLocalizationTests : LocalizationTestBase
{
    [Fact]
    public void DocumentEditorKeys_UsedByComponents_ExistInResourcesAndMockLocalizer()
    {
        var root = FindRepositoryRoot();
        var usedKeys = ReadUsedDocumentEditorKeys(root);

        usedKeys.Should().NotBeEmpty();
        ReadJsonKeys(root, "src/Tempo.Blazor/Resources/TmResources.json").Should().Contain(usedKeys);
        ReadJsonKeys(root, "src/Tempo.Blazor/Resources/TmResources.cs.json").Should().Contain(usedKeys);
        ReadJsonKeys(root, "src/Tempo.Blazor/Resources/TmResources.fr.json").Should().Contain(usedKeys);
        ReadMockLocalizerKeys(root).Should().Contain(usedKeys);
    }

    [Fact]
    public void Toolbar_UsesLocalizedLabelsInsteadOfHardcodedEnglishFallbacks()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmDocumentEditor_ToolbarLabel"] = "LOC toolbar",
            ["TmDocumentEditor_ToolbarTabsLabel"] = "LOC tabs",
            ["TmDocumentEditor_TabHome"] = "LOC home",
            ["TmDocumentEditor_TabInsert"] = "LOC insert tab",
            ["TmDocumentEditor_TabLayout"] = "LOC layout",
            ["TmDocumentEditor_TabReferences"] = "LOC refs",
            ["TmDocumentEditor_TabReview"] = "LOC review",
            ["TmDocumentEditor_TabView"] = "LOC view",
            ["TmDocumentEditor_GroupClipboard"] = "LOC clipboard",
            ["TmDocumentEditor_GroupFormatting"] = "LOC formatting",
            ["TmDocumentEditor_GroupInsert"] = "LOC insert",
            ["TmDocumentEditor_GroupLayout"] = "LOC page layout group",
            ["TmDocumentEditor_GroupReferences"] = "LOC references",
            ["TmDocumentEditor_GroupReview"] = "LOC review group",
            ["TmDocumentEditor_Save"] = "LOC save",
            ["TmDocumentEditor_Undo"] = "LOC undo",
            ["TmDocumentEditor_Redo"] = "LOC redo",
            ["TmDocumentEditor_Bold"] = "LOC bold",
            ["TmDocumentEditor_Italic"] = "LOC italic",
            ["TmDocumentEditor_Underline"] = "LOC underline",
            ["TmDocumentEditor_FontFamily"] = "LOC font",
            ["TmDocumentEditor_FontFamilyDefault"] = "LOC default font",
            ["TmDocumentEditor_FontSize"] = "LOC size",
            ["TmDocumentEditor_FontColor"] = "LOC font color",
            ["TmDocumentEditor_HighlightColor"] = "LOC highlight",
            ["TmDocumentEditor_GroupParagraph"] = "LOC paragraph",
            ["TmDocumentEditor_AlignLeft"] = "LOC left",
            ["TmDocumentEditor_AlignCenter"] = "LOC center",
            ["TmDocumentEditor_AlignRight"] = "LOC right",
            ["TmDocumentEditor_AlignJustify"] = "LOC justify",
            ["TmDocumentEditor_LineSpacing"] = "LOC line spacing",
            ["TmDocumentEditor_SpacingBefore"] = "LOC before",
            ["TmDocumentEditor_SpacingAfter"] = "LOC after",
            ["TmDocumentEditor_DecreaseIndent"] = "LOC decrease indent",
            ["TmDocumentEditor_IncreaseIndent"] = "LOC increase indent",
            ["TmDocumentEditor_Link"] = "LOC link",
            ["TmDocumentEditor_ClearFormatting"] = "LOC clear formatting",
            ["TmDocumentEditor_Insert"] = "LOC insert command",
            ["TmDocumentEditor_InsertImage"] = "LOC image",
            ["TmDocumentEditor_PageLayout"] = "LOC page layout",
            ["TmDocumentEditor_InsertFootnote"] = "LOC footnote",
            ["TmDocumentEditor_ExportPdf"] = "LOC PDF",
            ["TmDocumentEditor_TrackChanges"] = "LOC track changes",
            ["TmDocumentEditor_AddComment"] = "LOC comment",
            ["TmDocumentEditor_TemplatePreview"] = "LOC preview"
        });

        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.CanExportPdf, true)
            .Add(p => p.CanPreviewTemplate, true));

        var text = cut.Markup;
        text.Should().Contain("LOC home");
        text.Should().Contain("LOC save");
        text.Should().NotContain(">Home<");
        text.Should().NotContain(">Save<");

        cut.Find("[data-testid='document-ribbon-tab-references']").Click();
        cut.Markup.Should().Contain("LOC PDF");
        cut.Markup.Should().NotContain(">Export PDF<");
    }

    [Fact]
    public void EmptyAndErrorStates_UseLocalization()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmDocumentEditor_ProviderMissingTitle"] = "LOC provider title",
            ["TmDocumentEditor_ProviderMissingMessage"] = "LOC provider message",
            ["TmDocumentEditor_LoadErrorTitle"] = "LOC load title",
            ["TmDocumentEditor_LoadErrorMessage"] = "LOC load message",
            ["TmDocumentEditor_Retry"] = "LOC retry",
            ["TmDocumentEditor_DocumentSurfaceLabel"] = "LOC surface",
            ["TmDocumentEditor_PageLabel"] = "LOC page",
            ["TmDocumentEditor_UntitledDocument"] = "LOC untitled",
            ["TmDocumentEditor_StatusLoaded"] = "LOC loaded",
            ["TmDocumentEditor_EmptyDocument"] = "LOC empty"
        });

        var missingProvider = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-1"));
        missingProvider.Markup.Should().Contain("LOC provider title");
        missingProvider.Markup.Should().Contain("LOC provider message");

        var emptyProvider = new InMemoryDocumentEditorProvider();
        emptyProvider.SeedEmptyDocument("doc-empty");
        var empty = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-empty")
            .Add(p => p.Provider, emptyProvider)
            .Add(p => p.ShowToolbar, false)
            .Add(p => p.ShowComments, false)
            .Add(p => p.ShowVersionHistory, false));
        empty.WaitForAssertion(() => empty.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        empty.Markup.Should().Contain("LOC page");
        // DE-1.1: the in-surface page header ("Loaded" status) is gone; the region keeps its localized label.
        empty.Markup.Should().Contain("LOC surface");
        empty.Markup.Should().NotContain("LOC loaded");
        empty.Markup.Should().NotContain("This document is empty");

        var failed = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-fail")
            .Add(p => p.Provider, new ThrowingDocumentEditorProvider())
            .Add(p => p.ShowToolbar, false)
            .Add(p => p.ShowComments, false)
            .Add(p => p.ShowVersionHistory, false));
        failed.WaitForAssertion(() => failed.Markup.Should().Contain("LOC load title"));
        failed.Markup.Should().Contain("LOC load message");
        failed.Markup.Should().Contain("LOC retry");
        failed.Markup.Should().NotContain("Failed to load document");
    }

    private static SortedSet<string> ReadUsedDocumentEditorKeys(string root)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var componentRoot = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components", "DocumentEditor");
        foreach (var file in Directory.EnumerateFiles(componentRoot, "*.*", SearchOption.AllDirectories)
                     .Where(file => file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                         || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "Loc\\[\"(TmDocumentEditor_[A-Za-z0-9_]+)\""))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    private static SortedSet<string> ReadJsonKeys(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var keys = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => name.StartsWith("TmDocumentEditor_", StringComparison.Ordinal));

        return new SortedSet<string>(keys, StringComparer.Ordinal);
    }

    private static SortedSet<string> ReadMockLocalizerKeys(string root)
    {
        var source = File.ReadAllText(Path.Combine(root, "tests", "Tempo.Blazor.Tests", "Localization", "LocalizationTestBase.cs"));
        var keys = Regex.Matches(source, "\\[\"(TmDocumentEditor_[^\"]+)\"\\]")
            .Select(match => match.Groups[1].Value);

        return new SortedSet<string>(keys, StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Tempo.Blazor repository root.");
    }

    private sealed class ThrowingDocumentEditorProvider : InMemoryDocumentEditorProvider
    {
        public override Task<DocumentEditorLoadResult> LoadAsync(
            string documentId,
            DocumentEditorLoadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Load failed.");
        }
    }
}
