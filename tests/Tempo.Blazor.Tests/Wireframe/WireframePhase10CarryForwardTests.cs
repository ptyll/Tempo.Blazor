using FluentAssertions;
using Xunit;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>Phase 10 clean-up of the phase 5 / 7 review carry-forwards (wireframe redesign plan ce19aa8c).</summary>
public class WireframePhase10CarryForwardTests
{
    [Fact]
    public void AddElementsCommand_Undo_RemovesOnlyTheElementsItInserted()
    {
        var doc = new WireframeDocument();
        var existing = WireframeDocumentExtensions.NewElement("TmButton", 10, 10);
        doc.Elements.Add(existing);
        var fresh = WireframeDocumentExtensions.NewElement("TmButton", 50, 50);
        // The same id as an element already in the document: Execute skips it, so Undo must leave the original alone.
        var clash = WireframeDocumentExtensions.NewElement("TmButton", 90, 90);
        clash.Id = existing.Id;

        var command = new AddElementsCommand(doc, [fresh, clash]);
        command.Execute();
        doc.Elements.Should().HaveCount(2);

        command.Undo();

        doc.Elements.Should().ContainSingle().Which.Id.Should().Be(existing.Id);
    }

    [Fact]
    public void AddElementsCommand_ExecuteUndoRedo_IsStable()
    {
        var doc = new WireframeDocument();
        var a = WireframeDocumentExtensions.NewElement("TmButton", 10, 10);
        var b = WireframeDocumentExtensions.NewElement("TmButton", 20, 20);
        var command = new AddElementsCommand(doc, [a, b]);

        command.Execute();
        command.Undo();
        doc.Elements.Should().BeEmpty();
        command.Execute();
        doc.Elements.Should().HaveCount(2);
    }
}

