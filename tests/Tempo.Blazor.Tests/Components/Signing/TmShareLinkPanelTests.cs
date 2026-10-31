using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Share link panel (Signing redesign phase 7, S1-1): the enable control is a
/// <see cref="Tempo.Blazor.Components.Inputs.TmToggle"/> (role="switch"), the link input carries a
/// VISIBLE label, both copy actions render the labelled <c>TmCopyButton</c>, the QR sits in a
/// frame with a localized hint, the embed textarea's label is wired through <c>label for</c>, and
/// the section separator renders between the link area and the embed area.
/// </summary>
public class TmShareLinkPanelTests : LocalizationTestBase
{
    private const string DemoLink = "https://sign.example.test/s/abc";

    [Fact]
    public void Share_UsesTmToggle_WithRoleSwitch()
    {
        var cut = Render<TmShareLinkPanel>(parameters => parameters.Add(p => p.Link, DemoLink));

        var toggle = cut.FindComponent<Tempo.Blazor.Components.Inputs.TmToggle>();
        toggle.Should().NotBeNull("the enable control is a TmToggle, not a bare checkbox");

        var switchInput = toggle.Find("input[role='switch']");
        switchInput.GetAttribute("aria-checked").Should().Be("true");
    }

    /// <summary>The toggle's name is STATIC ("Link sharing"): a control must not rename itself
    /// with its own state, and no redundant Disabled="false" rides along (phase 7 review).</summary>
    [Fact]
    public void Share_ToggleLabelIsStatic_AndNeverDisabled()
    {
        var cut = Render<TmShareLinkPanel>(parameters => parameters.Add(p => p.Link, DemoLink));

        var toggle = cut.FindComponent<Tempo.Blazor.Components.Inputs.TmToggle>();
        toggle.Instance.Label.Should().Be("Link sharing");
        toggle.Instance.Disabled.Should().BeFalse();
    }

    [Fact]
    public void Share_LinkLabelVisible_AndForMatchesInputId()
    {
        var cut = Render<TmShareLinkPanel>(parameters => parameters.Add(p => p.Link, DemoLink));

        var input = cut.Find(".tm-share-link-panel__input");
        var labelFor = cut.Find("label.tm-share-link-panel__link-label").GetAttribute("for");
        labelFor.Should().NotBeNullOrWhiteSpace("the visible link label is wired to its input");
        input.Id.Should().Be(labelFor);
    }

    [Fact]
    public void Share_EmbedLabel_ForMatchesTextareaId()
    {
        var cut = Render<TmShareLinkPanel>(parameters => parameters
            .Add(p => p.Link, DemoLink)
            .Add(p => p.EmbedCode, "<iframe src=\"https://sign.example.test/s/abc\"></iframe>"));

        var textarea = cut.Find(".tm-share-link-panel__embed-code");
        var label = cut.Find("label.tm-share-link-panel__embed-label");
        label.GetAttribute("for").Should().Be(textarea.Id,
            "the embed label must name the textarea through label[for] (a11y bug fix)");
    }

    [Fact]
    public void Share_CopyButtons_ShowLabel()
    {
        var cut = Render<TmShareLinkPanel>(parameters => parameters
            .Add(p => p.Link, DemoLink)
            .Add(p => p.EmbedCode, "<iframe></iframe>"));

        cut.FindComponents<Tempo.Blazor.Components.Buttons.TmCopyButton>()
            .Select(component => component.Instance.ShowLabel)
            .Should().OnlyContain(showLabel => showLabel, "both copy actions show the localized label");
    }

    [Fact]
    public void Share_QrFrame_HasHint_AndSeparatorRenders()
    {
        var cut = Render<TmShareLinkPanel>(parameters => parameters
            .Add(p => p.Link, DemoLink)
            .Add(p => p.EmbedCode, "<iframe src=\"https://sign.example.test/s/abc\"></iframe>"));

        cut.Find(".tm-share-link-panel__qr-frame").Should().NotBeNull("the QR sits in a bordered frame");
        cut.Find(".tm-share-link-panel__qr-hint").TextContent.Should().NotBeEmpty("the frame carries the scan hint");
        cut.Find(".tm-share-link-panel__separator").Should().NotBeNull("the embed section starts after a separator");
    }

    [Fact]
    public void Toggle_InvokesEnabledChangedAndDisablesInputs()
    {
        bool? enabled = null;
        var cut = Render<TmShareLinkPanel>(parameters => parameters
            .Add(p => p.Link, DemoLink)
            .Add(p => p.EnabledChanged, EventCallback.Factory.Create<bool>(this, value => enabled = value)));

        cut.Find("input[role='switch']").Change(false);

        enabled.Should().BeFalse();
        cut.Find(".tm-share-link-panel").ClassList.Should().Contain("tm-share-link-panel--disabled");
        cut.Find(".tm-share-link-panel__input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Render_LinkCopyQrEmbedAndExpiration()
    {
        var cut = Render<TmShareLinkPanel>(parameters => parameters
            .Add(p => p.Link, DemoLink)
            .Add(p => p.EmbedCode, "<iframe src=\"https://sign.example.test/s/abc\"></iframe>")
            .Add(p => p.ExpiresAt, new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero)));

        cut.Find(".tm-share-link-panel__input").GetAttribute("value").Should().Be(DemoLink);
        cut.Find("[data-testid='share-link-copy']").Should().NotBeNull();
        cut.Find(".tm-qr-code").Should().NotBeNull();
        cut.Find(".tm-share-link-panel__embed-code").TextContent.Should().Contain("iframe");
        cut.Markup.Should().Contain("Expires");
    }
}
