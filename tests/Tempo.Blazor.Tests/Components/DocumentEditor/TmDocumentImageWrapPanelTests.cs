using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public sealed class TmDocumentImageWrapPanelTests : LocalizationTestBase
{
    // ─── Rendering ───────────────────────────────────────────────────────────

    [Fact]
    public void Panel_RendersWithTestId()
    {
        var cut = Render<TmDocumentImageWrapPanel>();

        cut.Find("[data-testid='document-image-wrap-panel']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_DoesNotClaimToolbarRoleItCannotHonour()
    {
        var cut = Render<TmDocumentImageWrapPanel>();

        var panel = cut.Find("[data-testid='document-image-wrap-panel']");
        panel.GetAttribute("role").Should().Be(
            "group",
            "panel nemá roving tabindex; toolbar by sliboval šipky, které neumí");
    }

    [Fact]
    public void Panel_ShowsWrapModeButtons()
    {
        var cut = Render<TmDocumentImageWrapPanel>();

        cut.Find("[data-testid='document-image-wrap-inline']").Should().NotBeNull();
        cut.Find("[data-testid='document-image-wrap-square']").Should().NotBeNull();
        cut.Find("[data-testid='document-image-wrap-top-bottom']").Should().NotBeNull();
        cut.Find("[data-testid='document-image-wrap-in-front']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_WrapModeButtons_AreIconSegmentsWithAccessibleLabels()
    {
        var cut = Render<TmDocumentImageWrapPanel>();

        var square = cut.Find("[data-testid='document-image-wrap-square']");
        square.QuerySelector(".tm-icon").Should().NotBeNull();
        square.QuerySelector(".tm-document-editor__sr-only")!.TextContent.Should().NotBeNullOrWhiteSpace();
        square.GetAttribute("title").Should().NotBeNullOrWhiteSpace();
    }

    // ─── Position buttons ─────────────────────────────────────────────────────

    [Fact]
    public void Panel_PositionButtonsHidden_WhenInlineMode()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Inline));

        cut.FindAll("[data-testid='document-image-position-left']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-image-position-right']").Should().BeEmpty();
    }

    [Fact]
    public void Panel_PositionButtonsVisible_WhenSquareMode()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square));

        cut.Find("[data-testid='document-image-position-left']").Should().NotBeNull();
        cut.Find("[data-testid='document-image-position-right']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_PositionButtonsVisible_WhenTopBottomMode()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.TopBottom));

        cut.Find("[data-testid='document-image-position-left']").Should().NotBeNull();
        cut.Find("[data-testid='document-image-position-center']").Should().NotBeNull();
        cut.Find("[data-testid='document-image-position-right']").Should().NotBeNull();
    }

    // ─── Active state ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DocumentWrapMode.Inline, "document-image-wrap-inline")]
    [InlineData(DocumentWrapMode.Square, "document-image-wrap-square")]
    [InlineData(DocumentWrapMode.TopBottom, "document-image-wrap-top-bottom")]
    [InlineData(DocumentWrapMode.InFrontOfText, "document-image-wrap-in-front")]
    public void Panel_ActiveButton_HasActiveClass(DocumentWrapMode mode, string testId)
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, mode));

        cut.Find($"[data-testid='{testId}']").ClassList.Should().Contain("tm-document-image-wrap-panel__btn--active");
    }

    [Fact]
    public void Panel_RightPositionButton_HasActiveClass_WhenRightSelected()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.CurrentHorizontalPosition, DocumentImageHorizontalPosition.Right));

        cut.Find("[data-testid='document-image-position-right']")
            .ClassList.Should().Contain("tm-document-image-wrap-panel__btn--active");
        cut.Find("[data-testid='document-image-position-left']")
            .ClassList.Should().NotContain("tm-document-image-wrap-panel__btn--active");
    }

    // ─── Callbacks ────────────────────────────────────────────────────────────

    [Fact]
    public void Panel_WrapModeButton_InvokesCallback()
    {
        DocumentWrapMode? received = null;
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Inline)
            .Add(x => x.OnWrapModeChanged, (DocumentWrapMode m) => received = m));

        cut.Find("[data-testid='document-image-wrap-square']").Click();

        received.Should().Be(DocumentWrapMode.Square);
    }

    [Fact]
    public void Panel_PositionButton_InvokesCallback()
    {
        DocumentImageHorizontalPosition? received = null;
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.OnHorizontalPositionChanged, (DocumentImageHorizontalPosition p) => received = p));

        cut.Find("[data-testid='document-image-position-right']").Click();

        received.Should().Be(DocumentImageHorizontalPosition.Right);
    }

    [Fact]
    public void Panel_LeftPositionButton_InvokesCallback()
    {
        DocumentImageHorizontalPosition? received = null;
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.OnHorizontalPositionChanged, (DocumentImageHorizontalPosition p) => received = p));

        cut.Find("[data-testid='document-image-position-left']").Click();

        received.Should().Be(DocumentImageHorizontalPosition.Left);
    }

    // ─── Distance fields ──────────────────────────────────────────────────────

    [Fact]
    public void Panel_DistanceGroup_HiddenWhenInline()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Inline));

        cut.FindAll("[data-testid='document-image-distance-group']").Should().BeEmpty();
    }

    [Fact]
    public void Panel_DistanceGroup_VisibleWhenSquare()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square));

        cut.Find("[data-testid='document-image-distance-group']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_DistanceGroup_VisibleWhenTopBottom()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.TopBottom));

        cut.Find("[data-testid='document-image-distance-group']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_DistanceInputs_ShowAllFourFields()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.DistanceLeft, 8.0)
            .Add(x => x.DistanceRight, 4.0)
            .Add(x => x.DistanceTop, 2.0)
            .Add(x => x.DistanceBottom, 6.0));

        cut.Find("[data-testid='document-image-distance-left']").GetAttribute("value").Should().Be("8");
        cut.Find("[data-testid='document-image-distance-right']").GetAttribute("value").Should().Be("4");
        cut.Find("[data-testid='document-image-distance-top']").GetAttribute("value").Should().Be("2");
        cut.Find("[data-testid='document-image-distance-bottom']").GetAttribute("value").Should().Be("6");
    }

    [Fact]
    public void Panel_DistanceLeftInput_InvokesCallback()
    {
        double? received = null;
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.OnDistanceLeftChanged, (double v) => received = v));

        cut.Find("[data-testid='document-image-distance-left']").Change("12");

        received.Should().Be(12);
    }

    [Fact]
    public void Panel_DistanceRightInput_InvokesCallback()
    {
        double? received = null;
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.OnDistanceRightChanged, (double v) => received = v));

        cut.Find("[data-testid='document-image-distance-right']").Change("5");

        received.Should().Be(5);
    }

    // ─── Lock anchor ──────────────────────────────────────────────────────────

    [Fact]
    public void Panel_LockAnchorCheckbox_HiddenWhenInline()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Inline));

        cut.FindAll("[data-testid='document-image-anchor-group']").Should().BeEmpty();
    }

    [Fact]
    public void Panel_LockAnchorCheckbox_VisibleWhenSquare()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square));

        cut.Find("[data-testid='document-image-lock-anchor']").Should().NotBeNull();
    }

    [Fact]
    public void Panel_LockAnchorCheckbox_ReflectsCurrentValue()
    {
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.LockAnchor, true));

        cut.Find("[data-testid='document-image-lock-anchor']").GetAttribute("checked").Should().NotBeNull();
    }

    [Fact]
    public void Panel_LockAnchorCheckbox_InvokesCallback()
    {
        bool? received = null;
        var cut = Render<TmDocumentImageWrapPanel>(p => p
            .Add(x => x.CurrentWrapMode, DocumentWrapMode.Square)
            .Add(x => x.LockAnchor, false)
            .Add(x => x.OnLockAnchorChanged, (bool v) => received = v));

        cut.Find("[data-testid='document-image-lock-anchor']").Change(true);

        received.Should().BeTrue();
    }
}
