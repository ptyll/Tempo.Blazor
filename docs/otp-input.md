# TmOtpInput

Segmented one-time-code input (`Tempo.Blazor.Components.Inputs`). Renders a `role="group"` of
`Length` (4–10, default 6) single-character cells with per-cell labels, paste/autofill
distribution, an optional masked PIN mode, grouping separators and invalid/verified states.

All DOM input handling lives in the `tm-otp-input.js` ES module (attached on first render,
detached in `DisposeAsync`): Blazor owns the model and the callbacks, the browser owns the
keystrokes. Demo: `/otp-input` in the demo app.

## Usage

```razor
@* Numeric 6-digit code with verification states *@
<TmOtpInput @bind-Value="_code" Length="6" OnComplete="VerifyAsync"
            Invalid="_invalid" ErrorText="Incorrect code" Verified="_verified" />

@* Alphanumeric backup code, grouped 4+4, uppercased automatically *@
<TmOtpInput @bind-Value="_backup" Length="8" Kind="OtpInputKind.Alphanumeric" GroupSize="4" />

@* Masked PIN *@
<TmOtpInput @bind-Value="_pin" Length="4" Masked="true" />
```

```csharp
private async Task VerifyAsync(string code)
{
    var ok = await VerifyOnServerAsync(code);
    _verified = ok;
    _invalid = !ok;
    if (_invalid)
    {
        // Replay the shake + re-announce an unchanged error, then let the user retry.
        await _otp.ShowErrorAsync();
        await _otp.ClearAsync();
        await _otp.FocusAsync();
    }
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Value` / `ValueChanged` | `string` / `EventCallback<string>` | `""` | The complete code (concatenation of all cells). |
| `Length` | `int` | `6` | Cell count. Must be 4–10 (`ArgumentOutOfRangeException` otherwise). |
| `Kind` | `OtpInputKind` | `Numeric` | `Numeric` accepts ASCII digits and presents `inputmode="numeric"` + `pattern="[0-9]*"`; `Alphanumeric` accepts A–Z 0–9 and uppercases automatically. |
| `GroupSize` | `int` | `0` | Renders an aria-hidden separator between groups of N cells (0 = none). |
| `Masked` | `bool` | `false` | Every cell renders `type="password"` (PIN entry). |
| `Disabled` | `bool` | `false` | Disables every cell (`tm-otp-input--disabled`). |
| `ReadOnly` | `bool` | `false` | Cells stay focusable but cannot be edited. |
| `Invalid` | `bool` | `false` | Error state: shake animation, `aria-invalid="true"`, `aria-describedby` on the group and every cell. |
| `ErrorText` | `string?` | `null` | Error message rendered below the input and announced politely. `Invalid` without `ErrorText` is the supported way to surface a surrounding `TmFormField`-owned message. |
| `Verified` | `bool` | `false` | Verified state: cells become readonly, a check-icon status row renders, referenced from every cell via `aria-describedby="{Id}-status"`. |
| `VerifiedText` | `string?` | `null` | Optional override of the localized verified wording (host's own "Verified" label). |
| `AutoFocus` | `bool` | `false` | Focuses the first cell after the first render. |
| `OnComplete` | `EventCallback<string>` | — | Fires exactly once per incomplete→complete transition; host resets inside the handler re-arm it. A pre-filled `Value` never fires it. |
| `Id` | `string` | generated | Root group id; cell ids are `{Id}-cell-{i}`; `InputId` = first cell. |
| `AriaLabel` / `AriaLabelledBy` / `AriaDescribedBy` | `string?` | localized / `null` | Group labelling/describing. |
| `Class`, `AdditionalAttributes` | — | — | Root class / unmatched attributes. |

## Public methods

| Method | Description |
|--------|-------------|
| `FocusAsync()` | Focuses the first empty cell (selecting it); a complete code focuses the first cell. No-op while `Disabled`. |
| `ClearAsync()` | Clears every cell, raises `ValueChanged` with `""` and re-arms `OnComplete`. |
| `ShowErrorAsync()` | Replays the invalid shake and re-announces an unchanged error through the polite live region (clear-then-set). |

## Keyboard & input behaviour

- `beforeinput` filters characters per `Kind`; the inserted character wins on overwrite
  (caret-before insert); IME composition is skipped and committed on `compositionend`.
- Multi-character inserts (paste, autofill, drop) are distributed from the edited cell through
  `DistributeFromAsync`; focus moves behind the last written cell.
- Paste is consumed in JS (the `paste` event's text is not available across the Blazor
  boundary), cleaned (spaces/dashes dropped, per-`Kind` filtering) and distributed; a pasted
  prefix ("Your code: AB12CD") prefers the last run matching the remaining length.
- Browser/SMS autofill (`autocomplete="one-time-code"` on the first cell) writes the whole code
  in one `input` event without a usable `beforeinput` payload — handled without a caret
  round-trip.
- Arrows, Home/End move between cells; typing auto-advances; Backspace on an empty cell steps
  back and selects. Pointer focus landing past an empty cell redirects to the first empty cell.
- Cells are 44 px targets on coarse pointers.

## Accessibility

- `role="group"` with a localized `aria-label` ("Verification code"); every cell has its own
  label ("Digit 3 of 6" / "Character 3 of 8").
- ONE always-rendered polite live region (`role="status"`) carries the error/verified text —
  empty on first render, so the states are announced on change.
- `Invalid` sets `aria-invalid="true"` and an `aria-describedby` chain to the error element on
  the group and every cell; `Verified` references the status element the same way.

## States and CSS

| Class | Meaning |
|-------|---------|
| `tm-otp-input` | Root group. |
| `tm-otp-input__cell` | A cell `<input>`. |
| `tm-otp-input__separator` | Aria-hidden group separator. |
| `tm-otp-input__error` | Error message (`Invalid` + `ErrorText`). |
| `tm-otp-input__status` | Verified status row. |
| `tm-otp-input--disabled` / `--readonly` / `--invalid` / `--verified` | Root modifiers. |

Invalid uses the danger tokens with a `forced-colors` fallback (a 2 px `CanvasText` border — the
state boundary under forced colors, WCAG 1.4.11); verified uses the success tokens the same way.
The shake animation is enumerated in the `animations.css` reduced-motion block.

## Integration

`TmSigningPhoneStep` (`Tempo.Blazor.Signing`) can swap its single code input for `TmOtpInput`
via `CodeInputMode="Segmented"` (+ `CodeLength`) — see the Signing docs and the
`/signing-components` demo (demo verifier code "123456").
