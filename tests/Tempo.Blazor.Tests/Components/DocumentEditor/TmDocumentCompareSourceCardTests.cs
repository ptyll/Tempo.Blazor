using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Redesign 146c7e1b, phase 7 (DE-2.6): one compare source as a card - title, what it is, "Change..." and the picker under it.</summary>
public class TmDocumentCompareSourceCardTests : LocalizationTestBase
{
    [Fact]
    public void Card_ShowsTitleLabelAndDescription_NamedByItsTitle()
    {
        var cut = Render<TmDocumentCompareSourceCard>(p => p
            .Add(c => c.Side, "target")
            .Add(c => c.Title, "New document")
            .Add(c => c.Label, "Návrh projektu_v2.docx")
            .Add(c => c.Description, "Uploaded DOCX"));

        var root = cut.Find("[data-testid='document-compare-target-card']");
        root.GetAttribute("role").Should().Be("group");
        root.GetAttribute("data-side").Should().Be("target");
        var title = root.QuerySelector("h4")!;
        root.GetAttribute("aria-labelledby").Should().Be(title.Id);
        cut.Find("[data-testid='document-compare-target-label']").TextContent.Should().Be("Návrh projektu_v2.docx");
        cut.Find(".tm-document-compare-source__description").TextContent.Should().Be("Uploaded DOCX");
    }

    [Fact]
    public void ChangeButton_RaisesOnChange_ReflectsExpanded_AndNamesTheSource()
    {
        var raised = 0;
        var cut = Render<TmDocumentCompareSourceCard>(p => p
            .Add(c => c.Side, "base")
            .Add(c => c.Title, "Original document")
            .Add(c => c.OnChange, EventCallback.Factory.Create(this, () => raised++)));

        var button = cut.Find("[data-testid='document-compare-base-change']");
        button.GetAttribute("aria-expanded").Should().Be("false");
        button.GetAttribute("aria-label").Should().Be("Change source: Original document");
        button.Click();

        raised.Should().Be(1);
        cut.FindAll("[data-testid='document-compare-base-editor']").Should().BeEmpty();
    }

    [Fact]
    public void Picker_RendersUnderTheBody_OnlyWhileExpanded()
    {
        var cut = Render<TmDocumentCompareSourceCard>(p => p
            .Add(c => c.Side, "base")
            .Add(c => c.Expanded, true)
            .Add(c => c.ChildContent, "<span data-testid='picker'>picker</span>"));

        cut.Find("[data-testid='document-compare-base-editor'] [data-testid='picker']").Should().NotBeNull();
        cut.Find("[data-testid='document-compare-base-change']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='document-compare-base-change']").GetAttribute("aria-controls")
            .Should().Be(cut.Find("[data-testid='document-compare-base-editor']").Id);
    }

    [Fact]
    public void Disabled_DisablesTheChangeButton()
    {
        var cut = Render<TmDocumentCompareSourceCard>(p => p.Add(c => c.Disabled, true));

        cut.Find("[data-testid='document-compare-base-change']").HasAttribute("disabled").Should().BeTrue();
    }
}
