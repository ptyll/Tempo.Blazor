using FluentAssertions;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

public class RenameElementCommandTests
{
    private static (WireframeDocument Doc, WireframeElement El) Make(string? name = null)
    {
        var doc = new WireframeDocument { Title = "T" };
        var el = new WireframeElement { Type = "TmButton", W = 100, H = 30, Name = name };
        doc.Elements.Add(el);
        return (doc, el);
    }

    [Fact]
    public void Execute_SetsName_Undo_RestoresOld()
    {
        var (doc, el) = Make("Old");
        var cmd = new RenameElementCommand(doc, el.Id, "Old", "Login button");

        cmd.Execute();
        el.Name.Should().Be("Login button");

        cmd.Undo();
        el.Name.Should().Be("Old");
    }

    [Fact]
    public void Undo_RestoresNull_WhenWasUnnamed()
    {
        var (doc, el) = Make();
        var cmd = new RenameElementCommand(doc, el.Id, null, "Named");

        cmd.Execute();
        cmd.Undo();

        el.Name.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Execute_BlankNewName_ClearsName(string? blank)
    {
        var (doc, el) = Make("Named");

        new RenameElementCommand(doc, el.Id, "Named", blank).Execute();

        el.Name.Should().BeNull();
    }

    [Fact]
    public void Execute_UnknownElement_IsNoOp()
    {
        var (doc, el) = Make("Keep");

        var act = () => new RenameElementCommand(doc, "nope", "Keep", "X").Execute();

        act.Should().NotThrow();
        el.Name.Should().Be("Keep");
    }

    [Fact]
    public void Name_IsRenameElement() =>
        new RenameElementCommand(Make().Doc, "x", null, "y").Name.Should().Be("Rename element");

    [Fact]
    public void WorksThroughCommandStack_UndoRedo()
    {
        var (doc, el) = Make();
        var stack = new WireframeCommandStack();

        stack.Push(new RenameElementCommand(doc, el.Id, null, "Hero"));
        el.Name.Should().Be("Hero");
        stack.Undo();
        el.Name.Should().BeNull();
        stack.Redo();
        el.Name.Should().Be("Hero");
    }
}
