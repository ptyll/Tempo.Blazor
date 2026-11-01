# TmPermissionRequest

The agent's request for one concrete permission: the tool it wants to use, the target it wants to
reach, why it needs it, a localized risk badge, the scopes the host is willing to offer, and an
Allow / Deny decision with an optional duration. A decision collapses the request into a result row; a
granted permission keeps a live countdown and offers Revoke until it runs out.

Plan: `10b3966f` (human-in-the-loop, phase 5). Package: `Tempo.Blazor.AI`.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmPermissionRequest ToolName="web.search"
                     ToolDisplayName="Web search"
                     Target="https://example.com/pricing"
                     Reason="The agent needs the current pricing page to answer."
                     Risk="ApprovalRisk.Medium"
                     @bind-Grant="grant"
                     OnDecision="HandleDecisionAsync"
                     OnRevoke="RevokeAsync" />

@code {
    private PermissionGrant? grant;

    private async Task HandleDecisionAsync(PermissionDecision decision)
    {
        if (decision.IsGranted)
        {
            await store.SaveAsync(decision.Grant!);   // scope + ExpiresAt belong to the host
        }

        // Persisted; the bound Grant mirrors the decision (and its later expiry / revoke).
    }

    private Task RevokeAsync() => store.RevokeAsync();
}
```

## States

| `data-state` | Request |
|---|---|
| `pending` | Tool, target, reason, risk badge, the scope `radiogroup`, the duration `radiogroup` (only for `ForDuration`), Allow / Deny. |
| `granted` | Collapsed row: `Allowed for 1 h · 42 min left` and Revoke. |
| `denied` | Collapsed row: `Denied`. |
| `expired` | Collapsed row: `Expired`. No Revoke — there is nothing left to revoke. |

The pending half and the decided half are BOTH mounted so the collapse can transition the height
(`grid-template-rows: 1fr` → `0fr`); the inactive half carries `inert`, so it leaves the tab order and
the accessibility tree. A host that reuses one instance for a **different** request renders it with
`@key="requestId"` — a `Grant` parameter change is treated as a new request, and an expired grant
deliberately does not come back when the host echoes the expiry notification as `Grant="@null"`.

## Scopes

`ScopeOptions` is the only source of the offered scopes, in the host's order. `null` offers all four;
an explicitly empty (or entirely invalid) list falls back to `PermissionScope.Once`, so a host that
filtered its allowed scopes down to nothing never accidentally offers "always".

| `PermissionScope` | Label (en) | Grant |
|---|---|---|
| `Once` | Allow once | no expiry |
| `ForDuration` | Allow for a while | `ExpiresAt = now + the picked duration` |
| `Session` | Allow for this session | no expiry (the host ends the session) |
| `Always` | Always allow | no expiry |

`DurationOptions` (default 15 minutes and one hour) feeds the second `radiogroup`, which appears only
while `ForDuration` is selected. Values that are zero or negative are ignored; an empty list falls back
to the default, because a timed permission always needs a length. To forbid the timed scope, exclude it
from `ScopeOptions`.

## Decisions and the clock

`OnDecision` receives a `PermissionDecision`:

```csharp
public sealed record PermissionDecision
{
    bool IsGranted { get; }
    bool IsDenied => !IsGranted;
    PermissionGrant? Grant { get; }        // always set for a grant, always null for a denial
    PermissionScope Scope => Grant?.Scope ?? PermissionScope.Once;
    DateTimeOffset? DecidedAt { get; }

