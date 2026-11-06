using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 8 — <c>TmAgentPresenceOverlay</c>: the collaborators' cursors over any host content.
/// <para>
/// These facts pin the surface's promise: the content renders untouched under an ABSOLUTE, <c>aria-hidden</c> visual
/// layer, every marker names its agent and activity in a label and takes its colour from the shared agent palette, an
/// anchored marker is MEASURED by the package's ES module (the component never guesses where the element is), a point
/// marker is painted from the host's own fractions, the optional roster gives screen readers the same presence the
/// cursors show, exactly ONE polite live region announces a join, an activity change and a leave, and the model records
/// normalise what a host hands over.
/// </para>
/// </summary>
public class TmAgentPresenceOverlayTests : LocalizationTestBase
{
    private static readonly AgentRef Ana = new("Ana", "a1") { ColorIndex = 0 };
    private static readonly AgentRef Bo = new("Bo", "a2") { ColorIndex = 3 };

    private static PresenceMarker Marker(
        string id,
        AgentRef agent,
        PresenceActivity activity = PresenceActivity.Typing,
        string? anchor = null,
        double x = 0,
        double y = 0,
        string? label = null)
        => new(id, agent) { Activity = activity, AnchorId = anchor, X = x, Y = y, Label = label };

    private IRenderedComponent<TmAgentPresenceOverlay> RenderOverlay(
        Action<ComponentParameterCollectionBuilder<TmAgentPresenceOverlay>>? configure = null,
        IReadOnlyList<PresenceMarker>? markers = null)
        => Render<TmAgentPresenceOverlay>(p =>
        {
            p.Add(c => c.Markers, markers ?? [Marker("m1", Ana, anchor: "block-a")]);
            p.Add(c => c.ChildContent, builder => builder.AddMarkupContent(0, "<p>Host content</p>"));
            configure?.Invoke(p);
        });

