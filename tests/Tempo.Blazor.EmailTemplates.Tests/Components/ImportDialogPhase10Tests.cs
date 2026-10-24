using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 10 (§2.4.4–2.4.6, B12): the import dialog is a two-mode (MJML / JSON) drop zone plus code surface
/// with the limits that come from <see cref="EmailImportOptions"/>, a structure preview, the localized
/// warnings of the import and a real footer. Nothing reaches the document until the user presses
/// “Import”, and every failure is a localized sentence — never an exception message.
/// </summary>
public class ImportDialogPhase10Tests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("en");

    public ImportDialogPhase10Tests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
        Services.AddHttpClient();
    }

    void IDisposable.Dispose()
    {
        _culture.Dispose();
        Dispose();
    }

    private const string SimpleMjml =
        "<mjml><mj-body><mj-section><mj-column><mj-text>Imported</mj-text></mj-column></mj-section></mj-body></mjml>";

    // ── modes and input surface ────────────────────────────────────────────────────────────────

    [Fact]
    public void Modes_AreTabs_WithTheMjmlTabActive()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("#tab-import-mjml").GetAttribute("aria-selected").Should().Be("true");
        cut.Find("#tab-import-json").GetAttribute("aria-selected").Should().Be("false");
        cut.Find("[data-tm-import-input] textarea").HasAttribute("readonly").Should().BeFalse();
    }

    [Fact]
    public void DropZone_NamesTheSupportedFormatsAndTheConfiguredLimit()
    {
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.ImportOptions, new EmailImportOptions { MaxInputBytes = 2 * 1024 * 1024 }));

        var zone = cut.Find("[data-tm-import-dropzone]");
        zone.GetAttribute("data-tm-import-limit").Should().Be((2 * 1024 * 1024).ToString());
        zone.TextContent.Should().Contain("EmailEditor_Import_DropFormats");
        zone.TextContent.Should().Contain("EmailEditor_Import_DropLimit");
        cut.Find("[data-tm-import-dropzone] input[type=file]").GetAttribute("accept").Should().Contain(".mjml");

        cut.Find("#tab-import-json").Click();
        cut.Find("[data-tm-import-dropzone] input[type=file]").GetAttribute("accept").Should().Contain(".json");
    }

    [Fact]
    public void Subtitle_ExplainsTheUploadOrPasteChoice()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find(".tm-modal-subtitle").TextContent.Should().Be("EmailEditor_Import_Subtitle");
    }

    // ── parsing + preview ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mjml_ParsesIntoAStructurePreview_WithLocalizedBlockNames()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-tm-import-input] textarea").Input(SimpleMjml);

        cut.FindAll("[data-tm-import-section]").Should().HaveCount(1);
        cut.FindAll("[data-tm-import-column]").Should().HaveCount(1);
        var block = cut.Find("[data-tm-import-block]");
        block.GetAttribute("data-tm-import-block-kind").Should().Be("Text");
        block.TextContent.Should().Be("block.text.name", "the type name is localized, not the discriminator");
    }

    private static string ManyBlocksJson(int blocks)
    {
        var column = new EmailColumn();
        for (var i = 0; i < blocks; i++) column.Blocks.Add(new EmailTextBlock { Content = "row " + i });
        return EmailTemplateSerializer.Serialize(new EmailTemplateDocument
        {
            Subject = "S",
            Sections = { new EmailSection { Columns = { column } } },
        });
    }

    [Theory]
    [InlineData(150)]
    [InlineData(1000)]
    public void StructurePreview_AboveTheFormerHundredRowLimit_IsVirtualizedNotTruncated(int blocks)
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));
        cut.Find("#tab-import-json").Click();

        cut.Find("[data-tm-import-input] textarea").Input(ManyBlocksJson(blocks));

        // The state card still counts EVERY block; the preview no longer stops at 100 and no longer needs a
        // "…and N more blocks" note: its rows (1 section + 1 column + the blocks) all exist in the list model.
        var summary = cut.Find("[data-tm-import-summary]");
        summary.GetAttribute("data-tm-import-sections").Should().Be("1");
        summary.GetAttribute("data-tm-import-blocks").Should().Be(blocks.ToString());
        cut.Find("[data-tm-import-tree]").GetAttribute("data-tm-import-rows").Should().Be((blocks + 2).ToString());
        cut.FindAll("[data-tm-import-structure-more]").Should().BeEmpty(
            "nothing is left out any more, so there is no truncation note");

        // A row names its place in the whole list, even though only a window of rows is in the DOM.
        var first = cut.Find("[data-tm-import-block]");
        first.GetAttribute("aria-posinset").Should().Be("3", "section and column come first");
        first.GetAttribute("aria-setsize").Should().Be((blocks + 2).ToString());
    }

    [Fact]
    public void StructurePreview_IsADivList_WithDivSpacers_AndNoEmptyListItems()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));
        cut.Find("#tab-import-json").Click();
        cut.Find("[data-tm-import-input] textarea").Input(ManyBlocksJson(40));

        var tree = cut.Find("[data-tm-import-tree]");
        tree.TagName.Should().Be("DIV");
        tree.GetAttribute("role").Should().Be("list");
        tree.GetAttribute("aria-label").Should().BeNull("the section is named by the heading, not by a duplicate label");

        cut.FindAll("[data-tm-import-tree] li").Should().BeEmpty("spacers are divs, so no empty <li> sits inside the list");
        var rows = cut.FindAll("[data-tm-import-tree] [data-tm-import-block]");
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.GetAttribute("role") == "listitem");
        foreach (var row in rows)
        {
            var label = row.QuerySelector(".tm-email-import__tree-label")!;
            label.GetAttribute("title").Should().Be(label.TextContent.Trim(), "the ellipsis span carries the full name as its title");
        }

        var section = cut.Find("[data-tm-import-structure]");
        var titleId = cut.Find(".tm-email-import__structure-title").Id;
        titleId.Should().NotBeNullOrEmpty();
        section.GetAttribute("aria-labelledby").Should().Be(titleId);
    }
    [Fact]
    public void StructurePreview_RowsAreFixedHeight_SoTheVirtualizeSpacersAreExact()
    {
        var css = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Tempo.Blazor.EmailTemplates", "Components", "TmEmailImportDialog.razor.css"));

        css.Should().MatchRegex(@"\.tm-email-import__tree-row\s*\{[^}]*block-size:\s*1\.75rem",
            "28 px is StructureRowHeight: a row that wrapped or grew would desynchronise the scroll extent");
        css.Should().MatchRegex(@"\.tm-email-import__tree-row\s*\{[^}]*min-block-size:\s*1\.75rem");
        css.Should().MatchRegex(@"\.tm-email-import__tree-row\s*\{[^}]*white-space:\s*nowrap");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void Json_ParsesIntoTheSamePreview()
    {
        var json = EmailTemplateSerializer.Serialize(new EmailTemplateDocument
        {
            Subject = "S",
            Sections =
            {
                new EmailSection
                {
                    Columns =
                    {
                        new EmailColumn { Blocks = { new EmailTextBlock { Content = "From JSON" } } },
                    },
                },
            },
        });

        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));
        cut.Find("#tab-import-json").Click();
        cut.Find("[data-tm-import-input] textarea").Input(json);

        cut.Find("[data-tm-import-summary]").GetAttribute("data-tm-import-state").Should().Be("success");
        cut.Find("[data-tm-import-summary]").GetAttribute("data-tm-import-sections").Should().Be("1");
        cut.Find("[data-tm-import-summary]").GetAttribute("data-tm-import-blocks").Should().Be("1");
    }

    [Fact]
    public void Summary_CountsSectionsBlocksAndWarnings()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-tm-import-input] textarea").Input(SimpleMjml);

        var summary = cut.Find("[data-tm-import-summary]");
        summary.GetAttribute("data-tm-import-state").Should().Be("success");
        summary.GetAttribute("data-tm-import-sections").Should().Be("1");
        summary.GetAttribute("data-tm-import-blocks").Should().Be("1");
        summary.GetAttribute("data-tm-import-warning-count").Should().Be("0");
        summary.TextContent.Should().Contain("EmailEditor_Import_SummarySections|1");
        summary.TextContent.Should().Contain("EmailEditor_Import_SummaryBlocks|1");
    }

    // ── failures are localized ────────────────────────────────────────────────────────────────

    [Fact]
    public void JsonWithANewerSchema_ReportsTheLocalizedKey_NotTheExceptionMessage()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("#tab-import-json").Click();
        cut.Find("[data-tm-import-input] textarea").Input("{\"schemaVersion\":99,\"sections\":[]}");

        var error = cut.Find("[data-tm-import-errors]");
        error.TextContent.Should().Contain(SerializationKeys.UnsupportedSchemaVersion);
        error.TextContent.Should().NotContain("99", "a raw exception message is not a user-facing sentence");
        cut.Find("[data-tm-import-summary]").GetAttribute("data-tm-import-state").Should().Be("error");
        cut.Find("[data-tm-import-confirm]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void MalformedJson_KeepsTheInput_AndOffersNoImport()
    {
        EmailTemplateDocument? imported = null;
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.OnImport, d => imported = d));

        cut.Find("#tab-import-json").Click();
        cut.Find("[data-tm-import-input] textarea").Input("{ not json");

        cut.Find("[data-tm-import-input] textarea").GetAttribute("value").Should().Be("{ not json",
            "a failed parse must not wipe what the user pasted");
        cut.Find("[data-tm-import-errors]").TextContent.Should().Contain(SerializationKeys.Malformed);
        cut.Find("[data-tm-import-confirm]").Click();
        imported.Should().BeNull();
    }

    [Fact]
    public void InputOverMaxInputBytes_ReportsTheLimitKey_WithoutParsing()
    {
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.ImportOptions, new EmailImportOptions { MaxInputBytes = 64 }));

        cut.Find("[data-tm-import-input] textarea").Input(SimpleMjml);

        // Fáze 10 review (UX 13): the PASTED text is measured before anything parses — a field has no InputFile
        // to be rejected by, and the sentence names the limit the host configured.
        cut.Find("[data-tm-import-errors]").TextContent.Should().Contain("EmailEditor_Import_InputTooLarge");
        cut.Find("[data-tm-import-errors]").TextContent.Should().Contain("64 B");
        cut.Find("[data-tm-import-confirm]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void DropZone_AcceptsEverySupportedFormat_RegardlessOfTheActiveTab()
    {
        // Fáze 10 review (UX 12): the drop zone used to narrow `accept` to the ACTIVE TAB's format, so a .json
        // file dropped while the MJML tab was open could not even be chosen.
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-testid='tm-import-file']").GetAttribute("accept").Should().Be(".mjml,.html,.json,.txt");

        cut.Find("#tab-import-json").Click();
        cut.Find("[data-testid='tm-import-file']").GetAttribute("accept").Should().Be(".mjml,.html,.json,.txt");
    }

    [Fact]
    public void JsonPastedOnTheMjmlTab_IsDetectedAsJson_AndParses()
    {
        // Fáze 10 review (UX 12): the mode follows the CONTENT when the user pastes, so a template JSON pasted
        // on the MJML tab does not fail as malformed MJML.
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-tm-import-input] textarea").Input(
            "{\"subject\":\"From JSON\",\"sections\":[{\"columns\":[{\"blocks\":"
            + "[{\"$type\":\"text\",\"content\":\"Body\"}]}]}]}");

        cut.Find("[data-tm-import]").GetAttribute("data-tm-import-mode").Should().Be("json");
        cut.Find("[data-tm-import-summary]").GetAttribute("data-tm-import-state").Should().Be("success");
        cut.Find("#tab-import-json").GetAttribute("aria-selected").Should().Be("true",
            "the strip follows the detected mode");
        cut.Find("[data-tm-import-confirm]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ReplaceNote_AppearsOnceTheInputParses()
    {
        // Fáze 10 review (UX 11): pressing Import replaces the template; the card has to say so (and that the
        // editor can undo it) BEFORE the user presses it.
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.FindAll("[data-tm-import-replace-note]").Should().BeEmpty("nothing has parsed yet");

        cut.Find("[data-tm-import-input] textarea").Input(SimpleMjml);

        cut.Find("[data-tm-import-replace-note]").TextContent.Should().Be("EmailEditor_Import_ReplaceNote");
    }

    [Fact]
    public void Title_IsImportTemplate_AndBothModesHaveAPanel()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find(".tm-modal-title").TextContent.Should().Be("EmailEditor_Import_Title");
        cut.Find("#tab-import-mjml").GetAttribute("aria-controls").Should().Be("tabpanel-import-mjml");
        cut.Find("#tabpanel-import-mjml").QuerySelector("[data-tm-import-input]").Should().NotBeNull();

        cut.Find("#tab-import-json").Click();
        cut.Find("#tab-import-json").GetAttribute("aria-controls").Should().Be("tabpanel-import-json");
        cut.Find("#tabpanel-import-json").QuerySelector("[data-tm-import-input]").Should().NotBeNull();
    }

    [Fact]
    public void Warnings_AreListedWithTheirLocalizedSentences()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-tm-import-input] textarea").Input(
            "<mjml><mj-body><mj-section><mj-column><mj-unknown-thing x=\"1\"/><mj-text>Hi</mj-text></mj-column></mj-section></mj-body></mjml>");

        var summary = cut.Find("[data-tm-import-summary]");
        summary.GetAttribute("data-tm-import-state").Should().Be("warning");
        summary.GetAttribute("data-tm-import-warning-count").Should().Be("1");
        cut.Find("[data-tm-import-warnings]").TextContent.Should().Contain(ImportKeys.UnknownElement);
    }

    // ── confirmation contract ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_IsDisabledUntilTheInputParses_AndRaisesTheDocumentOnce()
    {
        var imported = new List<EmailTemplateDocument>();
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.OnImport, d => imported.Add(d)));

        cut.Find("[data-tm-import-confirm]").HasAttribute("disabled").Should().BeTrue();

        cut.Find("[data-tm-import-input] textarea").Input(SimpleMjml);
        cut.Find("[data-tm-import-confirm]").HasAttribute("disabled").Should().BeFalse();

        cut.Find("[data-tm-import-confirm]").Click();
        imported.Should().ContainSingle();
    }

    [Fact]
    public void Footer_HasCancelThatClosesWithoutImporting()
    {
        var closed = false;
        EmailTemplateDocument? imported = null;
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.OnClose, () => closed = true)
            .Add(c => c.OnImport, d => imported = d));

        cut.Find("[data-tm-import-input] textarea").Input(SimpleMjml);
        cut.Find("[data-tm-import-cancel]").Click();

        closed.Should().BeTrue();
        imported.Should().BeNull();
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }

    /// <summary>Echoes the key and appends the format arguments so a formatted key is still assertable.</summary>
    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "|" + string.Join("|", arguments);
    }
}
