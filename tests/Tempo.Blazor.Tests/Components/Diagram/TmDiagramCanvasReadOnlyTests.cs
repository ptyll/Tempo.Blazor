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
}
