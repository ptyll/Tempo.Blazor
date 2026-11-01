using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Models.Approvals;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The agent's request for one concrete permission: the tool it wants to use, the target it wants to
/// reach, why it needs it, a localized risk badge, the scopes the host is willing to offer, and an
/// Allow / Deny decision with an optional duration.
/// <para>
/// A decision collapses the request into a single result row. A granted permission keeps a live
/// countdown driven by the injected <see cref="TimeProvider"/> and offers Revoke until it runs out; a
/// denial and an expired grant are just rows. The host owns what a permission means — the component
/// only raises <see cref="OnDecision"/> / <see cref="OnRevoke"/> and mirrors the grant it produced
/// through <see cref="GrantChanged"/>, so a host that binds <c>Grant</c> hears both the grant and its
/// expiry (as a cleared grant).
/// </para>
/// <para>
/// Accessibility: the scope and the duration are native radio groups (<see cref="PermissionScope"/>
/// values only ever come from <see cref="ScopeOptions"/>), the risk is always spelled out next to its
/// colour, exactly one polite live region carries every announcement (the ticking countdown itself is
/// <c>aria-live="off"</c>), and the collapse moves focus to the surviving control — Revoke, else the
/// group root — while Revoke hands it back to Allow. The Ctrl/Cmd shortcuts are advertised through
/// <c>aria-keyshortcuts</c> and can be switched off; the component has no text field, so there is no
/// shortcut-vs-typing conflict to fence.
/// </para>
/// <para>
/// A host that reuses one instance for a DIFFERENT request must render it with
/// <c>@key="requestId"</c>: the component treats a <c>Grant</c> parameter change as a new request and
/// deliberately does not resurrect a grant that has expired on its own.
/// </para>
/// </summary>
public partial class TmPermissionRequest : TmComponentBase, IDisposable
{
    private static readonly string AllowShortcutKeys = "Control+Enter Meta+Enter";
    private static readonly string DenyShortcutKeys = "Control+Backspace Meta+Backspace";

    private static readonly PermissionScope[] DefaultScopes =
    [
        PermissionScope.Once,
        PermissionScope.ForDuration,
        PermissionScope.Session,
        PermissionScope.Always,
    ];

    private static readonly TimeSpan[] DefaultDurations =
    [
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
    ];

    private readonly string _id = $"tm-ai-permission-{Guid.NewGuid():N}";

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private TimeProvider _clock = TimeProvider.System;
    private AiElapsedTicker? _ticker;
    private bool _disposed;

    private PermissionGrant? _grant;
    private PermissionGrant? _grantParameter;
    private bool _denied;
    private bool _expired;
    private bool _busy;
    private bool _revoking;
    private bool _revokeHasFocus;
    private bool _grantClearedPending;
    private bool _scopeSeeded;
    private bool _durationSeeded;
    private PermissionScope _selectedScope = PermissionScope.Once;
    private TimeSpan _selectedDuration;
    private string? _errorMessage;
    private string _announcement = string.Empty;
    private FocusTarget _focusTarget;

    private ElementReference _rootElement;
    private ElementReference _allowElement;
    private ElementReference _revokeElement;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>Technical name of the tool the agent wants to use (for example <c>web.search</c>), shown
    /// as code in the request details.</summary>
    [Parameter] public string? ToolName { get; set; }

    /// <summary>Human-readable name of the tool. When omitted the request is named by
    /// <see cref="ToolName"/>.</summary>
    [Parameter] public string? ToolDisplayName { get; set; }

    /// <summary>The target of the call — a path, a URL, a mailbox or whatever the tool operates on.
    /// When omitted the target row is not rendered.</summary>
    [Parameter] public string? Target { get; set; }

    /// <summary>Why the agent needs the permission, in the agent's own words. When omitted the reason
    /// row is not rendered.</summary>
    [Parameter] public string? Reason { get; set; }

