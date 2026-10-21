using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7 — <c>ReadOnly</c> (§3.2). A read-only editor keeps the preview, validation and export and
/// blocks <b>every</b> editing path: the toolbox, canvas drag&amp;drop and inline actions, the property
/// panel's inputs, import, undo/redo and the shortcuts that mutate the document. The component never
/// flips the flag itself; the host owns it.
/// </summary>
public class EditorReadOnlyTests : EmailEditorTestBase
{
    private IRenderedComponent<TmEmailTemplateEditor> RenderReadOnly(
        out EmailTemplateDocument document,
        out List<string> changes)
    {
        var doc = DocWithTextBlock(out _);
        document = doc;
        var recorded = new List<string>();
        changes = recorded;
        return Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved()))
            .Add(c => c.DocumentChanged, (EmailTemplateDocument d) =>
            {
                recorded.Add("DocumentChanged");
                return Task.CompletedTask;
            })
            .Add(c => c.IsDirtyChanged, (bool _) => recorded.Add("IsDirtyChanged")));
    }

    [Fact]
    public void TheToolbox_IsRenderedButInert()
    {
        var cut = RenderReadOnly(out var document, out var changes);
        var before = document.Sections[0].Columns[0].Blocks.Count;

        cut.Find("[data-tm-email-editor]").GetAttribute("data-tm-readonly").Should().Be("true");
        cut.FindAll("[data-tm-toolbox-list] [data-tm-block]").Should().NotBeEmpty("the palette stays visible");
        foreach (var tile in cut.FindAll("[data-tm-toolbox-list] [data-tm-block]"))
        {
            tile.GetAttribute("aria-disabled").Should().Be("true");
            tile.HasAttribute("draggable").Should().BeFalse("a read-only tile must not start a drag");
        }

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.FindAll("[data-tm-preset]").First().Click();

        document.Sections[0].Columns[0].Blocks.Should().HaveCount(before);
        document.Sections.Should().HaveCount(1);
        changes.Should().BeEmpty();
    }

    [Fact]
    public void TheCanvas_RendersNoInlineActions_AndIgnoresDropsAndKeys()
    {
        var cut = RenderReadOnly(out var document, out var changes);
        var blockId = document.Sections[0].Columns[0].Blocks[0].Id;

        cut.FindAll("[data-tm-block-action]").Should().BeEmpty("moving/duplicating/deleting is editing");
        cut.FindAll("[data-tm-section-action]").Should().BeEmpty();
        cut.Find($"[data-tm-block-id=\"{blockId}\"]").HasAttribute("draggable").Should().BeFalse();
        cut.FindAll("[data-tm-drop-col]").Should().BeEmpty("no drop zone may invite an edit");

        cut.Find("[data-tm-canvas-doc]").KeyDown(new KeyboardEventArgs { Key = "Delete" });
        cut.Find("[data-tm-canvas-doc]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        document.Sections[0].Columns[0].Blocks.Should().HaveCount(1);
        changes.Should().BeEmpty();
    }

    [Fact]
    public void TheInspector_IsDisabled_AndDoesNotCommit()
    {
        var cut = RenderReadOnly(out var document, out var changes);

        cut.Find("[data-tm-properties-panel] fieldset[disabled]").Should().NotBeNull(
            "a disabled fieldset is the native way to make a whole control group read-only");
        cut.FindAll("[data-tm-properties-panel] input[disabled]").Should().NotBeEmpty();

        var subject = cut.Find("[data-tm-prop='Subject'] input");
        subject.Change("Read-only edit");

        changes.Should().BeEmpty("no property change may reach the host");
        cut.Instance.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ImportUndoRedo_AreDisabled_AndTheShortcutsDoNothing()
    {
        var cut = RenderReadOnly(out var document, out var changes);

        cut.FindAll("[data-tm-import-btn][disabled]").Should().ContainSingle();
        cut.FindAll("[data-tm-undo][disabled]").Should().ContainSingle();
        cut.FindAll("[data-tm-redo][disabled]").Should().ContainSingle();

        var editor = cut.Find("[data-tm-email-editor]");
        editor.KeyDown(new KeyboardEventArgs { Key = "z", CtrlKey = true });
        editor.KeyDown(new KeyboardEventArgs { Key = "y", CtrlKey = true });
        editor.KeyDown(new KeyboardEventArgs { Key = "d", CtrlKey = true });
        editor.KeyDown(new KeyboardEventArgs { Key = "x", CtrlKey = true });
        editor.KeyDown(new KeyboardEventArgs { Key = "v", CtrlKey = true });

        document.Sections[0].Columns[0].Blocks.Should().HaveCount(1);
        changes.Should().BeEmpty();
    }

    [Fact]
    public void CtrlS_IsASilentNoOp_NotAnError()
    {
        var calls = 0;
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnSave, (EmailSaveRequest _) =>
            {
                calls++;
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        calls.Should().Be(0, "a read-only editor never writes through the host");
        cut.FindAll("[data-tm-save-badge]").Should().BeEmpty(
            "a keystroke that cannot save anything must not paint a failure state over the editor");
    }

    [Fact]
    public void Preview_Validation_AndExport_StayAvailable()
    {
        var cut = RenderReadOnly(out _, out _);

        cut.Find("[data-tm-preview-btn]").HasAttribute("disabled").Should().BeFalse("preview is not an edit");
        cut.Find("[data-tm-validate-btn]").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-tm-mjml-btn]").HasAttribute("disabled").Should().BeFalse();

        cut.Find("[data-tm-preview-btn]").Click();
        cut.FindComponents<TmEmailTemplatePreview>().Should().ContainSingle();
    }

    [Fact]
    public void TheSaveButton_IsNotRendered_AndTheScopeNeverFlipsTheFlagItself()
    {
        var hostReadOnly = true;
        var document = DocWithTextBlock(out _);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ReadOnly, hostReadOnly)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.FindAll("[data-tm-save]").Should().BeEmpty("there is nothing a read-only editor could save");
        cut.FindAll("[data-tm-readonly-badge]").Should().ContainSingle("the mode is stated, not implied");

        // A host-pinned value stays pinned: no interaction may flip ReadOnly inside the component.
        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-email-editor]").GetAttribute("data-tm-readonly").Should().Be("true");
        hostReadOnly.Should().BeTrue();
    }

    /// <summary>
    /// The sub-editors are separate components, so the panel's disabled fieldset is not enough: bUnit's
    /// <c>.Click()</c> and <c>.Change()</c> reach the handler even inside a disabled fieldset, and a real
    /// browser can deliver a synthesised event too. Every mutating path has to refuse on its own.
    /// </summary>
    [Fact]
    public void TheSubEditors_IgnoreClicksAndCellEdits_InReadOnly()
    {
        var document = new EmailTemplateDocument { Subject = "Hello" };
        var section = new EmailSection();
        var column = new EmailColumn();
        var table = new EmailTableBlock();
        var row = new EmailTableRow();
        row.Cells.Add(new EmailTableCell { Text = "cell" });
        table.Rows.Add(row);
        var social = new EmailSocialBlock();
        social.Elements.Add(new EmailSocialElement { Name = "x", Href = "https://example.com" });
        column.Blocks.Add(table);
        column.Blocks.Add(social);
        var tableId = table.Id;
        var socialId = social.Id;
        section.Columns.Add(column);
        document.Sections.Add(section);

        var before = EmailTemplateSerializer.Serialize(document);
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        // Selecting is reading, so it still works — and it is what brings each sub-editor into the inspector.
        cut.Find($"[data-tm-block-id=\"{tableId}\"]").Click();
        cut.Find("[data-tm-table-cell] input").Change("hacked");
        cut.Find("[data-tm-table-row-remove]").Click();
        cut.Find("[data-tm-table-add-row]").Click();

        cut.Find($"[data-tm-block-id=\"{socialId}\"]").Click();
        cut.Find("[data-tm-list-action=\"remove\"]").Click();
        cut.Find("[data-tm-list-add]").Click();
        cut.Find("[data-tm-kv-add]").Click();

        (EmailTemplateSerializer.Serialize(document) == before).Should().BeTrue(
            "no read-only interaction may change the serialized document");
        cut.Instance.IsDirty.Should().BeFalse();
    }
}
