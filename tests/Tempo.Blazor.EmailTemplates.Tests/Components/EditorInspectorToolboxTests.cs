using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A: the editor exposes the toolbox collapse contract (D10) and wires the inspector's
/// Duplicate/Delete header actions to the document history, so one click is exactly one undo step
/// (F8 lifts the same parameters onto <c>TmEditorShell.CollapsedPanels</c>).
/// </summary>
public class EditorInspectorToolboxTests : EmailEditorTestBase
{
    private static EmailTemplateDocument DocWithButton(out EmailButtonBlock button)
    {
        var document = new EmailTemplateDocument { Subject = "Ahoj" };
        var section = new EmailSection();
        var column = new EmailColumn();
        button = new EmailButtonBlock { Text = "Jdi", Href = "https://x" };
        column.Blocks.Add(button);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private IRenderedComponent<TmEmailTemplateEditor> RenderEditor(
        EmailTemplateDocument document,
        Action<bool>? onToolboxCollapsed = null,
        Action<EmailTemplateDocument>? onDocumentChanged = null,
        bool toolboxCollapsed = false)
        => Render<TmEmailTemplateEditor>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.OnSave, (EmailSaveRequest request) => Task.FromResult(EmailSaveResult.Saved()));
            p.Add(c => c.ToolboxCollapsed, toolboxCollapsed);
            if (onToolboxCollapsed is not null)
                p.Add(c => c.ToolboxCollapsedChanged, (bool value) => onToolboxCollapsed(value));
            if (onDocumentChanged is not null)
                p.Add(c => c.DocumentChanged, (EmailTemplateDocument changed) => onDocumentChanged(changed));
        });

    [Fact]
    public void Toolbox_collapse_parameter_rails_the_palette()
    {
        // Fáze 8 (D10): ToolboxCollapsed is the shell's CollapsedPanels.Left — a collapsed panel is a
        // RAIL, not a hidden list inside a full-width panel, so the palette itself is not rendered.
        var document = DocWithButton(out _);
        var cut = RenderEditor(document, toolboxCollapsed: true);

        cut.FindAll("[data-tm-toolbox-list]").Should().BeEmpty("the collapsed palette is a rail, not a hidden list");
        cut.FindAll("[data-region='rail-left'] [data-tm-rail-block]").Should().NotBeEmpty(
            "the rail keeps every block one tap away");
    }

    [Fact]
    public void Collapsing_the_toolbox_reports_the_state_to_the_host()
    {
        var document = DocWithButton(out _);
        bool? reported = null;
        var cut = RenderEditor(document, value => reported = value);

        // The shell's panel toggle is the ONE control for this state: the palette's own arrow is off
        // (ShowCollapseToggle=false), so the same state cannot be changed from two places.
        cut.Find("[data-region='left'] ~ button.tm-editor-shell__panel-toggle--left").Click();

        reported.Should().BeTrue();
    }

    [Fact]
    public void Selecting_a_block_shows_the_inspector_with_duplicate_and_delete()
    {
        var document = DocWithButton(out var button);
        var cut = RenderEditor(document);

        cut.Find($"[data-tm-block-id=\"{button.Id}\"]").Click();

        cut.Find("[data-tm-prop-duplicate]").Should().NotBeNull();
        cut.Find("[data-tm-prop-delete]").Should().NotBeNull();
    }

    [Fact]
    public void Inspector_duplicate_is_one_undo_step()
    {
        var document = DocWithButton(out var button);
        var emissions = new List<EmailTemplateDocument>();
        var cut = RenderEditor(document, onDocumentChanged: changed => emissions.Add(changed));

        cut.Find($"[data-tm-block-id=\"{button.Id}\"]").Click();
        cut.Find("[data-tm-prop-duplicate]").Click();

        emissions.Should().NotBeEmpty();
        emissions[^1].Sections[0].Columns[0].Blocks.Should().HaveCount(2);

        cut.Find("[data-tm-undo]").Click();

        emissions[^1].Sections[0].Columns[0].Blocks.Should().HaveCount(1,
            "the duplicate must be a single history step");
    }

    [Fact]
    public void Inspector_delete_is_one_undo_step()
    {
        var document = DocWithButton(out var button);
        var emissions = new List<EmailTemplateDocument>();
        var cut = RenderEditor(document, onDocumentChanged: changed => emissions.Add(changed));

        cut.Find($"[data-tm-block-id=\"{button.Id}\"]").Click();
        cut.Find("[data-tm-prop-delete]").Click();

        emissions[^1].Sections[0].Columns[0].Blocks.Should().BeEmpty();

        cut.Find("[data-tm-undo]").Click();

        emissions[^1].Sections[0].Columns[0].Blocks.Should().HaveCount(1);
    }

    [Fact]
    public void Inspector_actions_do_nothing_in_read_only()
    {
        var document = DocWithButton(out var button);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.OnSave, (EmailSaveRequest request) => Task.FromResult(EmailSaveResult.Saved()))
            .Add(c => c.ReadOnly, true));

        cut.Find($"[data-tm-block-id=\"{button.Id}\"]").Click();

        cut.FindAll("[data-tm-prop-duplicate]").Should().BeEmpty();
        cut.FindAll("[data-tm-prop-delete]").Should().BeEmpty();
        document.Sections[0].Columns[0].Blocks.Should().HaveCount(1);
    }

    [Fact]
    public void Deleting_a_block_is_announced_so_the_live_region_reports_the_undo_hint()
    {
        var document = DocWithButton(out var button);
        var cut = RenderEditor(document);

        cut.Find($"[data-tm-block-id=\"{button.Id}\"]").Click();
        cut.Find("[data-tm-prop-delete]").Click();

        cut.Find("[data-tm-save-announcer]").TextContent.Should().Be("EmailEditor_Announce_BlockDeleted",
            "the polite live region names the outcome and the undo chord (the block is gone from the canvas)");
    }

    [Fact]
    public void Deleting_a_block_moves_focus_out_of_the_removed_header()
    {
        var document = DocWithButton(out var button);
        var cut = RenderEditor(document);

        cut.Find($"[data-tm-block-id=\"{button.Id}\"]").Click();
        cut.Find("[data-tm-prop-delete]").Click();

        // The deleted block's header is gone; focus must not fall to <body>. The canvas root is the
        // fallback programmatic focus target (a neighbour when one is left, the canvas when none is).
        JSInterop.Invocations.Should().Contain(invocation =>
                invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase),
            "the caret is handed back into the canvas, never dropped to <body>");
    }

    [Fact]
    public void The_toolbox_panel_header_is_not_rendered_twice()
    {
        // The palette carries its own title plus the collapse arrow on one row; the editor's panel header
        // above it would be a second title for the same surface (and it clipped to "Bl…" in the rail).
        var document = DocWithButton(out _);
        var cut = RenderEditor(document);

        cut.FindAll(".tm-email-editor__panel--toolbox .tm-email-editor__panel-header").Should().BeEmpty();
        cut.Find("[data-tm-toolbox] .tm-email-toolbox__title").Should().NotBeNull();
    }
}
