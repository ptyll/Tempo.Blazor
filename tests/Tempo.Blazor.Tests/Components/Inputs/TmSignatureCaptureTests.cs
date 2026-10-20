using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

public class TmSignatureCaptureTests : LocalizationTestBase
{
    [Fact]
    public void Render_Default_RendersRootDrawModeCanvasAndClearButton()
    {
        var cut = Render<TmSignatureCapture>();

        var root = cut.Find(".tm-signature-capture");
        root.GetAttribute("data-mode").Should().Be("Draw");
        cut.Find("svg.tm-signature-capture__canvas").Should().NotBeNull();
        cut.Find(".tm-signature-capture__clear").TextContent.Should().Contain("Clear");
        // Frozen default: the clear button lives in the actions row, not over the surface.
        cut.Find(".tm-signature-capture__actions").InnerHtml.Should().Contain("tm-signature-capture__clear");
        cut.Find(".tm-signature-capture__surface").InnerHtml.Should().NotContain("tm-signature-capture__clear");
        // cluster-D §6: the compact icon-only tile is an overlay-mode change — the actions-row
        // button stays text-only (no icon, no label span) so the default markup is frozen.
        cut.Find(".tm-signature-capture__actions .tm-signature-capture__clear")
            .QuerySelector("svg").Should().BeNull("the actions-row clear carries no icon");
        cut.FindAll(".tm-signature-capture__clear-label").Should().BeEmpty();
    }