    /// <summary>Risk of the permission, shown as a localized badge. Default is
    /// <see cref="ApprovalRisk.Low"/>.</summary>
    [Parameter] public ApprovalRisk Risk { get; set; } = ApprovalRisk.Low;

    /// <summary>The scopes the reader may pick from, in the offered order. Default is every scope
    /// (<see cref="PermissionScope.Once"/>, <see cref="PermissionScope.ForDuration"/>,
    /// <see cref="PermissionScope.Session"/>, <see cref="PermissionScope.Always"/>); an empty or
    /// all-invalid list falls back to <see cref="PermissionScope.Once"/>, so a request can always be
    /// answered. Excluding a scope here is how a host forbids it.</summary>
    [Parameter] public IReadOnlyList<PermissionScope>? ScopeOptions { get; set; }

    /// <summary>The durations offered for <see cref="PermissionScope.ForDuration"/>, in the offered
    /// order. Default is 15 minutes and one hour; values that are zero or negative are ignored and an
    /// empty list falls back to the default, because a "for a while" permission always needs a length.
    /// To forbid the timed scope exclude it from <see cref="ScopeOptions"/>.</summary>
    [Parameter] public IReadOnlyList<TimeSpan>? DurationOptions { get; set; }

    /// <summary>The permission currently in force. While this parameter is set the request renders its
    /// granted row with the remaining time and Revoke; once the grant's
    /// <see cref="PermissionGrant.ExpiresAt"/> passes, the row becomes expired and
    /// <see cref="GrantChanged"/> reports a cleared grant. A new request needs a new component instance
    /// (render it with <c>@key</c>).</summary>
    [Parameter] public PermissionGrant? Grant { get; set; }

    /// <summary>Raised with the grant a decision produced and with <c>null</c> when the permission is
    /// revoked or runs out. Use it with <c>@bind-Grant</c>.</summary>
    [Parameter] public EventCallback<PermissionGrant?> GrantChanged { get; set; }

    /// <summary>Callback that persists the decision. The request stays pending while it runs (both
    /// actions disabled, the request <c>aria-busy</c>); when it throws, the error is shown and the
    /// request stays decidable.</summary>
    [Parameter] public Func<PermissionDecision, Task>? OnDecision { get; set; }

    /// <summary>Callback invoked when the reader revokes a permission. A throw keeps the grant and
    /// shows the localized error; without a callback Revoke still clears the grant locally and reports
    /// the cleared grant through <see cref="GrantChanged"/>.</summary>
    [Parameter] public Func<Task>? OnRevoke { get; set; }

    /// <summary>Whether the Ctrl/Cmd+Enter (allow) and Ctrl/Cmd+Backspace (deny) shortcuts are active
    /// while focus is inside the request, and whether the keyboard hint and
    /// <c>aria-keyshortcuts</c> are advertised. Default is true.</summary>
    [Parameter] public bool EnableShortcuts { get; set; } = true;

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Accessible name of the group. Defaults to a localized "Permission request".</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived state ────────────────────────────────────────────────────────

    /// <summary>Current lifecycle state of the request.</summary>
    public PermissionRequestState State => _expired
        ? PermissionRequestState.Expired
        : _denied
            ? PermissionRequestState.Denied
            : _grant is not null
                ? PermissionRequestState.Granted
                : PermissionRequestState.Pending;

    /// <summary>Whether the request still accepts a decision.</summary>
    public bool CanDecide => State == PermissionRequestState.Pending && !_busy && !_revoking;

    /// <summary>Whether a decision or a revoke is being persisted right now.</summary>
    public bool IsBusy => _busy || _revoking;

    /// <summary>Latest announcement, mirrored into the request's single live region.</summary>
    public string Announcement => _announcement;

    /// <summary>The permission in force, or null when the request is pending, denied or expired. A
    /// grant that ran out is reported as null here even before the host echoes the cleared grant —
    /// nothing is in force any more.</summary>
    public PermissionGrant? CurrentGrant => State == PermissionRequestState.Granted ? _grant : null;