    static PermissionDecision Allow(PermissionGrant grant, DateTimeOffset? decidedAt = null);
    static PermissionDecision Deny(DateTimeOffset? decidedAt = null);
}
```

The request leaves `pending` only when the callback completes without throwing: while it runs both
actions are disabled and the request is `aria-busy`; when it throws, the localized error is shown and
the request stays decidable. `Deny` collapses the request immediately — a denial does not depend on a
later save to be honest about not granting anything, but a throw still keeps the request open.

A granted permission counts down on the injected `TimeProvider` (the package's `AiElapsedTicker`,
armed only while there is something to count). When `Grant.ExpiresAt` passes, the row becomes
`expired`, the expiry is announced once and `GrantChanged` reports a **cleared** grant — for the host a
permission that ran out and one that was withdrawn are the same fact. Revoke raises `OnRevoke`; without
a callback it still clears the grant locally. A failing revoke keeps the grant and shows the error.

A deadline that passes **while `OnRevoke` is still saving** wins over the revoke: the request ends
`expired`, never back in `pending`, even when the host re-renders nothing in between (the revoke
re-evaluates the clock itself; the ticker is paused for the duration of the save). The host hears the
cleared grant exactly **once** for that transition — the expiry is not announced again by the revoke
that finished after it, and a failed revoke on an expired grant reports the expiry rather than the
save error.

`OnDecision` and `OnRevoke` are `Func<…, Task>` rather than `EventCallback`, unlike the event-style
hooks of `TmAiMessage` (`OnRetry`, `OnFeedback`). A decision is a **command**, not a UI event: the
request has to await the host's save before it leaves `pending`, and a throw is a modelled outcome
(the card keeps the question and shows the error) rather than something the renderer should own. Only
the grant is two-way bound (`Grant`/`GrantChanged`).

## Grant

```csharp
public sealed record PermissionGrant
{
    PermissionGrant(PermissionScope scope, DateTimeOffset? expiresAt = null, DateTimeOffset? grantedAt = null);

    PermissionScope Scope { get; }
    DateTimeOffset? ExpiresAt { get; }
    DateTimeOffset? GrantedAt { get; }
    TimeSpan? Duration => ExpiresAt - GrantedAt;   // null when either end is unknown
    bool IsExpired(DateTimeOffset now);
    TimeSpan? Remaining(DateTimeOffset now);
}
```

A timed grant must name its `ExpiresAt`; `Once` and `Always` must not carry one; `Session` may. Passing
`GrantedAt` is what lets the row read "Allowed for 1 h · 42 min left" instead of a bare countdown —
without it the row says only "42 min left".

## Accessibility

- The scope and the duration are native radio groups (`role="radiogroup"` with a visible
  `aria-labelledby` label): one tab stop each, arrow keys for free, no JavaScript.
- The risk is always spelled out ("High risk") next to the badge colour — colour is the second channel.
- Exactly ONE polite live region carries every announcement (granted / denied / revoked / expired /
  error); the ticking countdown is `aria-live="off"`.
- The collapse moves focus to the surviving control — Revoke, else the group root (`tabindex="-1"`) —
  and Revoke hands it back to Allow, so the keyboard never falls onto a removed button. The same rescue
  runs when a grant expires on its own: if Revoke owned the focus when the deadline passed (the button
  is then removed from the page), focus moves to the group root. When the reader is anywhere else, the
  expiry moves nothing.
- `Ctrl`/`⌘`+`Enter` allows and `Ctrl`/`⌘`+`Backspace` denies while focus is inside the request,
  advertised through `aria-keyshortcuts` and the visible keyboard hint; `EnableShortcuts="false"`
  removes both. The component has no text field, so there is no typing conflict to fence.
- `:focus-visible` uses `--tm-focus-ring` (and a `forced-colors` `Highlight` outline on every focus
  stop), every control reaches `--tm-touch-target` on a coarse pointer, and the collapse transition is
  switched off under `prefers-reduced-motion`.

## Contract notes

- The alert/badge colours are always paired with words; the countdown takes the danger colour only in
  the `expired` state.
- Localization: 40 `TmPermissionRequest_*` keys in all four resource tables; short duration units
  ("1 h", "42 min", "30 s") are templates, so a language can reorder them.
- Styling: `_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` (the `_ai-permission.css` partial). Every
  rule reads `--tm-*` tokens; there is no JS in this component.
- Wireframe: the `permission-request` role with a declarative stencil in the `Feedback` category.
