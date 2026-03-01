using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

public class TmModalTests : LocalizationTestBase
{
    public TmModalTests()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        Services.AddSingleton(jsRuntime);
    }

    #region Rendering Tests

    [Fact]
    public void Modal_IsVisible_WhenShowTrue()
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Modal")
            .AddChildContent("<p>Modal content</p>"));

        // Assert
        cut.Find(".tm-modal-overlay").Should().NotBeNull();
        cut.Find(".tm-modal").Should().NotBeNull();
    }

    [Fact]
    public void Modal_IsHidden_WhenShowFalse()
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.Title, "Test Modal"));

        // Assert
        cut.FindAll(".tm-modal-overlay").Count.Should().Be(0);
    }

    [Fact]
    public void Modal_DisplaysTitle()
    {
        // Arrange
        const string title = "My Modal Title";

        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, title));

        // Assert
        cut.Find(".tm-modal-title").TextContent.Should().Be(title);
    }

    [Fact]
    public void Modal_DisplaysChildContent()
    {
        // Arrange
        const string content = "This is the modal body content";

        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .AddChildContent(content));

        // Assert
        cut.Find(".tm-modal-body").TextContent.Should().Contain(content);
    }

    #endregion

    #region Interaction Tests

    [Fact]
    public void Modal_ClickOverlay_CallsOnClose()
    {
        // Arrange
        bool closeCalled = false;
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true))
            .Add(m => m.CloseOnOverlayClick, true));

        // Act
        cut.Find(".tm-modal-overlay").Click();

        // Assert
        closeCalled.Should().BeTrue();
    }

    [Fact]
    public void Modal_ClickOverlay_DoesNotClose_WhenCloseOnOverlayClickFalse()
    {
        // Arrange
        bool closeCalled = false;
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true))
            .Add(m => m.CloseOnOverlayClick, false));

        // Act
        cut.Find(".tm-modal-overlay").Click();

        // Assert
        closeCalled.Should().BeFalse();
    }

    [Fact]
    public void Modal_ClickCloseButton_CallsOnClose()
    {
        // Arrange
        bool closeCalled = false;
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.ShowCloseButton, true)
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Act
        cut.Find(".tm-modal-close").Click();

        // Assert
        closeCalled.Should().BeTrue();
    }

    [Fact]
    public void Modal_PressEscape_CallsOnClose()
    {
        // Arrange
        bool closeCalled = false;
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.CloseOnEscape, true)
            .Add(m => m.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Act
        cut.Find(".tm-modal").KeyUp(new KeyboardEventArgs { Key = "Escape" });

        // Assert
        closeCalled.Should().BeTrue();
    }

    #endregion

    #region Size Tests

    [Theory]
    [InlineData(ModalSize.Small, "tm-modal--sm")]
    [InlineData(ModalSize.Medium, "tm-modal--md")]
    [InlineData(ModalSize.Large, "tm-modal--lg")]
    [InlineData(ModalSize.XLarge, "tm-modal--xl")]
    [InlineData(ModalSize.Fullscreen, "tm-modal--fullscreen")]
    public void Modal_AppliesSizeClass(ModalSize size, string expectedClass)
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.Size, size));

        // Assert
        cut.Find(".tm-modal").ClassList.Should().Contain(expectedClass);
    }

    #endregion

    #region Animation Tests

    [Fact]
    public void Modal_HasAnimationClasses()
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.Animated, true));

        // Assert
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--animated");
    }

    [Fact]
    public void Modal_Show_AddsVisibleClass()
    {
        // Arrange
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.Title, "Test"));

        // Act
        cut.SetParametersAndRender(p => p.Add(m => m.Show, true));

        // Assert
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--visible");
    }

    #endregion

    #region Footer Tests

    [Fact]
    public void Modal_ShowFooter_RendersFooter()
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.ShowFooter, true)
            .Add(m => m.Footer, builder => builder.AddContent(0, "Footer content")));

        // Assert
        cut.Find(".tm-modal-footer").Should().NotBeNull();
        cut.Find(".tm-modal-footer").TextContent.Should().Be("Footer content");
    }

    #endregion

    #region Accessibility Tests

    [Fact]
    public void Modal_HasRoleDialog()
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));

        // Assert
        cut.Find(".tm-modal").GetAttribute("role").Should().Be("dialog");
    }

    [Fact]
    public void Modal_HasAriaModal()
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test"));

        // Assert
        cut.Find(".tm-modal").GetAttribute("aria-modal").Should().Be("true");
    }

    [Fact]
    public void Modal_HasAriaLabelledBy()
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test Title"));

        // Assert
        var modal = cut.Find(".tm-modal");
        var ariaLabelledBy = modal.GetAttribute("aria-labelledby");
        ariaLabelledBy.Should().NotBeNull();
        
        // Verify the title element has the matching id
        var title = cut.Find(".tm-modal-title");
        title.GetAttribute("id").Should().Be(ariaLabelledBy);
    }

    #endregion

    #region Position Tests

    [Theory]
    [InlineData(ModalPosition.Center, "tm-modal--center")]
    [InlineData(ModalPosition.Top, "tm-modal--top")]
    [InlineData(ModalPosition.Bottom, "tm-modal--bottom")]
    public void Modal_AppliesPositionClass(ModalPosition position, string expectedClass)
    {
        // Act
        var cut = RenderComponent<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Test")
            .Add(m => m.Position, position));

        // Assert
        cut.Find(".tm-modal-container").ClassList.Should().Contain(expectedClass);
    }

    #endregion
}
