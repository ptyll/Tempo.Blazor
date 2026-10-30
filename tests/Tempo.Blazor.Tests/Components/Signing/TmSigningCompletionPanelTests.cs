using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmSigningCompletionPanelTests : LocalizationTestBase
{
    [Fact]
    public void Render_CompletedMessageAndDownloadLink()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DownloadUrl, "/signed.pdf"));

        cut.Find(".tm-signing-completion-panel__title").TextContent.Should().Contain("Document completed");
        cut.Find(".tm-signing-completion-panel__download").GetAttribute("href").Should().Be("/signed.pdf");
    }

    [Fact]
    public void Render_SendCopyButtonAndCustomAction()
    {
        var sendCopyClicked = false;
        var customClicked = false;
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.CustomActionText, "Back to documents")
            .Add(p => p.OnSendCopy, EventCallback.Factory.Create(this, () => sendCopyClicked = true))
            .Add(p => p.OnCustomAction, EventCallback.Factory.Create(this, () => customClicked = true)));

        cut.Find(".tm-signing-completion-panel__send-copy").Click();
        cut.Find(".tm-signing-completion-panel__custom-action").Click();

        sendCopyClicked.Should().BeTrue();
        customClicked.Should().BeTrue();
    }

    [Fact]
    public void Render_WaitingForOthersState()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.IsWaitingForOthers, true));

        cut.Markup.Should().Contain("Waiting for others");
        cut.Find(".tm-signing-completion-panel").GetAttribute("data-state").Should().Be("waiting");
        cut.Find(".tm-signing-completion-panel__send-copy").HasAttribute("disabled").Should().BeTrue();
    }

    /// <summary>
    /// Signing redesign phase 3: the waiting state colours ONLY the hero circle — the card itself
    /// keeps the neutral surface. The markup renders the hero, and the stylesheet contract is
    /// checked directly: a <c>warning-subtle</c> background may only appear on a
    /// <c>__hero</c> rule, never on the panel root.
    /// </summary>
    [Fact]
    public void CompletionPanel_WaitingState_ColorsOnlyHero()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.IsWaitingForOthers, true));

        cut.Find(".tm-signing-completion-panel__hero").Should().NotBeNull();
        cut.Find(".tm-signing-completion-panel__hero .tm-icon").Should().NotBeNull();

        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components", "_signing-completion-panel.css"));

        css.Should().Contain(".tm-signing-completion-panel[data-state=\"waiting\"] .tm-signing-completion-panel__hero",
            "the waiting hero rule exists");
        css.Should().NotContain(".tm-signing-completion-panel[data-state=\"waiting\"] {",
            "the CARD itself no longer carries the waiting background — only the hero does");
    }

    /// <summary>The host slot renders between the description and the action row.</summary>
    [Fact]
    public void CompletionPanel_ChildContent_BetweenDescriptionAndActions()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DownloadUrl, "/signed.pdf")
            .Add(p => p.ChildContent, "<span class=\"slot-marker\">Timeline goes here</span>"));

        var markup = cut.Markup;
        var description = markup.IndexOf("tm-signing-completion-panel__description", StringComparison.Ordinal);
        var slot = markup.IndexOf("slot-marker", StringComparison.Ordinal);
        var actions = markup.IndexOf("tm-signing-completion-panel__actions", StringComparison.Ordinal);

        description.Should().BeGreaterThan(-1);
        actions.Should().BeGreaterThan(-1);
        slot.Should().BeGreaterThan(description, "the slot comes after the description");
        slot.Should().BeLessThan(actions, "the slot comes before the actions");
    }

    /// <summary>
    /// The panel announces through a status region on its CONTENT (title + description) and
    /// focuses the title once after the first render (default FocusTitleOnRender), without
    /// scrolling (preventScroll).
    /// </summary>
    [Fact]
    public void CompletionPanel_HasStatusRegionOnContent_AndFocusesTitle()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DownloadUrl, "/signed.pdf"));

        cut.Find(".tm-signing-completion-panel").GetAttribute("role").Should().BeNull(
            "the live region moved off the panel root onto its content");
        cut.Find(".tm-signing-completion-panel__content").GetAttribute("role").Should().Be("status");
        cut.Find(".tm-signing-completion-panel__title").GetAttribute("tabindex").Should().Be("-1");

        var invocation = JSInterop.VerifyFocusAsyncInvoke();
        invocation.Arguments[0].ShouldBeElementReferenceTo(cut.Find(".tm-signing-completion-panel__title"));
        invocation.Arguments[1].Should().Be(true,
            "the announcement focus must not scroll the signer (preventScroll)");
    }

    /// <summary>FocusTitleOnRender="false" opts out of the programmatic focus move — several
    /// panels on one screen must not fight over the initial focus.</summary>
    [Fact]
    public void CompletionPanel_FocusTitleOnRenderFalse_DoesNotFocus()
    {
        // Establish the focus baseline with a default panel in the same JSInterop context.
        var withFocus = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DownloadUrl, "/signed.pdf"));
        _ = withFocus;
        JSInterop.VerifyFocusAsyncInvoke();

        var invocationCount = JSInterop.Invocations.Count;
        Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DownloadUrl, "/signed.pdf")
            .Add(p => p.FocusTitleOnRender, false));

        JSInterop.Invocations.Count.Should().Be(invocationCount,
            "the opt-out panel must not move focus on render");
    }

    /// <summary>The root test id renders BEFORE the attribute splat, so a host passing
    /// data-testid as an unmatched attribute wins over the component's own default.</summary>
    [Fact]
    public void CompletionPanel_HostDataTestIdSplat_Wins()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DownloadUrl, "/signed.pdf")
            .AddUnmatched("data-testid", "host-owned-panel"));

        cut.Find(".tm-signing-completion-panel").GetAttribute("data-testid").Should().Be("host-owned-panel",
            "the splat renders after the component's data-testid, so the host value wins");
    }

    /// <summary>
    /// CSS contract (phase 3 review): the panel aligns to the start of its host cell instead of
    /// stretching, and the programmatic title focus paints no ring.
    /// </summary>
    [Fact]
    public void CompletionPanel_CssContract_StartsAtTop_TitleFocusPaintsNoRing()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DownloadUrl, "/signed.pdf"));

        cut.Find(".tm-signing-completion-panel__title").GetAttribute("tabindex").Should().Be("-1");

        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components", "_signing-completion-panel.css"));

        css.Should().Contain("align-content: start;", "the panel hugs its content in a taller host row");
        css.Should().Contain("align-self: start;");
        css.Should().Contain(".tm-signing-completion-panel__title:focus", "the title focus rule exists");
        css.Should().Contain("outline: none;");
    }

    /// <summary>The panel inherits TmComponentBase, so hosts can set the root test id through DataTestId.</summary>
    [Fact]
    public void CompletionPanel_SupportsDataTestId()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.DataTestId, "my-completion-panel"));

        cut.Find(".tm-signing-completion-panel").GetAttribute("data-testid").Should().Be("my-completion-panel");
    }

    [Fact]
    public void Render_UsesProvidedValuesWithoutSigningFieldLocalization()
    {
        var cut = Render<TmSigningCompletionPanel>(parameters => parameters
            .Add(p => p.Title, "Dokument podepsán pro Alex Johnson")
            .Add(p => p.Description, "Submission value: email")
            .Add(p => p.CustomActionText, "Use saved value email"));

        cut.Markup.Should().Contain("Dokument podepsán pro Alex Johnson");
        cut.Markup.Should().Contain("Submission value: email");
        cut.Find(".tm-signing-completion-panel__custom-action").TextContent.Should().Contain("Use saved value email");
    }
}
