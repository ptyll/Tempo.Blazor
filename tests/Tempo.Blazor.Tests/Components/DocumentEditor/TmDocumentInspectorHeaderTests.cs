using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Phase 2 / DE-2.1: the shared inspector panel header (icon, title, subtitle, close, actions slot).</summary>
public sealed class TmDocumentInspectorHeaderTests : LocalizationTestBase
{
    [Fact]
    public void Title_IsAHeadingWithAnIdTheRegionCanLabelItselfBy()
    {
        var cut = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Image tools")
            .Add(p => p.TitleId, "image-inspector-title"));

        var heading = cut.Find("h2");
        heading.TextContent.Should().Be("Image tools");
        heading.Id.Should().Be("image-inspector-title");
        heading.ClassList.Should().Contain("tm-document-inspector-header__title");
    }

    [Fact]
    public void Title_WithoutExplicitId_GetsAStableUniqueId()
    {
        var first = Render<TmDocumentInspectorHeader>(parameters => parameters.Add(p => p.Title, "A"));
        var second = Render<TmDocumentInspectorHeader>(parameters => parameters.Add(p => p.Title, "B"));

        first.Find("h2").Id.Should().NotBeNullOrWhiteSpace();
        first.Find("h2").Id.Should().NotBe(second.Find("h2").Id);
        first.Instance.ResolvedTitleId.Should().Be(first.Find("h2").Id, "the host reads the id back for aria-labelledby");
    }

    [Fact]
    public void HeadingLevel_IsConfigurable()
    {
        var cut = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Versions")
            .Add(p => p.HeadingLevel, 3));

        cut.FindAll("h3").Should().ContainSingle();
        cut.FindAll("h2").Should().BeEmpty();
    }

    [Fact]
    public void CloseButton_HasLocalizedAriaLabel_AndRaisesOnClose()
    {
        var closed = 0;
        var cut = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Find")
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closed++)));

        var close = cut.Find("button.tm-document-inspector-header__close");
        close.GetAttribute("aria-label").Should().Be("Close");
        close.GetAttribute("type").Should().Be("button");
        close.Click();

        closed.Should().Be(1);
    }

    [Fact]
    public void CloseButton_IsNotRendered_WithoutAHandler()
    {
        var cut = Render<TmDocumentInspectorHeader>(parameters => parameters.Add(p => p.Title, "Find"));

        cut.FindAll("button.tm-document-inspector-header__close").Should().BeEmpty();
    }

    [Fact]
    public void CloseButton_CarriesTheScopedTestId()
    {
        var cut = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Find")
            .Add(p => p.CloseTestId, "find-close")
            .Add(p => p.TestIdPrefix, "ed")
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => { })));

        cut.Find("button.tm-document-inspector-header__close").GetAttribute("data-testid").Should().Be("ed-find-close");
    }

    [Fact]
    public void ActionsSlot_RendersBeforeTheCloseButton()
    {
        var cut = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Revisions")
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.Actions, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddAttribute(2, "data-testid", "header-action");
                builder.AddContent(3, "Filter");
                builder.CloseElement();
            })));

        var actions = cut.Find(".tm-document-inspector-header__actions");
        actions.QuerySelector("[data-testid='header-action']").Should().NotBeNull();
        cut.Markup.IndexOf("header-action", StringComparison.Ordinal)
            .Should().BeLessThan(cut.Markup.IndexOf("tm-document-inspector-header__close", StringComparison.Ordinal));
    }

    [Fact]
    public void Icon_IsOptionalAndDecorative()
    {
        var without = Render<TmDocumentInspectorHeader>(parameters => parameters.Add(p => p.Title, "Plain"));
        without.FindAll(".tm-document-inspector-header__icon").Should().BeEmpty();

        var with = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Image")
            .Add(p => p.Icon, "image"));
        var icon = with.Find(".tm-document-inspector-header__icon");
        icon.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Subtitle_RendersOnlyWhenProvided()
    {
        var without = Render<TmDocumentInspectorHeader>(parameters => parameters.Add(p => p.Title, "Plain"));
        without.FindAll(".tm-document-inspector-header__subtitle").Should().BeEmpty();

        var with = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Versions")
            .Add(p => p.Subtitle, "3 versions"));
        with.Find(".tm-document-inspector-header__subtitle").TextContent.Should().Be("3 versions");
    }
    [Fact]
    public void BackArrow_RendersOnlyWithAHandler_BeforeTheTitle_AndRaisesIt()
    {
        var without = Render<TmDocumentInspectorHeader>(parameters => parameters.Add(p => p.Title, "Plain"));
        without.FindAll(".tm-document-inspector-header__back").Should().BeEmpty();

        var raised = 0;
        var with = Render<TmDocumentInspectorHeader>(parameters => parameters
            .Add(p => p.Title, "Image tools")
            .Add(p => p.BackTestId, "image-back")
            .Add(p => p.OnBack, () => raised++));

        var back = with.Find(".tm-document-inspector-header__back");
        back.GetAttribute("aria-label").Should().Be("Back to tabs");
        back.GetAttribute("data-testid").Should().Be("image-back");
        back.QuerySelector(".tm-icon").Should().NotBeNull();
        with.Find(".tm-document-inspector-header").FirstElementChild!.ClassList.Should().Contain("tm-document-inspector-header__back", "the arrow leads the header");

        back.Click();
        raised.Should().Be(1);
    }
}
