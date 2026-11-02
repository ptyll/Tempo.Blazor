using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models.Approvals;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Phase 5 of plan 10b3966f — <c>TmPermissionRequest</c> (Tempo.Blazor.AI), the agent's request for a
/// concrete permission: the tool, its target and the reason, a localized risk badge, a scope
/// <c>radiogroup</c> restricted to <c>ScopeOptions</c>, a duration <c>radiogroup</c> for the "for a
/// while" scope, and Allow / Deny actions.
/// <para>
/// A decision collapses the request into a row that names the outcome — a grant keeps a live
/// countdown driven by the injected <see cref="TimeProvider"/> and offers Revoke — and exactly one
/// polite live region carries every announcement. The collapse moves focus to the surviving control
/// (Revoke, else the group root) and Revoke hands it back to Allow, so the keyboard never falls onto
/// a removed button.
/// </para>
/// </summary>
public class TmPermissionRequestTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private const string ToolName = "web.search";
    private const string ToolDisplayName = "Web search";
    private const string Target = "https://example.com/pricing";
    private const string Reason = "The agent needs the current pricing page to answer.";

    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    private static Task DrainAsync<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => cut.InvokeAsync(() => { });

    private IRenderedComponent<TmPermissionRequest> RenderRequest(
        Action<ComponentParameterCollectionBuilder<TmPermissionRequest>>? configure = null,
        string? toolDisplayName = ToolDisplayName,
        string? target = Target,
        string? reason = Reason)
        => Render<TmPermissionRequest>(parameters =>
        {
            parameters
                .Add(request => request.ToolName, ToolName)
                .Add(request => request.ToolDisplayName, toolDisplayName)
                .Add(request => request.Target, target)
                .Add(request => request.Reason, reason);

            configure?.Invoke(parameters);
        });

    private static PermissionGrant DurationGrant(TimeSpan duration)
        => new(PermissionScope.ForDuration, Start.Add(duration), Start);

    /// <summary>Whether the element sits in the collapsed half of the request (inert, i.e. out of
    /// the tab order and out of the accessibility tree).</summary>
    private static bool IsHidden(IElement element)
    {
        for (var node = element; node is not null; node = node.ParentElement)
        {
            if (node.HasAttribute("inert"))
            {
                return true;
            }
        }

        return false;
    }

    private static string Summary(IRenderedComponent<TmPermissionRequest> cut)
        => cut.Find("[data-testid='ai-permission-summary']").TextContent.Trim();

    /// <summary>The ids of the elements the component moved the focus to, in call order.</summary>
    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (string?)((ElementReference)invocation.Arguments[0]!).Id)
            .ToList();

    /// <summary>
    /// A fake-clock <see cref="FakeTimeProvider.Advance(TimeSpan)"/> delivers one timer wake per elapsed
    /// second and each wake renders on its own dispatcher cycle, so a timer-driven expiry needs a few
    /// cycles before the row is final — and before its after-render focus move has run.
    /// </summary>
    private static async Task PumpAsync<TComponent>(IRenderedComponent<TComponent> cut, int cycles = 8)
        where TComponent : IComponent
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            await DrainAsync(cut);
        }
    }

    // ── The request ──────────────────────────────────────────────────────────

    [Fact]
    public void TheRequest_ShowsTheToolTheTargetTheReasonAndTheRiskInWords()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Risk, ApprovalRisk.High));

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("pending");
        cut.Find("[data-testid='ai-permission-title']").TextContent.Trim().Should().Be(ToolDisplayName);
        cut.Find("[data-testid='ai-permission-tool']").TextContent.Trim().Should().Be(ToolName);
        cut.Find("[data-testid='ai-permission-target']").TextContent.Trim().Should().Be(Target);
        cut.Find("[data-testid='ai-permission-reason']").TextContent.Trim().Should().Be(Reason);
        cut.Find("[data-testid='ai-permission-risk']").TextContent.Trim().Should().Be("High risk",
            "the risk is always spelled out — the badge colour is the second channel, never the only one");
    }

    [Fact]
    public void WithoutADisplayName_TheRequestIsNamedByItsToolName()
    {
        var cut = RenderRequest(toolDisplayName: null);

        cut.Find("[data-testid='ai-permission-title']").TextContent.Trim().Should().Be(ToolName);
    }

    [Fact]
    public void AnOptionalTargetAndReason_AreNotRenderedAsEmptyRows()
    {
        var cut = RenderRequest(target: null, reason: null);

        cut.FindAll("[data-testid='ai-permission-target']").Should().BeEmpty();
        cut.FindAll("[data-testid='ai-permission-reason']").Should().BeEmpty();
        cut.Find("[data-testid='ai-permission-tool']").Should().NotBeNull();
    }

    [Fact]
    public void TheDetails_AreADefinitionList_WithALabel()
    {
        var cut = RenderRequest();

        var details = cut.Find("[data-testid='ai-permission-details']");
        details.TagName.Should().Be("DL");
        details.GetAttribute("aria-label").Should().Be("Request details");
        details.QuerySelectorAll("dt").Should().HaveCount(3);
        details.QuerySelectorAll("dd").Should().HaveCount(3);
    }

    // ── Scope options ────────────────────────────────────────────────────────

    [Fact]
    public void WithoutScopeOptions_EveryScopeIsOffered_OnceFirst()
    {
        var cut = RenderRequest();

        var scopeGroup = cut.Find("[data-testid='ai-permission-scopes']");
        scopeGroup.GetAttribute("role").Should().Be("radiogroup");
        scopeGroup.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty();

        var radios = scopeGroup.QuerySelectorAll("input[type=radio]");
        radios.Should().HaveCount(4);
        radios.Select(radio => radio.GetAttribute("value"))
            .Should().Equal("once", "for-duration", "session", "always");
        radios.Count(radio => radio.HasAttribute("checked")).Should().Be(1,
            "a radiogroup has exactly one checked option");
        cut.Find("[data-testid='ai-permission-scope-once']").HasAttribute("checked").Should().BeTrue();
    }

    [Fact]
    public void ScopeOptions_RenderExactlyTheConfiguredScopes()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.ScopeOptions,
                new[] { PermissionScope.Always, PermissionScope.Once }));

        var values = cut
            .Find("[data-testid='ai-permission-scopes']")
            .QuerySelectorAll("input[type=radio]")
            .Select(radio => radio.GetAttribute("value"))
            .ToList();

        values.Should().Equal(
            new[] { "always", "once" },
            "the host decides which scopes are on offer, in the order it supplies them");
    }

    [Fact]
    public void EveryScopeOption_IsAnativeRadio_LabelAndOneTabStop()
    {
        var cut = RenderRequest();

        var group = cut.Find("[data-testid='ai-permission-scopes']");
        var radios = group.QuerySelectorAll("input[type=radio]").ToList();

        radios.Should().OnlyContain(radio => radio.GetAttribute("name") == radios[0].GetAttribute("name"),
            "one radio name is what makes the arrow keys work natively");

        foreach (var radio in radios)
        {
            var label = radio.ParentElement;
            label.Should().NotBeNull();
            label!.TagName.Should().Be("LABEL", "the visible text is the radio's accessible name");
            label.TextContent.Trim().Should().NotBeNullOrEmpty();
        }

        cut.FindAll("[data-testid='ai-permission-scopes'] input[tabindex='-1']").Should().BeEmpty(
            "native radios already form a single tab stop — a roving tabindex would second-guess them");
    }

    [Fact]
    public void ScopeOptions_WithAnUnknownValue_AreFilteredOut()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.ScopeOptions,
                new[] { PermissionScope.Once, (PermissionScope)42 }));

        cut.Find("[data-testid='ai-permission-scopes']")
            .QuerySelectorAll("input[type=radio]")
            .Select(radio => radio.GetAttribute("value"))
            .Should().Equal(
                new[] { "once" },
                "a value this version does not know cannot be offered as a scope");
    }

    [Fact]
    public void ScopeOptions_ThatAreEmpty_FallBackToAllowOnce()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.ScopeOptions, Array.Empty<PermissionScope>()));

        cut.Find("[data-testid='ai-permission-scopes']")
            .QuerySelectorAll("input[type=radio]")
            .Select(radio => radio.GetAttribute("value"))
            .Should().Equal(
                new[] { "once" },
                "a request the reader cannot answer is worse than a request with one modest scope");
    }

    // ── Duration options ─────────────────────────────────────────────────────

    [Fact]
    public void TheDurationRadiogroup_AppearsOnlyForTheForDurationScope()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.DurationOptions,
                new[] { TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(8) }));

        cut.FindAll("[data-testid='ai-permission-durations']").Should().BeEmpty(
            "the duration only matters once the reader picked the 'for a while' scope");

        cut.Find("[data-testid='ai-permission-scope-for-duration']").Change(true);

        var group = cut.Find("[data-testid='ai-permission-durations']");
        group.GetAttribute("role").Should().Be("radiogroup");
        group.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty();
        group.QuerySelectorAll("input[type=radio]").Should().HaveCount(3);
        group.QuerySelectorAll("label").Select(label => label.TextContent.Trim())
            .Should().Equal("15 min", "1 h", "8 h");
        cut.Find("[data-testid='ai-permission-duration-0']").HasAttribute("checked").Should().BeTrue();

        cut.Find("[data-testid='ai-permission-scope-once']").Change(true);
        cut.FindAll("[data-testid='ai-permission-durations']").Should().BeEmpty();
    }

    [Fact]
    public void WithoutDurationOptions_TheDefaultDurationsAreOffered()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.DurationOptions, Array.Empty<TimeSpan>()));

        cut.Find("[data-testid='ai-permission-scope-for-duration']").Change(true);

        cut.Find("[data-testid='ai-permission-durations']")
            .QuerySelectorAll("label")
            .Select(label => label.TextContent.Trim())
            .Should().Equal(
                new[] { "15 min", "1 h" },
                "a 'for a while' permission always needs a length — to forbid the timed scope a host "
                + "excludes it from ScopeOptions instead");
    }

    // ── Allowing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllowOnce_RaisesADecisionWithAGrantThatDoesNotExpire()
    {
        PermissionDecision? raised = null;
        PermissionGrant? bound = null;
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, decision =>
            {
                raised = decision;
                return Task.CompletedTask;
            })
            .Add(request => request.GrantChanged, grant => bound = grant));

        await cut.Find("[data-testid='ai-permission-allow']").ClickAsync(new MouseEventArgs());

        raised.Should().NotBeNull();
        raised!.IsGranted.Should().BeTrue();
        raised.Scope.Should().Be(PermissionScope.Once);
        raised.Grant.Should().NotBeNull();
        raised.Grant!.ExpiresAt.Should().BeNull();
        raised.DecidedAt.Should().NotBeNull();
        bound.Should().Be(raised.Grant, "the host can bind the grant the reader just gave");

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("granted");
        Summary(cut).Should().Be("Allowed once");
    }

    [Fact]
    public async Task AllowForDuration_RaisesTheChosenScopeAndTheExpiryThatFollowsFromIt()
    {
        UseFakeClock();
        PermissionDecision? raised = null;
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.DurationOptions,
                new[] { TimeSpan.FromMinutes(15), TimeSpan.FromHours(1) })
            .Add(request => request.OnDecision, decision =>
            {
                raised = decision;
                return Task.CompletedTask;
            }));

        cut.Find("[data-testid='ai-permission-scope-for-duration']").Change(true);
        cut.Find("[data-testid='ai-permission-duration-1']").Change(true);
        await cut.Find("[data-testid='ai-permission-allow']").ClickAsync(new MouseEventArgs());

        raised.Should().NotBeNull();
        raised!.Scope.Should().Be(PermissionScope.ForDuration);
        raised.Grant!.ExpiresAt.Should().Be(Start.AddHours(1));
        raised.Grant.GrantedAt.Should().Be(Start);
        cut.Find("[data-testid='ai-permission-summary']").TextContent.Trim()
            .Should().Be("Allowed for 1 h · 1 h left");
    }

    [Fact]
    public async Task AllowWithASessionOrAlwaysScope_ReportsThatScopeInTheGrant()
    {
        var raised = new List<PermissionDecision>();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, decision =>
            {
                raised.Add(decision);
                return Task.CompletedTask;
            })
            .Add(request => request.GrantChanged, _ => { }));

        cut.Find("[data-testid='ai-permission-scope-session']").Change(true);
        await cut.Find("[data-testid='ai-permission-allow']").ClickAsync(new MouseEventArgs());
        Summary(cut).Should().Be("Allowed for this session");

        // A second request instance (a new ask) starts pending again.
        var second = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, decision =>
            {
                raised.Add(decision);
                return Task.CompletedTask;
            }));
        second.Find("[data-testid='ai-permission-scope-always']").Change(true);
        await second.Find("[data-testid='ai-permission-allow']").ClickAsync(new MouseEventArgs());

        raised.Select(decision => decision.Scope)
            .Should().Equal(PermissionScope.Session, PermissionScope.Always);
        Summary(second).Should().Be("Allowed from now on");
    }

    // ── Denying ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deny_RaisesADenialAndCollapsesIntoARow()
    {
        PermissionDecision? raised = null;
        var grantRaised = 0;
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, decision =>
            {
                raised = decision;
                return Task.CompletedTask;
            })
            .Add(request => request.GrantChanged, _ => grantRaised++));

        await cut.Find("[data-testid='ai-permission-deny']").ClickAsync(new MouseEventArgs());

        raised.Should().NotBeNull();
        raised!.IsGranted.Should().BeFalse();
        raised.Grant.Should().BeNull();
        grantRaised.Should().Be(0, "a denial grants nothing");

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("denied");
        Summary(cut).Should().Be("Denied");
        IsHidden(cut.Find("[data-testid='ai-permission-allow']")).Should().BeTrue(
            "the request body is inert once decided");
        cut.FindAll("[data-testid='ai-permission-revoke']").Should().BeEmpty();
        cut.Find("[data-testid='ai-permission-announcements']").TextContent.Trim()
            .Should().Be("Permission denied.");
    }

    // ── A granted request ────────────────────────────────────────────────────

    [Fact]
    public void AGrantedRequest_ShowsTheScopeAndTheRemainingTime()
    {
        UseFakeClock();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromHours(1))));

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("granted");
        cut.Find("[data-testid='ai-permission-summary']").TextContent.Trim()
            .Should().Be("Allowed for 1 h · 1 h left");
        cut.FindAll("[data-testid='ai-permission-revoke']").Should().HaveCount(1);
        cut.FindAll("[data-testid='ai-permission-allow']").Should().HaveCount(1,
            "the body stays mounted for the collapse animation");
        IsHidden(cut.Find("[data-testid='ai-permission-allow']")).Should().BeTrue();
    }

    [Fact]
    public async Task TheRemainingTime_CountsDownOnTheInjectedClock()
    {
        var time = UseFakeClock();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromMinutes(5))));

        time.Advance(TimeSpan.FromMinutes(3));
        await DrainAsync(cut);

        cut.Find("[data-testid='ai-permission-summary']").TextContent.Trim()
            .Should().Be("Allowed for 5 min · 2 min left",
                "the granted row keeps counting down on its own, without a host re-render");
        cut.Find(".tm-ai-permission__countdown").GetAttribute("aria-live").Should().Be("off",
            "a clock that ticks every second must never be a live region");
    }

    [Fact]
    public void TheRequestExpiresWhenTheGrantRunsOut()
    {
        var time = UseFakeClock();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromHours(1))));

        time.SetUtcNow(Start.AddMinutes(18));
        cut.Render();
        cut.Find("[data-testid='ai-permission-summary']").TextContent.Trim()
            .Should().Be("Allowed for 1 h · 42 min left");

        time.SetUtcNow(Start.AddHours(1));
        cut.Render();

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("expired");
        Summary(cut).Should().Be("Expired");
        cut.FindAll("[data-testid='ai-permission-revoke']").Should().BeEmpty(
            "there is nothing left to revoke");
        cut.Find("[data-testid='ai-permission-announcements']").TextContent.Trim()
            .Should().Be("The permission has expired.");
    }

    [Fact]
    public void AnExpiredGrant_IsReportedToTheHostOnce_AsAClearedGrant()
    {
        var time = UseFakeClock();
        var reported = new List<PermissionGrant?>();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromMinutes(5)))
            .Add(request => request.GrantChanged, grant => reported.Add(grant)));

        time.SetUtcNow(Start.AddMinutes(6));
        cut.Render();
        cut.Render();

        reported.Should().Equal(new PermissionGrant?[] { null },
            "one transition, one notification — the host must not hear the same expiry twice");
        cut.Find("[data-testid='ai-permission-announcements']").TextContent.Trim()
            .Should().Be("The permission has expired.");
    }

    [Fact]
    public void AnAlreadyExpiredGrant_RendersTheExpiredRow_WithoutOfferingRevoke()
    {
        UseFakeClock();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant,
                new PermissionGrant(PermissionScope.ForDuration, Start.AddMinutes(-1), Start.AddHours(-2))));

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("expired");
        Summary(cut).Should().Be("Expired");
        cut.FindAll("[data-testid='ai-permission-revoke']").Should().BeEmpty();
    }

    // ── Revoking ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_CallsTheHostAndReturnsTheRequestToPending()
    {
        UseFakeClock();
        var revokes = 0;
        var reported = new List<PermissionGrant?>();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromHours(1)))
            .Add(request => request.OnRevoke, () =>
            {
                revokes++;
                return Task.CompletedTask;
            })
            .Add(request => request.GrantChanged, grant => reported.Add(grant)));

        var allowId = cut.Find("[data-testid='ai-permission-allow']").GetAttribute("blazor:elementReference");
        allowId.Should().NotBeNullOrEmpty();

        await cut.Find("[data-testid='ai-permission-revoke']").ClickAsync(new MouseEventArgs());

        revokes.Should().Be(1);
        reported.Should().Equal(new PermissionGrant?[] { null });
        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("pending");
        IsHidden(cut.Find("[data-testid='ai-permission-allow']")).Should().BeFalse(
            "the request is decidable again");

        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(allowId,
            "revoking re-expands the request, so focus lands back on the primary action");
    }

    [Fact]
    public async Task RevokingWithoutAHostHandler_ClearsTheGrantLocally()
    {
        UseFakeClock();
        var reported = new List<PermissionGrant?>();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromHours(1)))
            .Add(request => request.GrantChanged, grant => reported.Add(grant)));

        await cut.Find("[data-testid='ai-permission-revoke']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("pending");
        reported.Should().Equal(new PermissionGrant?[] { null });
    }

    [Fact]
    public async Task ARevokeThatFinishesAfterTheDeadline_LeavesTheRequestExpired()
    {
        var time = UseFakeClock();
        var reported = new List<PermissionGrant?>();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromMinutes(1)))
            .Add(request => request.OnRevoke, async () => await Task.Delay(500))
            .Add(request => request.GrantChanged, grant => reported.Add(grant)));

        var click = cut.Find("[data-testid='ai-permission-revoke']").ClickAsync(new MouseEventArgs());

        // The parent re-renders while the revoke is still being saved and the deadline passes.
        time.SetUtcNow(Start.AddMinutes(2));
        cut.Render(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromMinutes(1)))
            .Add(request => request.OnRevoke, async () => await Task.Delay(500)));

        await click;

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("expired",
            "a permission that ran out while the revoke was saving did run out — the row must not "
            + "reopen a question the deadline already answered");
        cut.Find("[data-testid='ai-permission-announcements']").TextContent.Trim()
            .Should().Be("The permission has expired.");
        reported.Should().Equal(new PermissionGrant?[] { null },
            "the expiry was already announced before the revoke finished — reporting the cleared grant "
            + "again would tell the host about one transition twice");
    }

    [Fact]
    public async Task ARevokeThatFinishesAfterTheDeadline_WithoutAParentRender_LeavesTheRequestExpired()
    {
        var time = UseFakeClock();
        var reported = new List<PermissionGrant?>();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromMinutes(1)))
            .Add(request => request.OnRevoke, async () => await Task.Delay(500))
            .Add(request => request.GrantChanged, grant => reported.Add(grant)));

        var click = cut.Find("[data-testid='ai-permission-revoke']").ClickAsync(new MouseEventArgs());

        // Nothing re-renders the request while the revoke is being saved: the deadline is caught by the
        // revoke itself, so the fix must not depend on a host round trip.
        time.SetUtcNow(Start.AddMinutes(2));
        await click;

        cut.Instance.State.Should().Be(PermissionRequestState.Expired,
            "the request just spent its whole deadline while the revoke was being saved");
        IsHidden(cut.Find("[data-testid='ai-permission-allow']")).Should().BeTrue(
            "Allow must not be offered again for a permission that ran out");
        cut.FindAll("[data-testid='ai-permission-revoke']").Should().BeEmpty();
        reported.Should().Equal(new PermissionGrant?[] { null },
            "one transition, one notification");
    }

    [Fact]
    public async Task AFailedRevokeOnAnExpiredGrant_ReportsTheExpiryNotTheSaveError()
    {
        var time = UseFakeClock();
        var reported = new List<PermissionGrant?>();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromMinutes(1)))
            .Add(request => request.OnRevoke, async () =>
            {
                await Task.Delay(500);
                throw new InvalidOperationException("boom");
            })
            .Add(request => request.GrantChanged, grant => reported.Add(grant)));

        var click = cut.Find("[data-testid='ai-permission-revoke']").ClickAsync(new MouseEventArgs());
        time.SetUtcNow(Start.AddMinutes(2));
        cut.Render(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromMinutes(1)))
            .Add(request => request.OnRevoke, async () =>
            {
                await Task.Delay(500);
                throw new InvalidOperationException("boom");
            }));

        await click;

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("expired");
        cut.Find("[data-testid='ai-permission-announcements']").TextContent.Trim()
            .Should().Be("The permission has expired.",
                "the reader hears the state that now holds, not 'try again' over a passed deadline");
        reported.Should().Equal(new PermissionGrant?[] { null },
            "a failed revoke cannot add a second notification to the expiry already reported");
    }

    [Fact]
    public async Task AFailedRevoke_KeepsTheGrantAndShowsTheError()
    {
        UseFakeClock();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromHours(1)))
            .Add(request => request.OnRevoke, () => throw new InvalidOperationException("boom")));

        await cut.Find("[data-testid='ai-permission-revoke']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("granted",
            "a revoke that could not be saved must not look like it happened");
        cut.Find("[data-testid='ai-permission-error']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
        cut.Find("[data-testid='ai-permission-announcements']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
    }

    // ── Focus when the grant runs out on its own ─────────────────────────────

    [Fact]
    public async Task AnExpiryWhileTheRevokeButtonHasFocus_MovesFocusToTheGroupRoot()
    {
        var time = UseFakeClock();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromSeconds(5))));

        var rootId = cut.Find(".tm-ai-permission").GetAttribute("blazor:elementReference");
        rootId.Should().NotBeNullOrEmpty();

        // The reader is on Revoke when the deadline passes on the ticker: the button disappears, so
        // the browser would otherwise drop the focus to <body>.
        cut.Find("[data-testid='ai-permission-revoke']").FocusIn();

        time.Advance(TimeSpan.FromSeconds(6));
        await PumpAsync(cut);

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("expired");
        cut.FindAll("[data-testid='ai-permission-revoke']").Should().BeEmpty();
        FocusTargets().Should().Equal(new[] { rootId },
            "a removed element cannot keep the focus, so the keyboard must land on the group root");
    }

    [Fact]
    public async Task AnExpiryWhileTheReaderIsElsewhere_DoesNotMoveTheFocus()
    {
        var time = UseFakeClock();
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.Grant, DurationGrant(TimeSpan.FromSeconds(5))));

        time.Advance(TimeSpan.FromSeconds(6));
        await PumpAsync(cut);

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("expired");
        FocusTargets().Should().BeEmpty(
            "focus is only rescued when the button that vanished actually owned it");
    }

    // ── Failure and busy states ──────────────────────────────────────────────

    [Fact]
    public async Task AFailedDecisionKeepsTheRequestPending_AndShowsTheLocalizedError()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, _ => throw new InvalidOperationException("boom")));

        await cut.Find("[data-testid='ai-permission-allow']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("pending");
        IsHidden(cut.Find("[data-testid='ai-permission-allow']")).Should().BeFalse();
        cut.Find("[data-testid='ai-permission-error']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
        cut.Find("[data-testid='ai-permission-announcements']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
    }

    [Fact]
    public async Task WhileTheDecisionIsBeingSaved_BothActionsAreDisabled()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, async _ => await Task.Delay(500)));

        var click = cut.Find("[data-testid='ai-permission-allow']").ClickAsync(new MouseEventArgs());

        cut.WaitForState(() => cut.Instance.IsBusy);
        cut.Find(".tm-ai-permission").GetAttribute("aria-busy").Should().Be("true");
        cut.Find("[data-testid='ai-permission-allow']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='ai-permission-deny']").HasAttribute("disabled").Should().BeTrue();

        await click;

        cut.Find(".tm-ai-permission").GetAttribute("data-state").Should().Be("granted");
        cut.Find(".tm-ai-permission").Attributes.Any(attribute => attribute.Name == "aria-busy").Should()
            .BeFalse("the busy state is gone once the decision landed");
    }

    [Fact]
    public async Task DenyingMovesFocusToTheGroupRoot_SoTheKeyboardDoesNotFallAway()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, _ => Task.CompletedTask));

        var rootId = cut.Find(".tm-ai-permission").GetAttribute("blazor:elementReference");
        rootId.Should().NotBeNullOrEmpty("the root is the focus fallback (tabindex=-1)");

        await cut.Find("[data-testid='ai-permission-deny']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-ai-permission").GetAttribute("tabindex").Should().Be("-1");
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(rootId);
    }

    // ── Shortcuts ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheShortcuts_AnnounceThemselves_AndCanBeSwitchedOff()
    {
        var decisions = 0;
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.EnableShortcuts, false)
            .Add(request => request.OnDecision, _ =>
            {
                decisions++;
                return Task.CompletedTask;
            }));

        cut.FindAll("[data-testid='ai-permission-shortcuts']").Should().BeEmpty();
        cut.Find("[data-testid='ai-permission-allow']").GetAttribute("aria-keyshortcuts")
            .Should().BeNullOrEmpty("a switched-off shortcut must not be advertised");

        cut.Find(".tm-ai-permission").KeyDown(new KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = true,
        });
        decisions.Should().Be(0);

        var enabled = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, _ =>
            {
                decisions++;
                return Task.CompletedTask;
            }));

        enabled.Find("[data-testid='ai-permission-shortcuts']").TextContent.Trim()
            .Should().Be("Keyboard: Ctrl+Enter allows, Ctrl+Backspace denies.");
        enabled.Find("[data-testid='ai-permission-allow']").GetAttribute("aria-keyshortcuts")
            .Should().Be("Control+Enter Meta+Enter");
        enabled.Find("[data-testid='ai-permission-deny']").GetAttribute("aria-keyshortcuts")
            .Should().Be("Control+Backspace Meta+Backspace");

        await enabled.Find(".tm-ai-permission").KeyDownAsync(new KeyboardEventArgs
        {
            Key = "Enter",
            CtrlKey = true,
        });
        decisions.Should().Be(1, "Ctrl+Enter is the advertised allow shortcut");

        var denial = RenderRequest(parameters => parameters
            .Add(request => request.OnDecision, _ =>
            {
                decisions++;
                return Task.CompletedTask;
            }));
        await denial.Find(".tm-ai-permission").KeyDownAsync(new KeyboardEventArgs
        {
            Key = "Backspace",
            MetaKey = true,
        });
        decisions.Should().Be(2, "Cmd+Backspace is the advertised deny shortcut");
    }

    // ── Accessibility invariants ─────────────────────────────────────────────

    [Fact]
    public void ThereIsExactlyOneLiveRegion_AndTheGroupIsNamed()
    {
        var cut = RenderRequest();

        cut.FindAll("[role='status']").Should().HaveCount(1,
            "one live region means one place to reason about announcements");
        cut.Find("[data-testid='ai-permission-announcements']").GetAttribute("aria-live").Should().Be("polite");

        var root = cut.Find(".tm-ai-permission");
        root.GetAttribute("role").Should().Be("group");
        root.GetAttribute("aria-label").Should().Be("Permission request");
    }

    [Fact]
    public void ATitlelessRequest_IsNamedByTheHostLabel()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.AriaLabel, "Let the agent read the pricing page"));

        cut.Find(".tm-ai-permission").GetAttribute("aria-label")
            .Should().Be("Let the agent read the pricing page");
    }

    [Fact]
    public void TestIdPrefix_NamespacesEveryInternalTestId()
    {
        var cut = RenderRequest(parameters => parameters
            .Add(request => request.TestIdPrefix, "ask-1"));

        cut.Find("[data-testid='ask-1-ai-permission']").Should().NotBeNull();
        cut.Find("[data-testid='ask-1-ai-permission-allow']").Should().NotBeNull();
    }

    [Fact]
    public void AdditionalAttributes_AreAppliedToTheRoot()
    {
        var cut = RenderRequest(parameters => parameters
            .AddUnmatched("data-request-id", "req-7"));

        cut.Find(".tm-ai-permission").GetAttribute("data-request-id").Should().Be("req-7");
    }
}
