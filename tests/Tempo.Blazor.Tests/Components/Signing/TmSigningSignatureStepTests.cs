using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningSignatureStep (Signing redesign phase 2) — the thin shell + capture wrapper the runner
/// uses instead of a bare TmSignatureCapture, so a signature step finally carries the step title,
/// the required badge and the "appears on" hint like every other step.
/// </summary>
public class TmSigningSignatureStepTests : LocalizationTestBase
{
    [Fact]
    public void SignatureStep_RendersShell_WithRequiredBadgeAndAppearsOn()
    {
        var cut = Render<TmSigningSignatureStep>(parameters => parameters
            .Add(p => p.Field, CreateSignatureField())
            .Add(p => p.AppearsOn, "Page 2"));

        cut.Find(".tm-signing-step-shell").Should().NotBeNull("the signature step renders inside the shared step shell");
        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("Signature");
        cut.Find(".tm-signing-step-shell__required").TextContent.Should().Contain("Required");
        cut.Find(".tm-signing-step-shell__appears-on").TextContent.Should().Be("Page 2");
        cut.Find(".tm-signature-capture").Should().NotBeNull();
    }

    [Fact]
    public void SignatureStep_OptionalField_ShowsOptionalBadge()
    {
        var field = CreateSignatureField();
        field.Required = false;

        var cut = Render<TmSigningSignatureStep>(parameters => parameters.Add(p => p.Field, field));

        cut.Find(".tm-signing-step-shell__optional").TextContent.Should().Contain("Optional");
    }

    /// <summary>
    /// Signing redesign phase 4: the capture's Clear button renders INSIDE the signature surface
    /// (<c>ClearButtonPlacement.Overlay</c>, the F8 core addition) — the clear sits on the canvas
    /// instead of in a row below it.
    /// </summary>
    [Fact]
    public void SignatureStep_ClearButtonInsideCanvas()
    {
        var cut = Render<TmSigningSignatureStep>(parameters => parameters
            .Add(p => p.Field, CreateSignatureField())
            .Add(p => p.Mode, TmSignatureCaptureMode.Draw));

        var surface = cut.Find(".tm-signature-capture__surface");
        surface.QuerySelector(".tm-signature-capture__clear").Should().NotBeNull(
            "Clear renders as an overlay inside the signing surface");
        surface.QuerySelectorAll(".tm-signature-capture__canvas").Should().NotBeNull();
    }

    /// <summary>The capture is the only required-marker: the step shell owns the required presentation,
    /// so the runner's signature step surfaces exactly one validation alert.</summary>
    [Fact]
    public void SignatureStep_ShellOwnsTheInvalidState_NotTheCapture()
    {
        var cut = Render<TmSigningSignatureStep>(parameters => parameters
            .Add(p => p.Field, CreateSignatureField())
            .Add(p => p.ValidationMessage, "Signature is required."));

        cut.Find(".tm-signing-step-shell--invalid").Should().NotBeNull();
        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Be("Signature is required.");
        cut.FindAll(".tm-signature-capture--invalid").Should().BeEmpty(
            "the capture is driven with Required=false; the step shell owns the invalid presentation");
        cut.FindAll("[role='alert']").Should().ContainSingle();
    }

    /// <summary>
    /// In the runner: a required signature step keeps its required badge and, after a failed Next,
    /// shows EXACTLY ONE <c>role="alert"</c> — the shell's, never the capture's duplicate
    /// (the capture's <c>Required=false</c> is deliberate; see the component remarks). The signature
    /// step is NOT the last step here: on a LAST step the phase-3 primary is Complete (disabled
    /// while incomplete) and a failed Next is unreachable — the alert contract lives on the
    /// reachable path.
    /// </summary>
    [Fact]
    public async Task SignatureStep_FailedNext_ShowsRequiredBadge_AndExactlyOneAlert()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreateRunnerPage()])
            .Add(p => p.Fields,
            [
                CreateSignatureField(),
                new SigningField
                {
                    Uuid = "name",
                    Name = "Name",
                    Type = SigningFieldType.Text,
                    Required = true,
                    Areas =
                    [
                        new SigningFieldArea { AttachmentUuid = "doc", Page = 0, X = 0.1, Y = 0.2, Width = 0.2, Height = 0.05 }
                    ]
                }
            ]));

        await cut.Find(".tm-signing-form-runner__next").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find(".tm-signing-step-shell__required").TextContent.Should().Contain("Required");
        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("required");
        cut.FindAll("[role='alert']").Should().ContainSingle(
            "the capture's Required=false keeps the step to a single alert");
    }

    private static SigningDocumentPage CreateRunnerPage()
    {
        return new SigningDocumentPage
        {
            AttachmentUuid = "doc",
            PageIndex = 0,
            Width = 600,
            Height = 800,
            Label = "Page 1"
        };
    }

    [Fact]
    public async Task SignatureStep_TypedInput_ForwardsValue()
    {
        string? value = null;
        var cut = Render<TmSigningSignatureStep>(parameters => parameters
            .Add(p => p.Field, CreateSignatureField())
            .Add(p => p.Mode, TmSignatureCaptureMode.Typed)
            .Add(p => p.Modes, [TmSignatureCaptureMode.Typed])
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => value = v)));

        await cut.Find("input.tm-signature-capture__typed-input").ChangeAsync(new ChangeEventArgs { Value = "Alex Johnson" });

        // The typed capture commits the rendered SVG signature (the typed text is embedded in it).
        value.Should().NotBeNullOrWhiteSpace();
        value.Should().Contain("Alex Johnson");
    }

    [Fact]
    public void SignatureStep_InitialsField_SetsCaptureInitials()
    {
        var field = CreateSignatureField();
        field.Type = SigningFieldType.Initials;

        var cut = Render<TmSigningSignatureStep>(parameters => parameters.Add(p => p.Field, field));

        cut.Find(".tm-signature-capture").GetAttribute("data-mode").Should().NotBeNull();
        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("Signature");
    }

    private static SigningField CreateSignatureField()
    {
        return new SigningField
        {
            Uuid = "signature",
            Name = "Signature",
            Type = SigningFieldType.Signature,
            Required = true
        };
    }
}