    private bool IsCollapsed => State != PermissionRequestState.Pending;
    private bool ShowRevoke => State == PermissionRequestState.Granted;
    private bool ShowDurationGroup => _selectedScope == PermissionScope.ForDuration;
    private string StateName => State.ToString().ToLowerInvariant();
    private string DisplayName => string.IsNullOrWhiteSpace(ToolDisplayName) ? ToolName ?? string.Empty : ToolDisplayName!;
    private string GroupLabel => AriaLabel ?? Loc["TmPermissionRequest_AriaLabel"];
    private string ScopeLabelId => $"{_id}-scope";
    private string DurationLabelId => $"{_id}-duration";
    private string ScopeGroupName => $"{_id}-scope";
    private string DurationGroupName => $"{_id}-duration";
    private string? AllowShortcuts => EnableShortcuts ? AllowShortcutKeys : null;
    private string? DenyShortcuts => EnableShortcuts ? DenyShortcutKeys : null;
    private string RiskName => Risk.ToString().ToLowerInvariant();
    private string? ErrorMessage => _errorMessage;

    /// <summary>Time left on the grant, or null when the grant does not expire.</summary>
    private TimeSpan? Remaining => _grant?.Remaining(_clock.GetUtcNow());

    /// <summary>The remaining time as a short localized span, or null when there is nothing to count
    /// down (a grant that never expires, or one that already ran out).</summary>
    private string? RemainingText => Remaining is { } remaining && remaining > TimeSpan.Zero
        ? FormatDuration(remaining)
        : null;

    private string? ExpiresAtIso => _grant?.ExpiresAt?.ToString("o", CultureInfo.InvariantCulture);

    private bool NeedsTicking => !_disposed
        && _grant is not null
        && !_expired
        && !_busy
        && !_revoking
        && _grant.ExpiresAt is not null;

    /// <summary>The scopes the reader may pick from: the host's list, minus values this version does
    /// not know, de-duplicated and never empty.</summary>
    private IReadOnlyList<PermissionScope> EffectiveScopes
    {
        get
        {
            // An explicitly EMPTY list means "offer nothing" — it must not silently widen to every
            // scope, because a host that filtered its allowed scopes down to none would suddenly offer
            // "always". The filtered result falls back to the single modest scope instead.
            var offered = ScopeOptions ?? DefaultScopes;
            var scopes = offered
                .Where(scope => Enum.IsDefined(scope))
                .Distinct()
                .ToList();

            if (scopes.Count == 0)
            {
                scopes.Add(PermissionScope.Once);
            }

            return scopes;
        }
    }

    private IReadOnlyList<TimeSpan> EffectiveDurations
    {
        get
        {
            var offered = DurationOptions is { Count: > 0 } ? DurationOptions : DefaultDurations;
            var durations = offered
                .Where(duration => duration > TimeSpan.Zero)
                .Distinct()
                .ToList();

            if (durations.Count == 0)
            {
                durations.AddRange(DefaultDurations);
            }

            return durations;
        }
    }

    private string SummaryText
    {
        get
        {
            if (State == PermissionRequestState.Expired)
            {
                return Loc["TmPermissionRequest_Expired"];
            }

            if (State == PermissionRequestState.Denied)
            {
                return Loc["TmPermissionRequest_Denied"];
            }

            if (_grant is not { } grant)
            {
                return string.Empty;
            }

            var remaining = RemainingText;
            if (remaining is null)
            {
                return ScopeSummary(grant);
            }

            return grant.Duration is { } window
                ? Loc["TmPermissionRequest_GrantedForRemaining", FormatDuration(window), remaining]
                : Loc["TmPermissionRequest_Remaining", remaining];
        }
    }

    private string StateIcon => State switch
    {
        PermissionRequestState.Granted => IconNames.CheckCircle,
        PermissionRequestState.Denied => IconNames.XCircle,
        PermissionRequestState.Expired => IconNames.Clock,
        _ => IconNames.Key,
    };

