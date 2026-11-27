using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public sealed class TmDocumentImageInspectorTests : LocalizationTestBase
{
    [Fact]
    public void ControlsWithoutDelegate_AreNotRendered()
    {
        // Phase 1 / DE-2.0: an inspector control whose callback is not bound would do nothing; it is not drawn.
        var bare = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing =>
            {
                drawing.Source = DocumentImageSource.Url;
                drawing.Url = "https://example.test/evidence.png";
            })));

        bare.FindAll("[data-testid='document-image-inspector-link']").Should().BeEmpty();
        bare.FindAll("[data-testid='document-image-inspector-decorative']").Should().BeEmpty();
        bare.FindAll("[data-testid='document-image-inspector-lock-anchor']").Should().BeEmpty();
        bare.FindAll("[data-testid='document-image-inspector-bring-forward']").Should().BeEmpty();
        bare.FindAll("[data-testid='document-image-inspector-send-backward']").Should().BeEmpty();
        bare.FindAll("[data-testid='document-image-inspector-section-order']").Should().BeEmpty();
        foreach (var testId in new[]
        {
            "document-image-inspector-alt", "document-image-inspector-caption", "document-image-inspector-caption-toggle",
            "document-image-inspector-width", "document-image-inspector-height", "document-image-inspector-lock-aspect",
            "document-image-inspector-reset-size", "document-image-inspector-wrap-square", "document-image-inspector-align-start",
            "document-image-inspector-position-x", "document-image-inspector-position-y",
            "document-image-inspector-section-size", "document-image-inspector-section-wrap", "document-image-inspector-section-position"
        })
        {
            bare.FindAll($"[data-testid='{testId}']").Should().BeEmpty($"{testId} has no bound callback");
        }
        // The read-only source line replaces the editable URL field.
        bare.Find("[data-testid='document-image-inspector-asset-info']").Should().NotBeNull();

        var wired = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing =>
            {
                drawing.Source = DocumentImageSource.Url;
                drawing.Url = "https://example.test/evidence.png";
            }))
            .Add(p => p.UrlChanged, _ => { })
            .Add(p => p.AltTextChanged, _ => { })
            .Add(p => p.CaptionChanged, _ => { })
            .Add(p => p.SizeChanged, _ => { })
            .Add(p => p.WrapModeChanged, _ => { })
            .Add(p => p.AlignmentChanged, _ => { })
            .Add(p => p.PositionChanged, _ => { })
            .Add(p => p.DecorativeChanged, _ => { })
            .Add(p => p.LockAnchorChanged, _ => { })
            .Add(p => p.BringForward, () => { })
            .Add(p => p.SendBackward, () => { }));

        wired.Find("[data-testid='document-image-inspector-link']").Should().NotBeNull();
        wired.Find("[data-testid='document-image-inspector-decorative']").Should().NotBeNull();
        wired.Find("[data-testid='document-image-inspector-lock-anchor']").Should().NotBeNull();
        wired.Find("[data-testid='document-image-inspector-bring-forward']").Should().NotBeNull();
        wired.Find("[data-testid='document-image-inspector-send-backward']").Should().NotBeNull();
        foreach (var testId in new[]
        {
            "document-image-inspector-alt", "document-image-inspector-caption", "document-image-inspector-caption-toggle",
            "document-image-inspector-width", "document-image-inspector-lock-aspect", "document-image-inspector-reset-size",
            "document-image-inspector-wrap-square", "document-image-inspector-align-start", "document-image-inspector-position-x"
        })
        {
            wired.Find($"[data-testid='{testId}']").Should().NotBeNull($"{testId} renders once its callback is bound");
        }
    }

    [Fact]
    public void Inspector_RendersAltWarning_ForActiveDrawingObject_WhenAltTextIsEmpty()
    {
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage()));

        cut.Find("[data-testid='document-image-inspector']").Should().NotBeNull();
        cut.Find("[data-testid='document-image-inspector-alt-warning']")
            .TextContent.Should().Contain("alt text");
    }

    [Fact]
    public void Inspector_AltInput_RaisesAltTextChanged_ForActiveDrawingObject()
    {
        string? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.AltText = "Old"))
            .Add(p => p.AltTextChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-alt']").Change("New alt");

        received.Should().Be("New alt");
    }

    [Fact]
    public void Inspector_DecorativeCheckbox_RaisesDecorativeChangedAndSuppressesAltWarning_ForActiveDrawingObject()
    {
        bool? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.IsDecorative = true))
            .Add(p => p.AltTextChanged, _ => { })
            .Add(p => p.DecorativeChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-decorative']")
            .HasAttribute("checked").Should().BeTrue();
        cut.FindAll("[data-testid='document-image-inspector-alt-warning']").Should().BeEmpty();
        cut.Find("[data-testid='document-image-inspector-alt']")
            .HasAttribute("disabled").Should().BeTrue();

        cut.Find("[data-testid='document-image-inspector-decorative']").Change(false);

        received.Should().BeFalse();
    }

    [Fact]
    public void Inspector_WrapButton_RaisesWrapModeChanged_ForActiveDrawingObject()
    {
        DocumentWrapMode? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage())
            .Add(p => p.WrapModeChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-wrap-square']").Click();

        received.Should().Be(DocumentWrapMode.Square);
    }

    [Fact]
    public void Inspector_WrapButtons_AreIconSegmentsWithAccessibleLabels_ForActiveDrawingObject()
    {
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage())
            .Add(p => p.WrapModeChanged, _ => { }));

        var square = cut.Find("[data-testid='document-image-inspector-wrap-square']");
        square.QuerySelector(".tm-icon").Should().NotBeNull();
        square.QuerySelector(".tm-document-editor__sr-only")!.TextContent.Should().NotBeNullOrWhiteSpace();
        square.GetAttribute("title").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Inspector_CaptionCheckboxReflectsCaption_ForActiveDrawingObject()
    {
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.Caption = "Evidence caption"))
            .Add(p => p.CaptionChanged, _ => { }));

        cut.Find("[data-testid='document-image-inspector-caption-toggle']")
            .HasAttribute("checked").Should().BeTrue();
        cut.Find("[data-testid='document-image-inspector-caption']")
            .GetAttribute("value").Should().Be("Evidence caption");
    }

    [Fact]
    public void Inspector_CaptionInput_RaisesCaptionChanged_ForActiveDrawingObject()
    {
        string? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.Caption = "Old caption"))
            .Add(p => p.CaptionChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-caption']").Change("New caption");

        received.Should().Be("New caption");
    }

    [Fact]
    public async Task Inspector_CaptionInput_DebouncesInputChanges_ForActiveDrawingObject()
    {
        string? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.Caption = "Old caption"))
            .Add(p => p.CaptionChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-caption']").Input("Live caption");

        await WaitForCallbackAsync(() => received == "Live caption");
        received.Should().Be("Live caption");
    }

    [Fact]
    public void Inspector_UncheckingCaption_RaisesEmptyCaption_ForActiveDrawingObject()
    {
        string? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.Caption = "Existing caption"))
            .Add(p => p.CaptionChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-caption-toggle']").Change(false);

        received.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DocumentImageHorizontalPosition.Left, "document-image-inspector-align-start")]
    [InlineData(DocumentImageHorizontalPosition.Center, "document-image-inspector-align-center")]
    [InlineData(DocumentImageHorizontalPosition.Right, "document-image-inspector-align-end")]
    public void Inspector_UsesDrawingLayoutHorizontalPosition_ForActiveAlignment(
        DocumentImageHorizontalPosition horizontalPosition,
        string activeTestId)
    {
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing =>
            {
                drawing.Layout = new DocumentObjectLayout
                {
                    Kind = DocumentObjectLayoutKind.Anchored,
                    Wrap = new DocumentObjectWrap { Mode = DocumentWrapMode.Square },
                    Position = new DocumentObjectPosition { HorizontalAlignment = horizontalPosition }
                };
            }))
            .Add(p => p.AlignmentChanged, _ => { }));

        cut.Find($"[data-testid='{activeTestId}']").ClassList
            .Should().Contain("tm-document-image-inspector__swatch--active");
    }

    [Fact]
    public void Inspector_SizeInput_RaisesSizeChanged_ForActiveDrawingObject()
    {
        DocumentImageSize? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.Size = new DocumentImageSize { Width = 120, Height = 80 }))
            .Add(p => p.SizeChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-width']").Change("240");

        received.Should().NotBeNull();
        received!.Width.Should().Be(240);
        received.Height.Should().Be(80);
    }

    [Fact]
    public async Task Inspector_SizeInput_DebouncesInputChanges_ForActiveDrawingObject()
    {
        DocumentImageSize? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing => drawing.Size = new DocumentImageSize { Width = 120, Height = 80 }))
            .Add(p => p.SizeChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-width']").Input("260");

        await WaitForCallbackAsync(() => received is not null);
        received.Should().NotBeNull();
        received!.Width.Should().Be(260);
        received.Height.Should().Be(80);
    }

    [Fact]
    public async Task Inspector_UrlInput_DebouncesInputChanges_ForActiveDrawingUrlImages()
    {
        string? received = null;
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing =>
            {
                drawing.Source = DocumentImageSource.Url;
                drawing.Url = "https://old.example/image.png";
            }))
            .Add(p => p.UrlChanged, value => received = value));

        cut.Find("[data-testid='document-image-inspector-link']")
            .GetAttribute("value").Should().Be("https://old.example/image.png");
        cut.Find("[data-testid='document-image-inspector-link']").Input("https://new.example");

        await WaitForCallbackAsync(() => received == "https://new.example");
        received.Should().Be("https://new.example");
    }

    [Fact]
    public void Inspector_HidesLinkInput_ForActiveDrawingAssetImages()
    {
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing =>
            {
                drawing.Source = DocumentImageSource.Asset;
                drawing.AssetId = "evidence";
            })));

        cut.FindAll("[data-testid='document-image-inspector-link']").Should().BeEmpty();
    }

    [Fact]
    public void Inspector_HidesUrlInput_ForActiveDrawingEmbeddedDataImages()
    {
        var cut = Render<TmDocumentImageInspector>(parameters => parameters
            .Add(p => p.Image, ActiveDrawingImage(drawing =>
            {
                drawing.Source = DocumentImageSource.Url;
                drawing.Url = "data:image/png;base64,iVBORw0KGgo=";
            })));

        cut.FindAll("[data-testid='document-image-inspector-link']").Should().BeEmpty();
    }

    // Debounce callback (CaptionChanged/SizeChanged/UrlChanged) nemění stav komponenty → bUnit
    // render-triggered čekání (WaitForAssertion/WaitForState) ho nezachytí a navíc blokuje dispatcher,
    // na který se callback marshaluje. Async poll s yieldem pustí dispatcher a adaptivně počká (robustní
    // vůči paralelní zátěži, na rozdíl od fixní Task.Delay).
    private static async Task WaitForCallbackAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private static ImageBlockContent ActiveDrawingImage(Action<DocumentDrawingRun>? configure = null)
    {
        var drawing = new DocumentDrawingRun
        {
            Id = "active-drawing-inline",
            ObjectId = "active-drawing-object",
            Source = DocumentImageSource.Url,
            Url = "https://example.test/drawing.png",
            AltText = string.Empty,
            Size = new DocumentImageSize { Width = 120, Height = 80 },
            NaturalSize = new DocumentImageSize { Width = 240, Height = 160 },
            Layout = DocumentObjectLayout.Inline()
        };
        configure?.Invoke(drawing);

        return new ImageBlockContent
        {
            Source = drawing.Source,
            Url = drawing.Url,
            AssetId = drawing.AssetId,
            AltText = drawing.AltText,
            IsDecorative = drawing.IsDecorative,
            Caption = drawing.Caption,
            Size = drawing.Size,
            NaturalSize = drawing.NaturalSize,
            Layout = drawing.Layout,
            LinkUrl = drawing.LinkUrl
        };
    }
}