    [Fact]
    public void Render_ClearButtonPlacementOverlay_MovesTheSameButtonOverTheSurface()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ClearButtonPlacement, TmSignatureClearButtonPlacement.Overlay));

        // One button, moved — not a second copy. It stays a native button so Enter/Space activate once.
        cut.FindAll(".tm-signature-capture__clear").Count.Should().Be(1);
        cut.Find(".tm-signature-capture__surface").InnerHtml.Should().Contain("tm-signature-capture__clear");
        cut.Find(".tm-signature-capture__actions").InnerHtml.Should().NotContain("tm-signature-capture__clear");
        cut.Find(".tm-signature-capture").ClassList.Should().Contain("tm-signature-capture--overlay-clear");
    }

    [Fact]
    public void Render_OverlayClear_HasAnIconAndAnAccessibleLabel()
    {
        // cluster-D §6: the overlay tile becomes a compact icon-only button below the 640px
        // container width. The icon ships in the markup at every width (CSS shrinks the tile);
        // the label span keeps the accessible name — it is visually hidden under the container
        // query, never display:none.
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ClearButtonPlacement, TmSignatureClearButtonPlacement.Overlay));

        var overlayClear = cut.Find(".tm-signature-capture__surface .tm-signature-capture__clear");
        overlayClear.QuerySelector("svg").Should().NotBeNull("the compact tile is icon-led");
        var label = overlayClear.QuerySelector(".tm-signature-capture__clear-label");
        label.Should().NotBeNull("the label span carries the accessible name");
        label!.TextContent.Should().Be("Clear");
    }

    [Theory]
    [InlineData(TmSignatureCaptureMode.Typed)]
    [InlineData(TmSignatureCaptureMode.Upload)]
    public void Render_OverlayClear_FallsBackToTheActionsRow_OutsideDrawMode(TmSignatureCaptureMode mode)
    {
        // L4: the overlay clear covers the typed-name input and the upload drop zone. It belongs
        // over the pad only; Type and Upload keep the button in the actions row.
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ClearButtonPlacement, TmSignatureClearButtonPlacement.Overlay)
                      .Add(p => p.Mode, mode));

        cut.FindAll(".tm-signature-capture__clear").Should().ContainSingle();
        cut.Find(".tm-signature-capture__surface").InnerHtml.Should().NotContain("tm-signature-capture__clear");
        cut.Find(".tm-signature-capture__actions").InnerHtml.Should().Contain("tm-signature-capture__clear");
        cut.Find(".tm-signature-capture").ClassList.Should().NotContain("tm-signature-capture--overlay-clear");
    }

    [Fact]
    public void Render_ClassAndAdditionalAttributes_AreApplied()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Class, "custom-signature")
                      .AddUnmatched("data-testid", "signature"));

        var root = cut.Find("[data-testid='signature']");
        root.ClassList.Should().Contain("tm-signature-capture");
        root.ClassList.Should().Contain("custom-signature");
    }

    [Fact]
    public void Render_WithValue_ShowsPreview()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Value, "data:image/png;base64,abc"));

        cut.Find(".tm-signature-capture__preview").Should().NotBeNull();
    }

    [Fact]
    public void ModeTabClick_InvokesModeChanged()
    {
        TmSignatureCaptureMode? capturedMode = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Typed)
                      .Add(p => p.ModeChanged, EventCallback.Factory.Create<TmSignatureCaptureMode>(this, mode => capturedMode = mode)));

        cut.FindAll(".tm-signature-capture__tab")
            .Single(button => button.TextContent.Contains("Draw", StringComparison.OrdinalIgnoreCase))
            .Click();

        capturedMode.Should().Be(TmSignatureCaptureMode.Draw);
        cut.Find(".tm-signature-capture").GetAttribute("data-mode").Should().Be("Draw");
    }

    [Fact]
    public void Render_Disabled_AppliesStateAndDisablesControls()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Disabled, true));

        var root = cut.Find(".tm-signature-capture");
        root.ClassList.Should().Contain("tm-signature-capture--disabled");
        root.GetAttribute("aria-disabled").Should().Be("true");
        cut.Find(".tm-signature-capture__clear").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Draw_PointerEvents_RenderStroke()
    {
        var cut = Render<TmSignatureCapture>();

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        cut.Find("polyline").GetAttribute("points").Should().Contain("10.0,10.0 20.0,20.0");
    }

    [Fact]
    public void Draw_PointerDown_AttemptsPointerCapture()
    {
        var cut = Render<TmSignatureCapture>();

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10, PointerId = 7 });

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("tmSignatureCapture.capturePointer"));
    }

    [Fact]
    public void Draw_PointerUp_InvokesValueChanged()
    {
        string? captured = null;
        TmSignatureCaptureChangedEventArgs? changed = null;

        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value))
                      .Add(p => p.Changed, EventCallback.Factory.Create<TmSignatureCaptureChangedEventArgs>(this, args => changed = args)));

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        captured.Should().Contain("<svg");
        changed.Should().NotBeNull();
        changed!.Mode.Should().Be(TmSignatureCaptureMode.Draw);
        cut.Instance.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Draw_PointerLeave_DoesNotEndStrokeAndAllowsReturnToCanvas()
    {
        string? captured = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10, Buttons = 1, PointerType = "mouse" });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20, Buttons = 1, PointerType = "mouse" });
        canvas.TriggerEvent("onpointerleave", new PointerEventArgs { OffsetX = 25, OffsetY = 25, Buttons = 1, PointerType = "mouse" });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 40, OffsetY = 35, Buttons = 1, PointerType = "mouse" });

        captured.Should().BeNull();
        cut.Find("polyline").GetAttribute("points").Should().Contain("10.0,10.0 20.0,20.0 40.0,35.0");
    }

    [Fact]
    public void Draw_ReturningAfterPointerWasReleasedOutside_CommitsStroke()
    {
        string? captured = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10, Buttons = 1, PointerType = "mouse" });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20, Buttons = 1, PointerType = "mouse" });
        canvas.TriggerEvent("onpointerleave", new PointerEventArgs { OffsetX = 25, OffsetY = 25, Buttons = 1, PointerType = "mouse" });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 40, OffsetY = 35, Buttons = 0, PointerType = "mouse" });

        captured.Should().Contain("<svg");
        cut.Find("polyline").GetAttribute("points").Should().Be("10.0,10.0 20.0,20.0");
    }

    [Fact]
    public async Task ClearAsync_ClearsValueAndStrokes()
    {
        string? captured = "initial";
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        await cut.InvokeAsync(() => cut.Instance.ClearAsync());

        captured.Should().BeNull();
        cut.FindAll("polyline").Should().BeEmpty();
        cut.Instance.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Render_RequiredEmpty_IsInvalid()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Required, true));

        var root = cut.Find(".tm-signature-capture");
        root.ClassList.Should().Contain("tm-signature-capture--invalid");
        root.GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void Draw_PngExport_InvokesJsInterop()
    {
        JSInterop.Setup<string>("tmSignatureCapture.exportPng", _ => true).SetResult("data:image/png;base64,abc");
        string? captured = null;

        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ExportFormat, TmSignatureCaptureExportFormat.PngDataUrl)
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        cut.WaitForAssertion(() => captured.Should().Be("data:image/png;base64,abc"));
        JSInterop.VerifyInvoke("tmSignatureCapture.exportPng");
    }

    [Fact]
    public void Draw_PngExportFallsBackToSvg_WhenJsIsUnavailable()
    {
        string? captured = null;

        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ExportFormat, TmSignatureCaptureExportFormat.PngDataUrl)
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        cut.WaitForAssertion(() => captured.Should().Contain("<svg"));
    }

    [Fact]
    public void TypedMode_InputGeneratesTypedSignatureValue()
    {
        string? captured = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Typed)
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        cut.Find("input.tm-signature-capture__typed-input").Change("Alex Johnson");

        captured.Should().Contain("Alex Johnson");
        cut.Find(".tm-signature-capture__typed-preview").TextContent.Should().Contain("Alex Johnson");
    }

    [Fact]
    public void TypedMode_ScriptExport_UsesSignatureFontStack()
    {
        string? captured = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Typed)
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        cut.Find("input.tm-signature-capture__typed-input").Change("Alex Johnson");

        captured.Should().Contain("Dancing Script");
        captured.Should().Contain("font-family=\"&quot;Dancing Script&quot;");
        captured.Should().Contain("font-style=\"italic\"");
        captured.Should().Contain("font-weight=\"500\"");
        captured.Should().NotContain("font-family=\"\"");
        captured.Should().Contain("Brush Script MT");
        captured.Should().Contain("Snell Roundhand");
        captured.Should().Contain("Z003");
        cut.Find(".tm-signature-capture__typed-preview")
            .ClassList
            .Should()
            .Contain("tm-signature-capture__typed-preview--script");
    }

    [Fact]
    public void TypedMode_ScriptFont_IsBundledAndRegistered()
    {
        var root = FindRepositoryRoot();
        var fontPath = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "fonts", "dancing-script", "DancingScript-VariableFont_wght.ttf");
        var licensePath = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "fonts", "dancing-script", "OFL.txt");
        var cssPath = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.css");
        var bundledCssPath = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css");

        File.Exists(fontPath).Should().BeTrue();
        File.Exists(licensePath).Should().BeTrue();
        new FileInfo(fontPath).Length.Should().BeGreaterThan(100_000);

        var css = File.ReadAllText(cssPath);
        var bundledCss = File.ReadAllText(bundledCssPath);

        css.Should().Contain("@font-face")
            .And.Contain("Dancing Script")
            .And.Contain("DancingScript-VariableFont_wght.ttf");
        bundledCss.Should().Contain("@font-face")
            .And.Contain("Dancing Script")
            .And.Contain("DancingScript-VariableFont_wght.ttf");
    }

    [Fact]
    public void TypedMode_Initials_UsesShorterLabel()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Typed)
                      .Add(p => p.Initials, true));

        cut.Find("input.tm-signature-capture__typed-input")
            .GetAttribute("aria-label")
            .Should()
            .Contain("Initials");
    }

    [Fact]
    public void TypedMode_FontSelection_ChangesPreviewClass()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Typed)
                      .Add(p => p.TypedFont, "serif"));

        cut.Find(".tm-signature-capture__typed-preview")
            .ClassList
            .Should()
            .Contain("tm-signature-capture__typed-preview--serif");
    }

    [Fact]
    public void TypedMode_UserSelectedFont_IsNotResetByParentRerenderWithSameParameter()
    {
        string? captured = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Typed)
                      .Add(p => p.TypedFont, "serif")
                      .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        cut.Find("select.tm-signature-capture__font").Change("script");
        cut.Render(parameters => parameters.Add(p => p.TypedFont, "serif"));
        cut.Find("input.tm-signature-capture__typed-input").Change("Tyll");

        cut.Find(".tm-signature-capture__typed-preview")
            .ClassList
            .Should()
            .Contain("tm-signature-capture__typed-preview--script");
        captured.Should().Contain("Dancing Script");
    }

    [Fact]
    public void UploadMode_RendersImageInputAndUploadCallback()
    {
        var requested = false;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Upload)
                      .Add(p => p.OnUploadRequested, EventCallback.Factory.Create(this, () => requested = true)));

        cut.Find("input[type='file']").GetAttribute("accept").Should().Be("image/*");
        cut.Find(".tm-signature-capture__upload-button").Click();

        requested.Should().BeTrue();
    }

    [Fact]
    public void UploadMode_WithValue_RendersReuploadButton()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.Mode, TmSignatureCaptureMode.Upload)
                      .Add(p => p.Value, "data:image/png;base64,abc"));

        cut.Find(".tm-signature-capture__reupload").Should().NotBeNull();
    }

    [Fact]
    public void Advanced_RequireReason_RendersReasonAndIncludesItInChangedPayload()
    {
        TmSignatureCaptureChangedEventArgs? changed = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.RequireReason, true)
                      .Add(p => p.Changed, EventCallback.Factory.Create<TmSignatureCaptureChangedEventArgs>(this, args => changed = args)));

        cut.Find(".tm-signature-capture__reason").Change("Approved remotely");

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        changed.Should().NotBeNull();
        changed!.Reason.Should().Be("Approved remotely");
    }

    [Fact]
    public void Advanced_RememberSignature_RendersCheckboxAndIncludesItInChangedPayload()
    {
        TmSignatureCaptureChangedEventArgs? changed = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ShowRememberSignature, true)
                      .Add(p => p.Changed, EventCallback.Factory.Create<TmSignatureCaptureChangedEventArgs>(this, args => changed = args)));

        cut.Find("input.tm-signature-capture__remember").Change(true);

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        changed.Should().NotBeNull();
        changed!.RememberSignature.Should().BeTrue();
    }

    [Fact]
    public void Advanced_ShowQrSigningButton_InvokesCallback()
    {
        var invoked = false;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ShowQrSigningButton, true)
                      .Add(p => p.OnQrSigningRequested, EventCallback.Factory.Create(this, () => invoked = true)));

        cut.Find(".tm-signature-capture__qr svg").Should().NotBeNull();
        cut.FindAll(".tm-signature-capture__qr .tm-icon-unknown").Should().BeEmpty();

        cut.Find(".tm-signature-capture__qr").Click();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void Advanced_ShowConfirmButton_RendersDisabledUntilSignatureExists()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ShowConfirmButton, true));

        var confirm = cut.Find(".tm-signature-capture__confirm");
        confirm.TextContent.Should().Contain("Sign");
        confirm.HasAttribute("disabled").Should().BeTrue();
        confirm.QuerySelector("svg").Should().NotBeNull();
    }

    [Fact]
    public void Advanced_ConfirmButton_InvokesConfirmedWithCurrentSignature()
    {
        TmSignatureCaptureChangedEventArgs? confirmed = null;
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.ShowConfirmButton, true)
                      .Add(p => p.Confirmed, EventCallback.Factory.Create<TmSignatureCaptureChangedEventArgs>(this, args => confirmed = args)));

        var canvas = cut.Find("svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        cut.Find(".tm-signature-capture__confirm").Click();

        confirmed.Should().NotBeNull();
        confirmed!.Value.Should().Contain("<svg");
        confirmed.Mode.Should().Be(TmSignatureCaptureMode.Draw);
    }

    [Fact]
    public void Advanced_PreviousValue_RendersReusePreview()
    {
        var cut = Render<TmSignatureCapture>(parameters =>
            parameters.Add(p => p.PreviousValue, "data:image/png;base64,previous"));

        cut.Find(".tm-signature-capture__previous").Should().NotBeNull();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