    private static string Text(IRenderedComponent<TmAgentPresenceOverlay> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    // ── the model ────────────────────────────────────────────────────────────

    [Fact]
    public void AMarkerKeepsItsIdAgentAndActivity()
    {
        var marker = new PresenceMarker("m1", Ana) { Activity = PresenceActivity.Commenting };

        marker.Id.Should().Be("m1");
        marker.Agent.Should().BeSameAs(Ana);
        marker.Activity.Should().Be(PresenceActivity.Commenting);
        marker.HasAnchor.Should().BeFalse();
        marker.HasPoint.Should().BeTrue();
        marker.HasLabel.Should().BeFalse();
    }

    [Fact]
    public void AMarkerWithoutAnIdGetsOne_SoTwoCursorsForOneAgentStayApart()
    {
        var first = new PresenceMarker(null, Ana);
        var second = new PresenceMarker("   ", Ana);

        first.Id.Should().NotBeNullOrWhiteSpace();
        second.Id.Should().NotBeNullOrWhiteSpace();
        second.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public void AMarkerNeedsAnAgent()
    {
        var act = () => new PresenceMarker("m1", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("agent");
    }

    [Fact]
    public void ACoordinateIsNormalisedIntoTheContentBox()
    {
        var marker = new PresenceMarker("m1", Ana) { X = 1.4, Y = -0.3 };

        marker.X.Should().Be(1d);
        marker.Y.Should().Be(0d);
        new PresenceMarker("m2", Ana) { X = double.NaN }.X.Should().Be(0d);
        new PresenceMarker("m3", Ana) { Y = double.PositiveInfinity }.Y.Should().Be(0d);
    }

    [Fact]
    public void AnAnchorWinsOverAPoint()
    {
        var anchored = new PresenceMarker("m1", Ana) { AnchorId = "block-a", X = 0.5, Y = 0.5 };

        anchored.HasAnchor.Should().BeTrue();
        anchored.HasPoint.Should().BeFalse();
        anchored.X.Should().Be(0.5, "the host's fraction is still readable, it is simply not used");
    }

    // ── the layer ────────────────────────────────────────────────────────────

    [Fact]
    public void TheHostContentRendersUntouched_UnderTheLayer()
    {
        var cut = RenderOverlay();

        cut.Find("[data-testid='presence-overlay'] [data-testid='presence-content']")
            .TextContent.Should().Contain("Host content");
        cut.Find("[data-testid='presence-overlay'] [data-testid='presence-content']").TagName.Should().Be("DIV");
    }

    [Fact]
    public void TheVisualLayerIsAriaHidden_BecauseItIsDecoration()
    {
        var cut = RenderOverlay();

        var layer = cut.Find("[data-testid='presence-layer']");
        layer.GetAttribute("aria-hidden").Should().Be("true");
        layer.ClassList.Should().Contain("tm-ai-presence__layer");
    }

    [Fact]
    public void AMarkerNamesItsAgentAndActivity_InALabel()
    {
        var cut = RenderOverlay(
            null,
            [Marker("m1", Ana, PresenceActivity.Typing, anchor: "block-a")]);

        var marker = cut.Find("[data-presence-marker='m1']");
        marker.GetAttribute("data-presence-anchor").Should().Be("block-a");
        marker.GetAttribute("data-activity").Should().Be("typing");
        marker.GetAttribute("data-agent-id").Should().Be("a1");
        marker.QuerySelector(".tm-ai-presence__name")!.TextContent.Should().Be("Ana");
        marker.QuerySelector(".tm-ai-presence__activity")!.TextContent.Should().Be("Typing");
        marker.GetAttribute("style").Should().Contain("--tm-presence-color: var(--tm-agent-color-0)");
    }

    [Fact]
    public void AHostLabelReplacesTheLocalizedActivityWord()
    {
        var cut = RenderOverlay(
            null,
            [Marker("m1", Ana, PresenceActivity.Editing, anchor: "block-a", label: "Rewriting the summary")]);

        var marker = cut.Find("[data-presence-marker='m1']");
        marker.QuerySelector(".tm-ai-presence__activity")!.TextContent.Trim()
            .Should().Be("Rewriting the summary");
    }

    [Fact]
    public void APointMarkerIsPaintedFromTheHostsFractions()
    {
        var cut = RenderOverlay(
            null,
            [Marker("m1", Bo, PresenceActivity.Viewing, x: 0.4, y: 0.25)]);

        var style = cut.Find("[data-presence-marker='m1']").GetAttribute("style");
        style.Should().Contain("--tm-presence-x: 40%");
        style.Should().Contain("--tm-presence-y: 25%");
        cut.Find("[data-presence-marker='m1']").GetAttribute("data-presence-anchor").Should().BeNull();
    }

    [Fact]
    public void AnUnnamedAgentFallsBackToTheLocalizedName()
    {
        var cut = RenderOverlay(
            null,
            [Marker("m1", new AgentRef(string.Empty, "anon"), anchor: "block-a")]);

        cut.Find("[data-presence-marker='m1'] .tm-ai-presence__name").TextContent
            .Should().Be("Unnamed agent");
    }

    [Fact]
    public void NoMarkers_RendersAnEmptyLayerWithoutThrowing()
    {
        var cut = RenderOverlay(null, []);

        cut.FindAll("[data-presence-marker]").Should().BeEmpty();
        cut.Find("[data-testid='presence-overlay']").GetAttribute("data-markers").Should().Be("0");
    }

    [Fact]
    public void ANullMarkerListRendersNothingAndDoesNotThrow()
    {
        var cut = Render<TmAgentPresenceOverlay>(p =>
        {
            p.Add(c => c.Markers, null);
            p.Add(c => c.ChildContent, builder => builder.AddMarkupContent(0, "<p>Only content</p>"));
        });

        cut.FindAll("[data-presence-marker]").Should().BeEmpty();
    }

    // ── the roster ───────────────────────────────────────────────────────────

    [Fact]
    public void TheRosterIsOptOut_AndListsEveryPresentAgentForScreenReaders()
    {
        var withoutRoster = RenderOverlay();
        withoutRoster.FindAll("[data-testid='presence-roster']").Should().BeEmpty();

        var cut = RenderOverlay(p => p.Add(c => c.ShowRoster, true));
        var roster = cut.Find("[data-testid='presence-roster']");

        roster.GetAttribute("role").Should().Be("group");
        roster.GetAttribute("aria-labelledby").Should().Be(cut.Find("[data-testid='presence-roster-title']").Id);
        Text(cut, "presence-roster-title").Should().Be("Present now");

        var items = cut.FindAll("[data-testid='presence-roster-item']");
        items.Should().HaveCount(1);
        items[0].TextContent.Should().Contain("Ana").And.Contain("Typing");
    }

    [Fact]
    public void AnEmptyRosterSaysSoInsteadOfRenderingAnEmptyList()
    {
        var cut = RenderOverlay(p => p.Add(c => c.ShowRoster, true), []);

        cut.FindAll("[data-testid='presence-roster-item']").Should().BeEmpty();
        Text(cut, "presence-roster-empty").Should().Be("No one is present.");
    }

    [Fact]
    public void TheRosterNamesTheHostsActivityLabelToo()
    {
        var cut = RenderOverlay(
            p => p.Add(c => c.ShowRoster, true),
            [Marker("m1", Ana, PresenceActivity.Commenting, anchor: "block-a", label: "Reviewing the plan")]);

        Text(cut, "presence-roster-item").Should().Contain("Reviewing the plan");
    }

    // ── announcements ────────────────────────────────────────────────────────

    [Fact]
    public void TheSurfaceOwnsExactlyOnePoliteLiveRegion()
    {
        var cut = RenderOverlay(p => p.Add(c => c.ShowRoster, true));

        var regions = cut.FindAll("[aria-live]");
        regions.Should().HaveCount(1);
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].ClassList.Should().Contain("tm-sr-only");
    }

    [Fact]
    public void APresenceIsAnnouncedOnce_AndRepeatingTheSameMarkersSaysNothingNew()
    {
        var markers = new List<PresenceMarker> { Marker("m1", Ana, PresenceActivity.Typing, anchor: "block-a") };
        var cut = RenderOverlay(null, markers);

        Text(cut, "presence-announcement").Should().Be("Ana is Typing.");

        // A re-render with an EQUAL marker list must not repeat the sentence.
        cut.Render();

        Text(cut, "presence-announcement").Should().Be("Ana is Typing.");
        cut.Find("[data-testid='presence-announcement']").TextContent.Should().NotContain("\u200B",
            "nothing changed, so nothing is re-announced");
    }

    [Fact]
    public async Task AnActivityChangeIsAnnounced()
    {
        var cut = RenderOverlay(
            null,
            [Marker("m1", Ana, PresenceActivity.Typing, anchor: "block-a")]);

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmAgentPresenceOverlay.Markers)] =
                    new List<PresenceMarker> { Marker("m1", Ana, PresenceActivity.Commenting, anchor: "block-a") },
            })));

        Text(cut, "presence-announcement").Should().Be("Ana is now Commenting.");
    }

    [Fact]
    public async Task ARenamedAgentIsAnnouncedAsPresent_RatherThanAsANewActivity()
    {
        var cut = RenderOverlay(
            null,
            [Marker("m1", Ana, PresenceActivity.Typing, anchor: "block-a")]);

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmAgentPresenceOverlay.Markers)] = new List<PresenceMarker>
                {
                    Marker("m1", new AgentRef("Anastasia", "a1"), PresenceActivity.Typing, anchor: "block-a"),
                },
            })));

        Text(cut, "presence-announcement").Should().Be("Anastasia is Typing.",
            "a different name is a different reading of WHO is present, not of what they are doing");
    }

    [Fact]
    public async Task AJoinAndALeaveAreAnnounced()
    {
        var cut = RenderOverlay(
            null,
            [Marker("m1", Ana, PresenceActivity.Typing, anchor: "block-a")]);

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmAgentPresenceOverlay.Markers)] = new List<PresenceMarker>
                {
                    Marker("m1", Ana, PresenceActivity.Typing, anchor: "block-a"),
                    Marker("m2", Bo, PresenceActivity.Viewing, anchor: "block-b"),
                },
            })));

        Text(cut, "presence-announcement").Should().Be("Bo is Viewing.");

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmAgentPresenceOverlay.Markers)] =
                    new List<PresenceMarker> { Marker("m1", Ana, PresenceActivity.Typing, anchor: "block-a") },
            })));

        Text(cut, "presence-announcement").Should().Be("Bo left.");
    }

    [Fact]
    public void AnnouncementsCanBeTurnedOff_ForAHostThatOwnsItsOwnRegion()
    {
        var cut = RenderOverlay(p => p.Add(c => c.AnnounceChanges, false));

        cut.FindAll("[aria-live]").Should().BeEmpty();
        cut.FindAll("[data-testid='presence-announcement']").Should().BeEmpty();
    }

    // ── the measurement module ───────────────────────────────────────────────

    [Fact]
    public void TheMeasuringModuleIsImported_AndAttachedToTheRootAndTheContent()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.AI/js/tm-agent-presence.js");
        module.Mode = JSRuntimeMode.Loose;

        RenderOverlay(
            null,
            [Marker("m1", Ana, anchor: "block-a"), Marker("m2", Bo, anchor: "block-b")]);

        module.VerifyInvoke("attachPresence", 1);
        var attachment = module.Invocations["attachPresence"][0];
        attachment.Arguments.Should().HaveCount(2);
        attachment.Arguments[0].Should().BeOfType<ElementReference>();
        attachment.Arguments[1].Should().BeOfType<ElementReference>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheInteropAttachesOnce_RefreshesAndReleasesEverything(bool dispose)
    {
        var handle = new RecordingObjectReference();
        var module = new RecordingObjectReference { Result = handle };
        var runtime = new RecordingRuntime(module, handle);
        var interop = new AgentPresenceInterop(runtime);

        await interop.AttachAsync(default, default);
        await interop.AttachAsync(default, default);

        interop.IsAttached.Should().BeTrue();
        runtime.Calls.Should().ContainSingle(call => call.Identifier == "import",
            "the module is imported once per component");
        module.Calls.Should().ContainSingle(identifier => identifier == "attachPresence",
            "a second paint must not re-attach the measurement");

        await interop.RefreshAsync();
        handle.Calls.Should().Contain("refresh");

        if (dispose)
        {
            await interop.DisposeAsync();
        }

        if (dispose)
        {
            handle.Calls.Should().Contain("detach", "the observers and listeners are released with the component");
            handle.Disposed.Should().Be(1);
            module.Disposed.Should().Be(1);
            interop.IsAttached.Should().BeFalse();
        }
    }

    [Fact]
    public async Task TheInteropSwallowsAnUnavailableRuntime()
    {
        var interop = new AgentPresenceInterop(new RecordingRuntime(new RecordingObjectReference(), null)
        {
            Throw = new JSException("no JavaScript"),
        });

        await interop.AttachAsync(default, default);
        await interop.RefreshAsync();
        await interop.DisposeAsync();

        interop.IsAttached.Should().BeFalse();
    }

    /// <summary>An <see cref="IJSRuntime"/> that hands out scripted object references.</summary>
    private sealed class RecordingRuntime : IJSRuntime
    {
        private readonly IJSObjectReference _module;
        private readonly IJSObjectReference? _handle;

        public RecordingRuntime(IJSObjectReference module, IJSObjectReference? handle)
        {
            _module = module;
            _handle = handle;
        }

        public List<(string Identifier, object?[]? Args)> Calls { get; } = [];

        public Exception? Throw { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args)
        {
            Calls.Add((identifier, args));

            if (Throw is not null)
            {
                throw Throw;
            }

            object? result = identifier == "import" ? _module : _handle;
            return ValueTask.FromResult((TValue)result!);
        }
    }

    /// <summary>An <see cref="IJSObjectReference"/> that records every call and its disposal.</summary>
    private sealed class RecordingObjectReference : IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public int Disposed { get; private set; }

        /// <summary>The reference any invocation returns (the module hands out the attachment handle).</summary>
        public IJSObjectReference? Result { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args)
        {
            Calls.Add(identifier);
            object? result = Result;
            return ValueTask.FromResult((TValue)result!);
        }

        public ValueTask DisposeAsync()
        {
            Disposed += 1;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void NoJavaScriptIsNotAnError_TheLayerStillRenders()
    {
        // A runtime that refuses every call is what prerender, a disconnected circuit and a host without the package's
        // static assets all look like.
        Services.AddSingleton<IJSRuntime>(new UnavailableJsRuntime());

        var cut = RenderOverlay(null, [Marker("m1", Bo, PresenceActivity.Viewing, x: 0.4, y: 0.25)]);

        cut.FindAll("[data-presence-marker]").Should().HaveCount(1);
        cut.Find("[data-presence-marker='m1']").GetAttribute("style").Should().Contain("--tm-presence-x: 40%");
        cut.Find("[data-testid='presence-announcement']").Should().NotBeNull();
    }

    /// <summary>A runtime that refuses every call, the way a prerender or a disconnected circuit does.</summary>
    private sealed class UnavailableJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new JSException("JavaScript is not available in this environment.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args)
            => throw new JSException("JavaScript is not available in this environment.");
    }

    // ── accessibility plumbing ───────────────────────────────────────────────

    [Fact]
    public async Task TheLayerIsNotInTheTabOrder_AndNothingStealsFocus()
    {
        var cut = RenderOverlay(p => p.Add(c => c.ShowRoster, true));

        cut.FindAll("[tabindex]").Should().BeEmpty("a presence layer is decoration, not a control");
        cut.FindAll("[data-presence-marker]").Should().OnlyContain(marker => marker.GetAttribute("tabindex") == null);

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmAgentPresenceOverlay.Markers)] =
                    new List<PresenceMarker> { Marker("m1", Ana, PresenceActivity.Viewing, anchor: "block-a") },
            })));

        cut.FindAll("[autofocus]").Should().BeEmpty();
    }
}
