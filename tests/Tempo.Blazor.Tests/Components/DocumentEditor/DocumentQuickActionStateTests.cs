using Bunit;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Save / Undo / Redo live in ONE place - the header, or the ribbon when <c>ShowHeader</c> is false. Both places ask
/// <see cref="DocumentQuickActionState"/>, so they cannot disagree (redesign 146c7e1b, DE-1.2).
/// </summary>
public class DocumentQuickActionStateTests : LocalizationTestBase
{
    [Fact]
    public void WithoutRegistry_SaveFollowsReadOnlyAndSaving_UndoRedoFollowTheHostFlags()
    {
        DocumentQuickActionState.Resolve(null, readOnly: false, isSaving: false, canUndo: true, canRedo: false)
            .Should().Be(new DocumentQuickActionState(true, true, false));
        DocumentQuickActionState.Resolve(null, readOnly: false, isSaving: true, canUndo: false, canRedo: true)
            .Should().Be(new DocumentQuickActionState(false, false, true));
    }

    [Fact]
    public void ReadOnly_DisablesEverything_EvenWhenTheHostReportsHistory()
    {
        DocumentQuickActionState.Resolve(null, readOnly: true, isSaving: false, canUndo: true, canRedo: true)
            .Should().Be(new DocumentQuickActionState(false, false, false));
    }

    [Fact]
    public void ARegistry_IsTheSourceOfTruthForSave_AndMayEnableUndoAndRedo()
    {
        var registry = new DocumentEditorCommandRegistry();
        registry.Register(new FuncDocumentEditorCommandEntry("save", affectsData: false, computeEnabled: _ => false, computeValue: _ => null, execute: (_, _) => Task.CompletedTask));
        registry.Register(new FuncDocumentEditorCommandEntry("undo", affectsData: false, computeEnabled: _ => true, computeValue: _ => null, execute: (_, _) => Task.CompletedTask));
        registry.RefreshAllAsync(new DocumentEditorCommandContext()).GetAwaiter().GetResult();

        DocumentQuickActionState.Resolve(registry, readOnly: false, isSaving: false, canUndo: false, canRedo: false)
            .Should().Be(new DocumentQuickActionState(false, true, false));
    }

    [Fact]
    public void Titles_NameTheReasonAndTheStep()
    {
        var loc = Services.GetRequiredService<ITmLocalizer>();

        DocumentQuickActionState.SaveTitle(loc, readOnly: false, isSaving: false).Should().Be("Save");
        DocumentQuickActionState.SaveTitle(loc, readOnly: true, isSaving: false).Should().Contain("Save:");
        DocumentQuickActionState.SaveTitle(loc, readOnly: false, isSaving: true, canSave: false).Should().StartWith("Save: ");
        DocumentQuickActionState.UndoTitle(loc, readOnly: false, canUndo: true, "Typing").Should().Be("Undo: Typing");
        DocumentQuickActionState.UndoTitle(loc, readOnly: false, canUndo: false, null).Should().StartWith("Undo: ");
        DocumentQuickActionState.RedoTitle(loc, readOnly: true, canRedo: true, "x").Should().StartWith("Redo: ");
    }

    [Fact]
    public void HeaderAndRibbon_AgreeOnTheTooltipsAndTheEnablement()
    {
        var header = Render<TmDocumentEditorHeader>(p => p
            .Add(x => x.CanUndo, true)
            .Add(x => x.NextUndoDescription, "Typing"));
        var ribbon = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.CanUndo, true)
            .Add(x => x.NextUndoDescription, "Typing"));

        header.Find("[data-testid='document-undo']").GetAttribute("title")
            .Should().Be(ribbon.Find("[data-testid='document-undo']").GetAttribute("title"));
        header.Find("[data-testid='document-redo']").HasAttribute("disabled")
            .Should().Be(ribbon.Find("[data-testid='document-redo']").HasAttribute("disabled"));
        header.Find("[data-testid='document-save']").GetAttribute("title")
            .Should().Be(ribbon.Find("[data-testid='document-save']").GetAttribute("title"));
    }
}
