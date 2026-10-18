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
        cut.Find("[data-tm-group='readonly'] button").GetAttribute("aria-pressed")
            .Should().Be("true", "the toggle reflects the effective state");
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
        cut.Find("[data-tm-group='readonly'] button").GetAttribute("aria-pressed")
            .Should().Be("true");

        // And the user can still unlock via the same toggle.
        cut.Find("[data-tm-group='readonly'] button").Click();
        cut.Instance.IsReadOnly.Should().BeFalse();
        cut.Instance.ReadOnly.Should().BeFalse();
        cut.Find("[data-tm-group='readonly'] button").GetAttribute("aria-pressed")
            .Should().Be("false");
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

        // DI2: the layers panel lives behind the inspector's Layers tab.
        OpenLayersTab(cut);

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

        // DI2: the layers panel lives behind the inspector's Layers tab.
        OpenLayersTab(cut);

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

    // ── Phase 1 (read-only & broken bindings): F3 / F2 / F4 / F5 / D9 ─────────

    [Fact]
    public void SearchPanel_OpensWithEmptyInputs_AndNoRegexError()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "OpenSearchPanel");
        cut.Render();

        cut.FindAll(".tm-diagram-search-panel input[type='search']")
            .Should().NotBeEmpty("the panel opened");
        foreach (var input in cut.FindAll(".tm-diagram-search-panel input[type='search']"))
        {
            input.GetAttribute("value").Should().BeEmpty(
                "the F3 literal bindings once showed the C# field names");
        }
        cut.FindAll(".tm-diagram-search-panel__error").Should().BeEmpty();
    }

    [Fact]
    public void EffectiveReadOnly_ReplaceAll_DoesNotChangeTheDocument()
    {
        var doc = CreateDocument();
        var node = new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 100, H = 50 };
        node.Data["text"] = "Alpha";
        doc.Nodes.Add(node);

        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        InvokePrivate(cut.Instance, "OpenSearchPanel");
        InvokePrivate(cut.Instance, "OnSearchQueryChanged", "Alpha");
        InvokePrivate(cut.Instance, "OnReplaceQueryChanged", "Beta");
        InvokePrivate(cut.Instance, "OnReplaceAll");
        InvokePrivate(cut.Instance, "OnReplace");

        node.Data["text"].Should().Be("Alpha",
            "replace must not run in the effective read-only state");
        GetActiveCommandStack(cut.Instance).CanUndo.Should().BeFalse();
        cut.FindAll(".tm-diagram-search-panel__replace").Should().BeEmpty(
            "the panel renders search-only while read-only");
    }

    [Fact]
    public async Task HostReadOnly_CtrlH_OpensSearchOnlyPanel()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, true));

        // Ctrl+H arrives from diagram-editor.js (case 'h') as JsOnShowReplace → OpenReplacePanel.
        await cut.InvokeAsync(() => cut.FindComponent<TmDiagramCanvas>().Instance.JsOnShowReplace());

        cut.FindAll(".tm-diagram-search-panel").Should().HaveCount(1);
        cut.FindAll(".tm-diagram-search-panel__replace").Should().BeEmpty(
            "a host-pinned read-only editor opens plain search only");
    }

    [Fact]
    public void WritableEditor_ReplaceAll_RaisesDocumentChanged_AndIsOneUndoStep()
    {
        var doc = CreateDocument();
        var node = new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 100, H = 50 };
        node.Data["text"] = "Alpha";
        doc.Nodes.Add(node);
        var calls = 0;

        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false)
            .Add(e => e.DocumentChanged, _ => calls++));

        InvokePrivate(cut.Instance, "OpenSearchPanel");
        InvokePrivate(cut.Instance, "OnSearchQueryChanged", "Alpha");
        InvokePrivate(cut.Instance, "OnReplaceQueryChanged", "Beta");
        InvokePrivate(cut.Instance, "OnReplaceAll");

        calls.Should().BeGreaterThanOrEqualTo(1, "the host must learn about the replacement");
        node.Data["text"].Should().Be("Beta");

        var stack = GetActiveCommandStack(cut.Instance);
        stack.Undo();
        node.Data["text"].ToString().Should().Be("Alpha",
            "replace all is exactly one undo step (DeepCopy round-trips values as JSON)");
    }

    [Fact]
    public void WritableEditor_Replace_PlainQueryWithRegexChars_ReplacesLiterally()
    {
        var doc = CreateDocument();
        var dot = new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 100, H = 50 };
        dot.Data["text"] = "a.b";
        var cross = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 10, W = 100, H = 50 };
        cross.Data["text"] = "axb";
        doc.Nodes.Add(dot);
        doc.Nodes.Add(cross);

        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "OpenSearchPanel");
        InvokePrivate(cut.Instance, "OnSearchQueryChanged", "a.b");
        InvokePrivate(cut.Instance, "OnReplaceQueryChanged", "x");
        InvokePrivate(cut.Instance, "OnReplaceAll");

        dot.Data["text"].Should().Be("x", "a plain query with regex metacharacters matches literally");
        cross.Data["text"].Should().Be("axb", "a plain query must not act as a wildcard");
    }

    [Fact]
    public void EffectiveReadOnly_RulerUnitSelect_IsDisabledAndIgnored()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        var select = cut.Find(".tm-diagram-editor__unit-select");
        select.HasAttribute("disabled").Should().BeTrue(
            "the ruler unit is a serialized document property (A5)");

        InvokePrivate(cut.Instance, "OnRulerUnitChanged", MeasurementUnit.Mm);
        doc.ActivePage.RulerUnit.Should().Be(MeasurementUnit.Px,
            "the read-only guard ignores the change even if the event fires");
    }

    [Fact]
    public void WritableEditor_RulerUnitChange_RaisesDocumentChanged()
    {
        var doc = CreateDocument();
        var changes = 0;
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false)
            .Add(e => e.DocumentChanged, _ => changes++));

        cut.Find(".tm-diagram-editor__unit-select").Change("Mm");

        doc.ActivePage.RulerUnit.Should().Be(MeasurementUnit.Mm);
        changes.Should().BeGreaterThanOrEqualTo(1, "a serialized document property notifies the host (A5)");
    }

    [Fact]
    public void EffectiveReadOnly_TemplatesButton_IsDisabled()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='templates'] button").HasAttribute("disabled").Should().BeFalse(
            "precondition: the templates button is enabled while writable");

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.Find("[data-tm-group='templates'] button").HasAttribute("disabled")
            .Should().BeTrue("the templates gallery is an editing surface");
    }

    [Fact]
    public void EffectiveReadOnly_LayoutDropdown_IsDisabled()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        // The TmDropdown disabled contract is twofold: the wrapper class AND the native
        // `disabled` attribute on the trigger button (so the browser blocks the click).
        var layoutTrigger = cut.Find("[data-tm-group='layout'] .tm-dropdown-trigger");
        layoutTrigger.HasAttribute("disabled").Should().BeTrue("the layout menu changes the document");
        layoutTrigger.GetAttribute("aria-expanded").Should().BeNull(
            "an expanded state is meaningless on a disabled trigger");
        cut.Find("[data-tm-group='layout'] .tm-dropdown-wrapper").ClassList
            .Should().Contain("tm-dropdown-disabled");
    }

    [Fact]
    public void EffectiveReadOnly_InsertAndImportDropdowns_AreDisabled()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.Find("[data-tm-group='insert'] .tm-dropdown-trigger").HasAttribute("disabled")
            .Should().BeTrue("inserting shapes is an editing path");
        cut.Find("[data-tm-group='import'] .tm-dropdown-trigger").HasAttribute("disabled")
            .Should().BeTrue("importing replaces the document");
    }

    [Fact]
    public void EffectiveReadOnly_CanvasSizeButton_IsDisabled()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.Find("[data-tm-group='canvas-size'] button").HasAttribute("disabled")
            .Should().BeTrue("the canvas size editor changes the serialized page size");
    }

    [Fact]
    public void ReadOnlyToggle_ClosesAnOpenContextMenu()
    {
        var doc = CreateDocument();
        var node = new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 100, H = 50 };
        doc.Nodes.Add(node);
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "OnCanvasContextMenu", (10d, 10d, 10d, 10d, "mouse"));
        cut.FindAll(".tm-diagram-editor__context-menu").Should().HaveCount(1,
            "precondition: the context menu opened");

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.FindAll(".tm-diagram-editor__context-menu").Should().BeEmpty(
            "entering read-only closes surfaces that only make sense while editing");
    }

    [Fact]
    public void HostReadOnlyFlip_ClosesTheCanvasSizeEditor()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "OpenCanvasSize");
        cut.Render();
        cut.FindAll(".tm-diagram-editor__canvas-size-input").Should().NotBeEmpty(
            "precondition: the canvas size editor opened");

        cut.Render(p => p.Add(e => e.ReadOnly, true));

        cut.FindAll(".tm-diagram-editor__canvas-size-input").Should().BeEmpty(
            "a host flip to read-only closes the edit-only surfaces like the user toggle");
    }

    [Fact]
    public void WritableEditor_TemplatesAndLayout_AreEnabled()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='templates'] button").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-tm-group='layout'] .tm-dropdown-wrapper").ClassList
            .Should().NotContain("tm-dropdown-disabled");
        cut.Find("[data-tm-group='canvas-size'] button").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void HostReadOnly_ToolboxTilesAreAriaDisabled()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, true));

        cut.Find("[data-stencil-id='general.rectangle']").GetAttribute("aria-disabled")
            .Should().Be("true", "A8: read-only tiles stay visible and discoverable but inert");
    }

    [Fact]
    public void EffectiveReadOnly_EdgeTileDrag_DoesNotArmTheEdgeTool()
    {
        var edgeStencilId = Services.GetRequiredService<DiagramStencilRegistry>()
            .GetAll().First(s => s.Kind == Tempo.Blazor.Components.Diagram.Models.DiagramStencilKind.Edge).Id;
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        cut.Find("[data-tm-group='readonly'] button").Click();

        InvokePrivate(cut.Instance, "OnToolboxDragStart", edgeStencilId);

        GetPrivateField<string>(cut.Instance, "_toolMode").Should().Be("select",
            "arming the edge tool is an editing path");
    }

    // ── R1 review: the Select tool stays reachable in read-only ───────────────

    [Fact]
    public void EffectiveReadOnly_SelectTool_IsEnabled()
    {
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var selectButton = cut.Find("[data-tm-group='tools'] button");
        selectButton.HasAttribute("disabled").Should().BeFalse(
            "precondition: the Select tool is enabled while writable");

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.Find("[data-tm-group='tools'] button").HasAttribute("disabled").Should().BeFalse(
            "R1: selecting a node for inspection is not an editing path — the tool stays enabled");
    }

    [Fact]
    public void EffectiveReadOnly_EdgeToolActive_ResetsToSelect()
    {
        var edgeStencilId = Services.GetRequiredService<DiagramStencilRegistry>()
            .GetAll().First(s => s.Kind == Tempo.Blazor.Components.Diagram.Models.DiagramStencilKind.Edge).Id;
        var doc = CreateDocument();
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "OnToolboxDragStart", edgeStencilId);
        GetPrivateField<string>(cut.Instance, "_toolMode").Should().Be("edge",
            "precondition: the edge draw tool is armed");

        cut.Find("[data-tm-group='readonly'] button").Click();

        GetPrivateField<string>(cut.Instance, "_toolMode").Should().Be("select",
            "an armed click-to-connect must not survive the read-only flip");
    }

    // ── Review: the replacement text is literal in non-regex mode ─────────────

    [Fact]
    public void WritableEditor_Replace_DollarInLiteralReplacement_ReplacesLiterally()
    {
        var doc = CreateDocument();
        // A node whose id and stencil id contain no "a": the search yields the Data match for
        // "a" (an Id/StencilId match is not replaceable and would swallow the whole query).
        var node = new DiagramNode { Id = "n1", StencilId = "uml.component", X = 10, Y = 10, W = 100, H = 50 };
        node.Data["text"] = "a";
        doc.Nodes.Add(node);

        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        InvokePrivate(cut.Instance, "OpenSearchPanel");
        InvokePrivate(cut.Instance, "OnSearchQueryChanged", "a");
        InvokePrivate(cut.Instance, "OnReplaceQueryChanged", "$0x");
        InvokePrivate(cut.Instance, "OnReplaceAll");

        node.Data["text"].Should().Be("$0x",
            "$ in a plain (non-regex) replacement is literal text, not a group reference");
    }

    // ── Review: entering read-only closes the connect dialog too ──────────────

    [Fact]
    public async Task ReadOnlyToggle_ClosesAnOpenConnectDialog()
    {
        var doc = CreateDocument();
        var node = new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 100, H = 50 };
        doc.Nodes.Add(node);
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        await cut.InvokeAsync(() => InvokePrivate(cut.Instance, "OnConnectArrowClicked", (node.Id, "right")));
        cut.FindAll(".tm-diagram-connect-modal").Should().NotBeEmpty(
            "precondition: the connect dialog opened");

        cut.Find("[data-tm-group='readonly'] button").Click();

        cut.FindAll(".tm-diagram-connect-modal").Should().BeEmpty(
            "entering read-only closes the connect dialog");
    }

    private static T? GetPrivateField<T>(TmDiagramEditor editor, string fieldName)
        => (T?)typeof(TmDiagramEditor)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(editor);

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

    /// <summary>DI2: the layers panel renders behind the inspector's Layers tab — select it first.</summary>
    private static void OpenLayersTab(IRenderedComponent<TmDiagramEditor> cut)
    {
        var layersTab = cut.FindAll(".tm-diagram-editor__inspector [role='tab']")
            .FirstOrDefault(t => t.TextContent.Contains("Layers"));
        layersTab.Should().NotBeNull("the inspector must offer the Layers tab");
        layersTab!.Click();
    }

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
