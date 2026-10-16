using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// DI1 read-only guard rails for <see cref="TmDiagramEditor"/>: the built-in Read-only/Edit
/// toggle flips an internal user state and NEVER writes back to the <c>[Parameter] ReadOnly</c>,
/// the toggle is hidden while the host pins <c>ReadOnly=true</c> (a hosted preview can no longer
/// be unlocked from the UI), and the effective read-only state blocks every editing path
/// (undo/redo, paste, SQL/CSV import, templates).
/// </summary>
public class TmDiagramEditorReadOnlyTests : LocalizationTestBase
{
    public TmDiagramEditorReadOnlyTests()
    {
        Services.GetRequiredService<DiagramStencilRegistry>()
            .RegisterProvider(new BuiltInDiagramStencilProvider());
        Services.GetRequiredService<DiagramTemplateRegistry>()
            .RegisterTemplate(new DiagramTemplate
            {
                Id = "tpl-readonly-test",
                Name = "Read-only Test Template",
                Category = "Test",
                DocumentJson = DiagramSerializer.Serialize(CreateTemplateDocument()),
            });
    }

    [Fact]
    public void HostReadOnly_True_HidesTheReadOnlyToggle()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, true));

        cut.FindAll("[data-tm-group='readonly']").Should().BeEmpty(
            "a host-pinned read-only editor must not offer the unlock toggle");
        cut.Instance.IsReadOnly.Should().BeTrue();
        cut.Instance.ReadOnly.Should().BeTrue("the host parameter stays the source of truth");
    }

    [Fact]
    public void HostReadOnly_True_HidesTheAddPageButton()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, true));

        cut.FindAll(".tm-diagram-editor__page-tab-add").Should().BeEmpty(
            "a read-only editor / Viewer preview must not show the add-page button");

        cut.Render(p => p.Add(e => e.ReadOnly, false));

        cut.FindAll(".tm-diagram-editor__page-tab-add").Should().ContainSingle(
            "a writable editor keeps the add-page button");
    }

    [Fact]
    public void HostReadOnly_True_RootElementCarriesTheReadonlyClass()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, true));

        cut.Find("[data-testid='diagram-editor']").ClassList
            .Should().Contain("tm-diagram-editor--readonly");
    }

    [Fact]
    public void ToggleClick_FlipsInternalState_WithoutWritingTheReadOnlyParameter()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.Instance.ReadOnly.Should().BeFalse(
            "the [Parameter] ReadOnly is never assigned inside the component");
        cut.Instance.IsReadOnly.Should().BeTrue("the toggle flips the internal user state");
        cut.Find("[data-tm-group='readonly'] button").ClassList
            .Should().Contain("tm-toolbar-btn--active", "the toggle reflects the effective state");
        cut.Find("[data-testid='diagram-editor']").ClassList
            .Should().Contain("tm-diagram-editor--readonly");
    }

    [Fact]
    public void ToggleClick_SurvivesParentRerender_WithoutGoingInconsistent()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();
        cut.Instance.IsReadOnly.Should().BeTrue();

        // Parent re-renders with the same ReadOnly=false: the user's local lock persists, so the
        // editor never disagrees with what the toggle shows.
        cut.Render();
        cut.Instance.IsReadOnly.Should().BeTrue();
        cut.Instance.ReadOnly.Should().BeFalse();
        cut.FindAll("[data-tm-group='readonly']").Should().HaveCount(1);
        cut.Find("[data-tm-group='readonly'] button").ClassList
            .Should().Contain("tm-toolbar-btn--active");

        // And the user can still unlock via the same toggle.
        cut.Find("[data-tm-group='readonly'] button").Click();
        cut.Instance.IsReadOnly.Should().BeFalse();
        cut.Instance.ReadOnly.Should().BeFalse();
        cut.Find("[data-tm-group='readonly'] button").ClassList
            .Should().NotContain("tm-toolbar-btn--active");
    }

    [Fact]
    public void EffectiveReadOnly_BlocksUndoRedoKeyboardPaths()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "HandleQuickInsert", "general.rectangle");
        doc.Nodes.Should().HaveCount(1);

        cut.Find("[data-tm-group='readonly'] button").Click();
        cut.Instance.IsReadOnly.Should().BeTrue();

        // Simulated keyboard undo/redo (Ctrl+Z / Ctrl+Y arrive from diagram-editor.js) — must no-op.
        InvokePrivate(cut.Instance, "OnUndoClicked");
        InvokePrivate(cut.Instance, "OnRedoClicked");
        doc.Nodes.Should().HaveCount(1, "undo must not run in the effective read-only state");
    }

    [Fact]
    public void EffectiveReadOnly_BlocksPaste()
    {
        var doc = CreateDocument();
        DiagramClipboard.Nodes.Clear();
        DiagramClipboard.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 10, Y = 10, W = 100, H = 50,
        });

        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        // bUnit raises the click event even on a disabled button — the handler must still no-op.
        cut.FindAll("button").First(b => b.GetAttribute("aria-label") == "Paste").Click();
        doc.Nodes.Should().BeEmpty("paste must not insert clipboard nodes in the effective read-only state");
    }

    [Fact]
    public void EffectiveReadOnly_BlocksSqlAndCsvImportDialogs()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        InvokePrivate(cut.Instance, "HandleImportSelect", "sql");
        InvokePrivate(cut.Instance, "HandleImportSelect", "csv");

        cut.FindAll(".tm-diagram-sql-import").Should().BeEmpty();
        cut.FindAll(".tm-diagram-csv-import").Should().BeEmpty();
    }

    [Fact]
    public void EffectiveReadOnly_TemplatesButton_DoesNotOpenTheGallery()
    {
        var doc = CreateDocument();
        doc.Title = "Original";
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.FindAll("button").First(b => b.TextContent.Contains("Templates")).Click();

        cut.FindAll(".tm-diagram-template-gallery").Should().BeEmpty(
            "the template gallery must not open in the effective read-only state");
        cut.Instance.Document.Should().BeSameAs(doc,
            "a read-only editor must not swap to the template document");
        doc.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void HostReadOnly_TemplatesButton_DoesNotOpenTheGallery()
    {
        var doc = CreateDocument();
        doc.Title = "Original";
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, true));

        cut.FindAll("button").First(b => b.TextContent.Contains("Templates")).Click();

        cut.FindAll(".tm-diagram-template-gallery").Should().BeEmpty(
            "a host-pinned read-only editor must not open the template gallery");
        cut.Instance.Document.Should().BeSameAs(doc);
    }

    [Fact]
    public void ReadOnlyToggle_PushesReadOnlyToTheCanvasJs_TrueThenFalse()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var setReadOnly = JSInterop.SetupVoid("tmDiagramEditor.setReadOnly", _ => true).SetVoidResult();

        cut.Find("[data-tm-group='readonly'] button").Click();

        setReadOnly.VerifyInvoke("tmDiagramEditor.setReadOnly", 1);
        setReadOnly.Invocations["tmDiagramEditor.setReadOnly"].Single().Arguments[1].Should().Be(true);

        cut.Find("[data-tm-group='readonly'] button").Click();

        setReadOnly.VerifyInvoke("tmDiagramEditor.setReadOnly", 2);
        setReadOnly.Invocations["tmDiagramEditor.setReadOnly"].Last().Arguments[1].Should().Be(false);
    }

    [Fact]
    public void EffectiveReadOnly_LayerMutationsDoNotChangeTheDocument()
    {
        var doc = CreateDocument();
        doc.Layers.Add(new DiagramLayer { Name = "Base", Order = 0 });
        doc.Layers.Add(new DiagramLayer { Name = "Detail", Order = 1 });
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ShowLayersPanel, true));

        cut.Find("[data-tm-group='readonly'] button").Click();

        var layersBefore = doc.Layers.Count;
        cut.Find(".tm-diagram-layers__add-btn").Click();
        doc.Layers.Should().HaveCount(layersBefore, "add-layer must no-op in the effective read-only state");

        cut.FindAll(".tm-diagram-layers__delete").Should().NotBeEmpty();
        cut.Find(".tm-diagram-layers__delete").Click();
        doc.Layers.Should().HaveCount(layersBefore, "delete-layer must no-op in the effective read-only state");

        cut.Find(".tm-diagram-layers__name").TriggerEvent("ondblclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.FindAll(".tm-diagram-layers__name-input").Should().BeEmpty(
            "rename must not start in the effective read-only state");
        doc.Layers.Single(l => l.Name == "Detail").Name.Should().Be("Detail");
    }

    [Fact]
    public void HostReadOnly_LayerMutationsDoNotChangeTheDocument()
    {
        var doc = CreateDocument();
        doc.Layers.Add(new DiagramLayer { Name = "Base", Order = 0 });
        doc.Layers.Add(new DiagramLayer { Name = "Detail", Order = 1 });
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, true)
            .Add(e => e.ShowLayersPanel, true));

        var layersBefore = doc.Layers.Count;
        cut.Find(".tm-diagram-layers__add-btn").Click();
        doc.Layers.Should().HaveCount(layersBefore, "add-layer must no-op when the host pins read-only");

        cut.Find(".tm-diagram-layers__delete").Click();
        doc.Layers.Should().HaveCount(layersBefore, "delete-layer must no-op when the host pins read-only");

        cut.Find(".tm-diagram-layers__name").TriggerEvent("ondblclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.FindAll(".tm-diagram-layers__name-input").Should().BeEmpty(
            "rename must not start when the host pins read-only");
    }

    [Fact]
    public void EffectiveReadOnly_ContextMenuUndo_DoesNotTouchTheCommandStack()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "HandleQuickInsert", "general.rectangle");
        doc.Nodes.Should().HaveCount(1);
        GetActiveCommandStack(cut.Instance).CanUndo.Should().BeTrue();

        cut.Find("[data-tm-group='readonly'] button").Click();

        InvokePrivate(cut.Instance, "ContextMenuUndo");

        doc.Nodes.Should().HaveCount(1, "context-menu undo must no-op in the effective read-only state");
        GetActiveCommandStack(cut.Instance).CanUndo.Should().BeTrue(
            "the undo entry must still be on the stack");
    }

    [Fact]
    public void EffectiveReadOnly_ContextMenuRedo_DoesNotTouchTheCommandStack()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "HandleQuickInsert", "general.rectangle");
        cut.InvokeAsync(() => InvokePrivate(cut.Instance, "OnUndoClicked")).GetAwaiter().GetResult();
        doc.Nodes.Should().BeEmpty();
        GetActiveCommandStack(cut.Instance).CanRedo.Should().BeTrue();

        cut.Find("[data-tm-group='readonly'] button").Click();

        InvokePrivate(cut.Instance, "ContextMenuRedo");

        doc.Nodes.Should().BeEmpty("context-menu redo must no-op in the effective read-only state");
        GetActiveCommandStack(cut.Instance).CanRedo.Should().BeTrue(
            "the redo entry must still be on the stack");
    }

    [Fact]
    public void WritableEditor_ContextMenuUndo_RestoresTheQuickInsertedNode()
    {
        // Control test proving the ContextMenuUndo harness above actually undoes.
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "HandleQuickInsert", "general.rectangle");
        InvokePrivate(cut.Instance, "ContextMenuUndo");

        doc.Nodes.Should().BeEmpty();
        GetActiveCommandStack(cut.Instance).CanRedo.Should().BeTrue();
    }

    [Fact]
    public async Task WritableEditor_TemplateSelection_StillReplacesTheDocument()
    {
        var doc = CreateDocument();
        doc.Title = "Original";
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.FindAll("button").First(b => b.TextContent.Contains("Templates")).Click();
        cut.Find(".tm-diagram-template-card").Click();
        var create = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        await cut.InvokeAsync(() => create.Click());

        cut.Instance.Document.Should().NotBeSameAs(doc,
            "the template gallery keeps working when writable");
        cut.Instance.Document!.Title.Should().Be("Read-only Test Template");
    }

    private static DiagramDocument CreateDocument()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        return doc;
    }

    private static DiagramDocument CreateTemplateDocument()
    {
        var doc = new DiagramDocument { Title = "Read-only Test Template" };
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100, Y = 100, W = 120, H = 60,
        });
        return doc;
    }

    private static DiagramCommandStack GetActiveCommandStack(TmDiagramEditor editor)
        => (typeof(TmDiagramEditor)
            .GetProperty("ActiveCommandStack", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ActiveCommandStack must exist"))
            .GetValue(editor) as DiagramCommandStack
            ?? throw new InvalidOperationException("ActiveCommandStack must return a DiagramCommandStack");

    private static void InvokePrivate(TmDiagramEditor editor, string methodName, params object?[] args)
    {
        var method = typeof(TmDiagramEditor).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: args.Select(a => a?.GetType() ?? typeof(object)).ToArray(),
            modifiers: null);
        method.Should().NotBeNull($"'{methodName}' must exist");
        try
        {
            var result = method!.Invoke(editor, args);
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
