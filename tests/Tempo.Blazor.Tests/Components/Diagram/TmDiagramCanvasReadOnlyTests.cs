using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Read-only guard rails for <see cref="TmDiagramCanvas"/>: the JS canvas instance must be
/// updated on every read-only flip (not only at init), undo/redo arriving from the JS layer
/// must not touch the command stack while read-only, and a drag that starts into a read-only
/// flip must never latch <c>_isJsDragging</c> (a latched flag disables <c>ShouldRender</c>
/// and freezes the canvas for the rest of the session).
/// </summary>
public class TmDiagramCanvasReadOnlyTests : LocalizationTestBase
{
    public TmDiagramCanvasReadOnlyTests()
    {
        Services.GetRequiredService<DiagramStencilRegistry>()
            .RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public void ReadOnly_True_JsOnUndo_DoesNotTouchTheCommandStack()
    {
        var doc = new DiagramDocument();
        var node = NewNode();
        var stack = new DiagramCommandStack();
        stack.Push(new AddNodeCommand(doc, node));

        var cut = RenderCanvas(doc, stack, readOnly: true);
        var canvas = cut.Instance;

        canvas.JsOnUndo().GetAwaiter().GetResult();

        doc.Nodes.Should().ContainSingle(n => n.Id == node.Id,
            "undo arriving from the JS layer must not run while read-only");
        stack.CanUndo.Should().BeTrue("the undo entry must still be on the stack");
        stack.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void ReadOnly_True_JsOnRedo_DoesNotTouchTheCommandStack()
    {
        var doc = new DiagramDocument();
        var node = NewNode();
        var stack = new DiagramCommandStack();
        stack.Push(new AddNodeCommand(doc, node));
        stack.Undo();

        var cut = RenderCanvas(doc, stack, readOnly: true);
        var canvas = cut.Instance;

        canvas.JsOnRedo().GetAwaiter().GetResult();

        doc.Nodes.Should().BeEmpty("redo arriving from the JS layer must not run while read-only");
        stack.CanRedo.Should().BeTrue("the redo entry must still be on the stack");
    }

    [Fact]
    public void ReadOnly_False_JsOnUndo_StillUndoesTheCommandStack()
    {
        // Control test: the harness above must actually undo when writable.
        var doc = new DiagramDocument();
        var node = NewNode();
        var stack = new DiagramCommandStack();
        stack.Push(new AddNodeCommand(doc, node));

        var cut = RenderCanvas(doc, stack, readOnly: false);
        var canvas = cut.Instance;

        canvas.JsOnUndo().GetAwaiter().GetResult();

        doc.Nodes.Should().BeEmpty();
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void ReadOnly_True_OnDragStarted_DoesNotLatchJsDragging()
    {
        var doc = new DiagramDocument();
        var node = NewNode();
        doc.Nodes.Add(node);

        using var host = Render<CascadingValue<DiagramCommandStack>>(p => p
            .Add(c => c.Value, new DiagramCommandStack())
            .AddChildContent<TmDiagramCanvas>(child => child
                .Add(c => c.Document, doc)
                .Add(c => c.ReadOnly, true)));
        var cut = host.FindComponent<TmDiagramCanvas>();

        cut.Instance.OnDragStarted([node.Id]);

        GetJsDragging(cut.Instance).Should().BeFalse(
            "a drag reported by JS in read-only mode must not latch ShouldRender() off");

        // A move that slips through (e.g. from an arrow nudge) must also release the latch.
        cut.Instance.OnElementMoved(node.Id, 500, 500).GetAwaiter().GetResult();
        GetJsDragging(cut.Instance).Should().BeFalse(
            "a rejected move must release the JS-drag latch so the canvas keeps rendering");
        node.X.Should().Be(NewNodeDefaultX, "a read-only move must not mutate the document");
    }

    [Fact]
    public void ReadOnly_ChangeAfterInit_PushesSetReadOnlyToTheJsInstance()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(NewNode());

        using var host = Render<CascadingValue<DiagramCommandStack>>(p => p
            .Add(c => c.Value, new DiagramCommandStack())
            .AddChildContent<TmDiagramCanvas>(child => child
                .Add(c => c.Document, doc)
                .Add(c => c.ReadOnly, false)));
        var cut = host.FindComponent<TmDiagramCanvas>();

        var setReadOnly = JSInterop.SetupVoid("tmDiagramEditor.setReadOnly", _ => true).SetVoidResult();

        cut.Render(p => p.Add(c => c.ReadOnly, true));

        setReadOnly.VerifyInvoke("tmDiagramEditor.setReadOnly", 1);
        setReadOnly.Invocations["tmDiagramEditor.setReadOnly"].Single().Arguments[1].Should().Be(true);

        cut.Render(p => p.Add(c => c.ReadOnly, false));

        setReadOnly.VerifyInvoke("tmDiagramEditor.setReadOnly", 2);
        setReadOnly.Invocations["tmDiagramEditor.setReadOnly"].Last().Arguments[1].Should().Be(false);
    }

    [Fact]
    public void ReadOnly_UnchangedAfterInit_DoesNotCallSetReadOnly()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(NewNode());

        using var host = Render<CascadingValue<DiagramCommandStack>>(p => p
            .Add(c => c.Value, new DiagramCommandStack())
            .AddChildContent<TmDiagramCanvas>(child => child
                .Add(c => c.Document, doc)
                .Add(c => c.ReadOnly, true)));
        var cut = host.FindComponent<TmDiagramCanvas>();

        var setReadOnly = JSInterop.SetupVoid("tmDiagramEditor.setReadOnly", _ => true).SetVoidResult();

        cut.Render(p => p.Add(c => c.Document, doc));

        setReadOnly.Invocations["tmDiagramEditor.setReadOnly"].Should().BeEmpty(
            "re-renders without a read-only flip must not re-send the JS state");
    }

    // ── Phase 1 (read-only mid-gesture): F12 / J1b ────────────────────────────

    [Fact]
    public async Task ReadOnlyFlip_DuringTouchResize_RollsGeometryBack_AndCommitsNothing()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 };
        doc.Nodes.Add(node);
        var changes = 0;
        // GridSize 0: the editor snaps document geometry to the 8px grid on attach (banker's
        // rounding), which would move the literal start rect this test pins.
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.GridSize, 0)
            .Add(e => e.DocumentChanged, _ => changes++));
        var canvas = cut.FindComponent<TmDiagramCanvas>();

        await cut.InvokeAsync(() => canvas.Instance.OnResizeStarted("n1", "se", 0, 0));
        await cut.InvokeAsync(() => canvas.Instance.OnResizeMoved(40, 30));
        node.W.Should().BeGreaterThan(120, "precondition: the live resize moved the geometry");

        var before = changes;
        cut.Find("[data-tm-group='readonly'] button").Click();

        node.W.Should().Be(120, "the read-only flip rolls the half-applied resize back");
        node.H.Should().Be(60);
        await cut.InvokeAsync(() => canvas.Instance.OnResizeMoved(80, 60));
        await cut.InvokeAsync(() => canvas.Instance.OnResizeEnded());
        node.W.Should().Be(120, "a gesture that outlived edit mode never commits");
        changes.Should().Be(before, "no DocumentChanged may fire from the cancelled gesture");
        GetActiveCommandStack(cut.Instance).CanUndo.Should().BeFalse(
            "the rolled-back gesture pushed nothing onto the undo stack");
    }

    [Fact]
    public async Task HostReadOnlyFlip_DuringResize_RollsBack()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 };
        doc.Nodes.Add(node);
        var changes = 0;
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.GridSize, 0)
            .Add(e => e.DocumentChanged, _ => changes++));
        var canvas = cut.FindComponent<TmDiagramCanvas>();

        await cut.InvokeAsync(() => canvas.Instance.OnResizeStarted("n1", "se", 0, 0));
        await cut.InvokeAsync(() => canvas.Instance.OnResizeMoved(40, 30));
        node.W.Should().BeGreaterThan(120, "precondition: the live resize moved the geometry");

        // The HOST flips ReadOnly while the gesture is in flight.
        var before = changes;
        cut.Render(p => p.Add(e => e.ReadOnly, true));

        node.W.Should().Be(120, "the host flip rolls the half-applied resize back");
        node.H.Should().Be(60);
        await cut.InvokeAsync(() => canvas.Instance.OnResizeMoved(80, 60));
        await cut.InvokeAsync(() => canvas.Instance.OnResizeEnded());
        node.W.Should().Be(120, "a gesture that outlived edit mode never commits");
        node.H.Should().Be(60);
        changes.Should().Be(before, "no DocumentChanged may fire from the cancelled gesture");
        GetActiveCommandStack(cut.Instance).CanUndo.Should().BeFalse(
            "the rolled-back gesture pushed nothing onto the undo stack");
    }

    [Fact]
    public async Task WritableResize_StillCommitsOneUndoStep()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 };
        doc.Nodes.Add(node);
        var changes = 0;
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.GridSize, 0)
            .Add(e => e.DocumentChanged, _ => changes++));
        var canvas = cut.FindComponent<TmDiagramCanvas>();

        await cut.InvokeAsync(() => canvas.Instance.OnResizeStarted("n1", "se", 0, 0));
        await cut.InvokeAsync(() => canvas.Instance.OnResizeMoved(40, 30));
        await cut.InvokeAsync(() => canvas.Instance.OnResizeEnded());

        node.W.Should().BeGreaterThan(120, "the writable resize commits");
        changes.Should().BeGreaterThanOrEqualTo(1, "the host learns about the resize");
        var stack = GetActiveCommandStack(cut.Instance);
        stack.CanUndo.Should().BeTrue("a committed resize is undoable");
        stack.Undo();
        node.W.Should().Be(120, "one undo restores the start geometry");
        node.H.Should().Be(60);
    }

    // ── Review B: the read-only flip reaches JS even while a drag latches the render ──

    [Fact]
    public void ReadOnlyFlip_WhileJsDragLatched_PushesSetReadOnlyFromOnParametersSet()
    {
        var doc = new DiagramDocument();
        var node = NewNode();
        doc.Nodes.Add(node);

        var cut = RenderCanvas(doc, new DiagramCommandStack(), readOnly: false);

        // Latch the JS drag: OnAfterRenderAsync is gated off while _isJsDragging is set, so the
        // push must happen in OnParametersSet — invoked exactly once, in the same pass.
        cut.Instance.OnDragStarted([node.Id]);
        GetJsDragging(cut.Instance).Should().BeTrue("precondition: the JS drag latched the render gate");

        var setReadOnly = JSInterop.SetupVoid("tmDiagramEditor.setReadOnly", _ => true).SetVoidResult();
        cut.Render(p => p.Add(c => c.ReadOnly, true));

        setReadOnly.VerifyInvoke("tmDiagramEditor.setReadOnly", 1);
        setReadOnly.Invocations["tmDiagramEditor.setReadOnly"].Single().Arguments[1].Should().Be(true);
    }

    // ── Review E: a selected edge in read-only keeps its outline, loses its edit affordances ──

    [Fact]
    public void EffectiveReadOnly_SelectedEdge_ShowsOutlineWithoutHandlesOrToolbar()
    {
        var doc = new DiagramDocument();
        var a = NewNode();
        var b = new DiagramNode { StencilId = "general.rectangle", X = 400, Y = 300, W = 120, H = 60 };
        doc.Nodes.Add(a);
        doc.Nodes.Add(b);
        var edge = new DiagramEdge { SourceNodeId = a.Id, TargetNodeId = b.Id };
        doc.Edges.Add(edge);

        var cut = RenderCanvas(doc, new DiagramCommandStack(), readOnly: true);
        cut.InvokeAsync(() => cut.Instance.SetSelection(edge.Id)).GetAwaiter().GetResult();
        cut.Render();

        cut.FindAll(".tm-diagram-edge-path--selected-outline").Should().ContainSingle(
            "the outline is the read-only selection cue (the edge stays inspectable)");
        cut.FindAll(".tm-diagram-edge-handle").Should().BeEmpty(
            "waypoint/segment handles are reshape affordances");
        cut.FindAll(".tm-diagram-edge-toolbar").Should().BeEmpty(
            "the inline edge toolbar mutates routing and shape");
    }

    // ── Review C: open inline edits never survive (or commit in) read-only ────

    [Fact]
    public async Task ReadOnlyFlip_DuringEdgeLabelEdit_CommitsNothing()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(NewNode());
        var edge = new DiagramEdge { SourceNodeId = null, TargetNodeId = null, Label = "before" };
        doc.Edges.Add(edge);

        var cut = RenderCanvas(doc, new DiagramCommandStack(), readOnly: false);
        await cut.InvokeAsync(() => cut.Instance.StartEdgeLabelEdit(edge.Id));
        GetPrivateField<string?>(cut.Instance, "_editingEdgeLabelId").Should().Be(edge.Id,
            "precondition: the edge label editor is open");

        cut.Render(p => p.Add(c => c.ReadOnly, true));

        GetPrivateField<string?>(cut.Instance, "_editingEdgeLabelId").Should().BeNull(
            "the flip cancels the open edge label editor");
        GetPrivateField<string>(cut.Instance, "_editingLabelValue").Should().BeEmpty();
        edge.Label.Should().Be("before", "the flip must not commit the edited label");

        // A blur that still arrives commits nothing.
        InvokePrivateTask(cut.Instance, "OnLabelInputBlur").GetAwaiter().GetResult();
        edge.Label.Should().Be("before", "SaveEdgeLabel is a no-op while read-only");
    }

    [Fact]
    public void EffectiveReadOnly_SectionDoubleClick_DoesNotEnterInlineEdit()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new Uml25DiagramStencilProvider());
        var stencil = registry.GetStencil("uml25.class")!;
        var node = new DiagramNode { StencilId = stencil.Id, W = stencil.DefaultWidth, H = stencil.DefaultHeight };
        foreach (var item in stencil.DefaultData)
            node.Data[item.Key] = item.Value;

        var cut = Render<TmDiagramStencilShape>(parameters => parameters
            .Add(p => p.Node, node)
            .Add(p => p.ReadOnly, true));

        cut.FindAll(".tm-diagram-node__section")[0].DoubleClick();

        cut.FindAll(".tm-diagram-node__inline-input").Should().BeEmpty(
            "a section double-click is an editing path — read-only never opens the inline editor");
    }

    [Fact]
    public async Task EffectiveReadOnly_HandleSectionEdit_IsIgnored()
    {
        var doc = new DiagramDocument();
        var node = new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 10, Y = 10, W = 100, H = 50 };
        node.Data["text"] = "keep";
        doc.Nodes.Add(node);
        var cut = RenderCanvas(doc, new DiagramCommandStack(), readOnly: true);

        await cut.InvokeAsync(() => InvokePrivateHandleSectionEdit(cut.Instance, "n1", "text", "changed"));

        node.Data["text"].Should().Be("keep", "HandleSectionEdit must not run while read-only");
    }

    [Fact]
    public async Task OnDragCancelled_AfterOnDragStarted_ResumesRendering()
    {
        var doc = new DiagramDocument();
        var node = NewNode();
        doc.Nodes.Add(node);

        var cut = RenderCanvas(doc, new DiagramCommandStack(), readOnly: false);

        cut.Instance.OnDragStarted([node.Id]);
        GetJsDragging(cut.Instance).Should().BeTrue("precondition: OnDragStarted latched the render gate");

        await cut.InvokeAsync(() => cut.Instance.OnDragCancelled());
        GetJsDragging(cut.Instance).Should().BeFalse("the cancelled drag releases ShouldRender()");

        // A content change after the cancellation must actually reach the markup again.
        var late = NewNode();
        late.X = 500;
        doc.Nodes.Add(late);
        cut.Render();
        cut.FindAll($"[data-node-id='{late.Id}']").Should().NotBeEmpty(
            "the canvas must not stay frozen after a cancelled drag (J1b)");
    }

    [Fact]
    public async Task OnDragCancelled_WithoutAnActiveDrag_IsANoOp()
    {
        var doc = new DiagramDocument();
        doc.Nodes.Add(NewNode());
        var cut = RenderCanvas(doc, new DiagramCommandStack(), readOnly: false);

        await cut.InvokeAsync(() => cut.Instance.OnDragCancelled());

        GetJsDragging(cut.Instance).Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const double NewNodeDefaultX = 100;

    private static DiagramNode NewNode() => new()
    {
        StencilId = "general.rectangle",
        X = NewNodeDefaultX, Y = 100, W = 120, H = 60,
    };

    private IRenderedComponent<TmDiagramCanvas> RenderCanvas(
        DiagramDocument doc, DiagramCommandStack stack, bool readOnly)
    {
        // No `using` on the host on purpose: the returned canvas cut must stay
        // alive for the caller's assertions (BunitContext disposes the tree).
        var host = Render<CascadingValue<DiagramCommandStack>>(p => p
            .Add(c => c.Value, stack)
            .AddChildContent<TmDiagramCanvas>(child => child
                .Add(c => c.Document, doc)
                .Add(c => c.ReadOnly, readOnly)));
        return host.FindComponent<TmDiagramCanvas>();
    }

    private static bool GetJsDragging(TmDiagramCanvas canvas)
        => (bool)(typeof(TmDiagramCanvas)
            .GetField("_isJsDragging", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_isJsDragging must exist"))
            .GetValue(canvas)!;

    private static T? GetPrivateField<T>(TmDiagramCanvas canvas, string fieldName)
        => (T?)typeof(TmDiagramCanvas)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(canvas);

    private static async Task InvokePrivateHandleSectionEdit(TmDiagramCanvas canvas, string nodeId, string dataKey, object value)
    {
        var method = typeof(TmDiagramCanvas).GetMethod(
            "HandleSectionEdit",
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [typeof(string), typeof(string), typeof(object)],
            modifiers: null) ?? throw new InvalidOperationException("HandleSectionEdit must exist");
        await (Task)method.Invoke(canvas, [nodeId, dataKey, value])!;
    }

    private static Task InvokePrivateTask(TmDiagramCanvas canvas, string methodName)
    {
        var method = typeof(TmDiagramCanvas).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [],
            modifiers: null) ?? throw new InvalidOperationException($"{methodName} must exist");
        return (Task)method.Invoke(canvas, [])!;
    }

    /// <summary>The active page command stack of the editor harness (mirrors the editor RO tests).</summary>
    private static DiagramCommandStack GetActiveCommandStack(TmDiagramEditor editor)
        => (typeof(TmDiagramEditor)
            .GetProperty("ActiveCommandStack", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ActiveCommandStack must exist"))
            .GetValue(editor) as DiagramCommandStack
            ?? throw new InvalidOperationException("ActiveCommandStack must return a DiagramCommandStack");
}