    private string RiskLabel => Risk switch
    {
        ApprovalRisk.Medium => Loc["TmPermissionRequest_RiskMedium"],
        ApprovalRisk.High => Loc["TmPermissionRequest_RiskHigh"],
        ApprovalRisk.Critical => Loc["TmPermissionRequest_RiskCritical"],
        _ => Loc["TmPermissionRequest_RiskLow"],
    };

    private string RiskIcon => Risk switch
    {
        ApprovalRisk.Medium => IconNames.AlertCircle,
        ApprovalRisk.High => IconNames.AlertTriangle,
        ApprovalRisk.Critical => IconNames.Shield,
        _ => IconNames.CheckCircle,
    };

    private BadgeVariant RiskBadgeVariant => Risk switch
    {
        ApprovalRisk.Medium => BadgeVariant.Warning,
        ApprovalRisk.High => BadgeVariant.Danger,
        ApprovalRisk.Critical => BadgeVariant.Danger,
        _ => BadgeVariant.Success,
    };

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _clock = ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        _ticker = new AiElapsedTicker(_clock, OnTickAsync);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Grant != _grantParameter)
        {
            _grantParameter = Grant;
            if (Grant is { } grant)
            {
                _grant = grant;
                _denied = false;
                _expired = false;
                _errorMessage = null;
            }
            else if (!_expired)
            {
                // A cleared grant that never ran out returns the request to pending. An EXPIRED grant
                // stays expired: the host echoes the expiry notification as a null parameter, and that
                // must not look like a fresh question.
                _grant = null;
                _denied = false;
            }
        }

        NormalizeSelection();
        EvaluateClock();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (NeedsTicking)
        {
            _ticker?.Start();
        }
        else
        {
            _ticker?.Stop();
        }

        if (_grantClearedPending)
        {
            _grantClearedPending = false;
            await RaiseGrantChangedAsync(null);
        }

        if (_focusTarget != FocusTarget.None)
        {
            var target = _focusTarget;
            _focusTarget = FocusTarget.None;
            await FocusAsync(target);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Grants the permission: builds the grant for the selected scope (and duration) and runs
    /// <see cref="OnDecision"/>. When the callback throws the request stays pending and the error is
    /// shown; a no-op when the request is not decidable.</summary>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public Task AllowAsync() => DecideAsync(isGrant: true);

    /// <summary>Denies the permission. On success the request collapses into its denial row; a
    /// throwing callback keeps it pending with the error shown.</summary>
    /// <returns>A task that completes when the decision has been applied or refused.</returns>
    public Task DenyAsync() => DecideAsync(isGrant: false);

    /// <summary>Revokes the permission in force: runs <see cref="OnRevoke"/> and, when it succeeds,
    /// returns the request to pending and reports the cleared grant. A throwing callback keeps the
    /// grant; a no-op when there is nothing to revoke or an expiry already ended it.</summary>
    /// <returns>A task that completes when the revoke has been applied or refused.</returns>
    public async Task RevokeAsync()
    {
        if (!ShowRevoke || _revoking)
        {
            return;
        }

        _revoking = true;
        _errorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            if (OnRevoke is not null)
            {
                await OnRevoke();
            }
        }
        catch (Exception)
        {
            _revoking = false;

            // The deadline may have passed while the revoke was being saved. The expiry is a fact
            // that no longer needs the host to succeed, so it wins over "try again" — exactly the
            // rule the approval card follows for its own in-flight decision.
            EvaluateClock();
            if (!_expired)
            {
                _errorMessage = Loc["TmPermissionRequest_Error"];
                _announcement = _errorMessage;
            }

            await InvokeAsync(StateHasChanged);
            return;
        }

        _revoking = false;

        // The deadline keeps running while the revoke is being saved, and NeedsTicking is off for the
        // whole of it, so nothing else re-evaluates the clock. Catching the expiry here — while the
        // grant is still set, which is what EvaluateClock reads — is what keeps a late revoke from
        // reopening the pending question a passed deadline already answered.
        EvaluateClock();

        _grant = null;
        _denied = false;
        _revokeHasFocus = false;

        if (_expired)
        {
            _announcement = Loc["TmPermissionRequest_AnnounceExpired"];
            _focusTarget = FocusTarget.Root;
        }
        else
        {
            _announcement = Loc["TmPermissionRequest_AnnounceRevoked"];
            _focusTarget = FocusTarget.Allow;
        }

        await InvokeAsync(StateHasChanged);

        // When the expiry was caught above (or by an earlier render) the cleared grant is already
        // queued through _grantClearedPending, or was announced then: reporting it again here would
        // describe one transition twice.
        if (!_expired)
        {
            await RaiseGrantChangedAsync(null);
        }
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    private async Task DecideAsync(bool isGrant)
    {
        if (!CanDecide)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var decision = isGrant
            ? PermissionDecision.Allow(BuildGrant(now), now)
            : PermissionDecision.Deny(now);

        _busy = true;
        _errorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            if (OnDecision is not null)
            {
                await OnDecision(decision);
            }
        }
        catch (Exception)
        {
            _busy = false;
            _errorMessage = Loc["TmPermissionRequest_Error"];
            _announcement = _errorMessage;
            await InvokeAsync(StateHasChanged);
            return;
        }

        _busy = false;
        _revokeHasFocus = false;

        if (isGrant)
        {
            _grant = decision.Grant;
            _denied = false;
            _expired = false;
            _announcement = Loc["TmPermissionRequest_AnnounceGranted"];
            _focusTarget = FocusTarget.Revoke;
        }
        else
        {
            _grant = null;
            _denied = true;
            _expired = false;
            _announcement = Loc["TmPermissionRequest_AnnounceDenied"];
            _focusTarget = FocusTarget.Root;
        }

        await InvokeAsync(StateHasChanged);

        // A denial grants nothing, so it is not a grant transition: reporting a null grant the host
        // already had would fire a change event nobody asked for.
        if (isGrant)
        {
            await RaiseGrantChangedAsync(decision.Grant);
        }
    }

    private PermissionGrant BuildGrant(DateTimeOffset now)
    {
        var scopes = EffectiveScopes;
        var scope = scopes.Contains(_selectedScope) ? _selectedScope : scopes[0];

        return scope switch
        {
            PermissionScope.ForDuration => new PermissionGrant(
                PermissionScope.ForDuration, now + SelectedDuration, now),
            _ => new PermissionGrant(scope),
        };
    }

    private TimeSpan SelectedDuration
    {
        get
        {
            var durations = EffectiveDurations;
            return durations.Contains(_selectedDuration) ? _selectedDuration : durations[0];
        }
    }

    // ── Clock ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Turns a grant whose deadline has passed into the expired row exactly once. The host hears about
    /// it as a cleared grant, which is the same shape a revoke produces — a permission that ran out and
    /// one that was withdrawn are the same fact for the caller.
    /// </summary>
    private void EvaluateClock()
    {
        if (_grant is null || _expired)
        {
            return;
        }

        if (!_grant.IsExpired(_clock.GetUtcNow()))
        {
            return;
        }

        _expired = true;
        _announcement = Loc["TmPermissionRequest_AnnounceExpired"];
        _grantClearedPending = true;

        // Expiring removes the Revoke button from the page. When that button owned the focus the browser
        // would drop it to <body> and the keyboard reader would lose their place, so hand it to the
        // group root — but only when the reader was actually on Revoke, never pulling the focus back
        // from wherever else they are working.
        if (_revokeHasFocus && _focusTarget == FocusTarget.None)
        {
            _focusTarget = FocusTarget.Root;
            _revokeHasFocus = false;
        }
    }

    private async Task OnTickAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                EvaluateClock();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer went away between the tick and the hop.
        }
        catch (InvalidOperationException)
        {
            // The renderer has already been torn down.
        }
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private void NormalizeSelection()
    {
        var scopes = EffectiveScopes;
        if (!_scopeSeeded || !scopes.Contains(_selectedScope))
        {
            _selectedScope = scopes[0];
            _scopeSeeded = true;
        }

        var durations = EffectiveDurations;
        if (!_durationSeeded || !durations.Contains(_selectedDuration))
        {
            _selectedDuration = durations[0];
            _durationSeeded = true;
        }
    }

    private void SelectScope(PermissionScope scope)
    {
        if (_selectedScope == scope)
        {
            return;
        }

        _selectedScope = scope;
    }

    private void SelectDuration(TimeSpan duration)
    {
        _selectedDuration = duration;
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (!EnableShortcuts || !CanDecide || args.Repeat || !(args.CtrlKey || args.MetaKey))
        {
            return;
        }

        if (args.Key == "Enter")
        {
            await AllowAsync();
        }
        else if (args.Key == "Backspace")
        {
            await DenyAsync();
        }
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    private string ScopeSummary(PermissionGrant grant) => grant.Scope switch
    {
        PermissionScope.Once => Loc["TmPermissionRequest_GrantedOnce"],
        PermissionScope.Session => Loc["TmPermissionRequest_GrantedSession"],
        PermissionScope.Always => Loc["TmPermissionRequest_GrantedAlways"],
        _ => grant.Duration is { } window
            ? Loc["TmPermissionRequest_GrantedFor", FormatDuration(window)]
            : Loc["TmPermissionRequest_GrantedFor", FormatDuration(SelectedDuration)],
    };

    /// <summary>A short, localized span: <c>1 h</c>, <c>1 h 5 min</c>, <c>42 min</c>, <c>30 s</c>.</summary>
    private string FormatDuration(TimeSpan duration)
    {
        if (duration >= TimeSpan.FromHours(1))
        {
            var hours = (int)duration.TotalHours;
            return duration.Minutes == 0
                ? Loc["TmPermissionRequest_DurationHours", hours]
                : Loc["TmPermissionRequest_DurationHoursMinutes", hours, duration.Minutes];
        }

        if (duration >= TimeSpan.FromMinutes(1))
        {
            return Loc["TmPermissionRequest_DurationMinutes", (int)duration.TotalMinutes];
        }

        return Loc["TmPermissionRequest_DurationSeconds", Math.Max(1, (int)duration.TotalSeconds)];
    }

    private string ScopeLabel(PermissionScope scope) => scope switch
    {
        PermissionScope.ForDuration => Loc["TmPermissionRequest_ScopeForDuration"],
        PermissionScope.Session => Loc["TmPermissionRequest_ScopeSession"],
        PermissionScope.Always => Loc["TmPermissionRequest_ScopeAlways"],
        _ => Loc["TmPermissionRequest_ScopeOnce"],
    };

    private static string ScopeModifier(PermissionScope scope) => scope switch
    {
        PermissionScope.ForDuration => "for-duration",
        PermissionScope.Session => "session",
        PermissionScope.Always => "always",
        _ => "once",
    };

    // ── Focus ────────────────────────────────────────────────────────────────

    private async Task FocusAsync(FocusTarget target)
    {
        var element = target switch
        {
            FocusTarget.Allow => _allowElement,
            FocusTarget.Revoke => _revokeElement,
            _ => _rootElement,
        };

        try
        {
            await element.FocusAsync(preventScroll: true);
        }
        catch (Microsoft.JSInterop.JSDisconnectedException)
        {
            // Blazor Server: the circuit dropped, so there is no focus to move.
        }
        catch (Microsoft.JSInterop.JSException)
        {
            // The element left the DOM between the render and the focus call — losing the focus move
            // is better than tearing the circuit down.
        }
        catch (InvalidOperationException)
        {
            // No renderer, or the element reference is no longer attached.
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task RaiseGrantChangedAsync(PermissionGrant? grant)
        => GrantChanged.HasDelegate ? GrantChanged.InvokeAsync(grant) : Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ticker?.Dispose();
        _ticker = null;
    }

    private enum FocusTarget
    {
        None,
        Root,
        Allow,
        Revoke,
    }
}
