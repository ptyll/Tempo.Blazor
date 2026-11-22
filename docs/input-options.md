# TmInputOptions — when text inputs commit

> **Status:** proposed for 2.13.0; implementation (steps C–E) is gated on owner approval of
> [ADR-0008](adr/0008-input-options-immediate.md). Until then the types and provider do not exist
> and today's `TmTextArea` / `TmTextInput` behaviour is unchanged.
> **Proposal:** [docs/proposals/input-options-immediate.md](proposals/input-options-immediate.md).

Host-wide (DI) and per-tree (`TmInputOptionsProvider`) defaults for whether a text input raises
`ValueChanged` on every `input` event or only on `change`/`blur`. The resolve order is:

1. the component's explicit parameter,
2. the cascaded `TmInputOptions`,
3. `IOptions<TmInputOptions>` from `AddTempoBlazor` / `Configure<TmInputOptions>`,
4. the component's own fallback.

`ImmediateByDefault` is `bool?` (default `null` = not set). Without options nothing changes.

## Which inputs are governed

| Component | Governed? | Why |
|-----------|-----------|-----|
| **`TmTextArea`** | **Yes** | Already has `Immediate` (`bool`, default `false`): `ValueChanged` on change/blur, or on input when true. Keeps `bool Immediate`; additive `bool? ImmediateMode` is the explicit override (`true`/`false`). `Immediate="true"` always wins. Default / `Immediate="false"` does **not** block options (otherwise a global default could never turn TextArea on). Forced blur-commit under `ImmediateByDefault=true` is `ImmediateMode="false"` only. Fallback stays `false`. |
| **`TmTextInput`** | **Yes** | No `Immediate` today; both `HandleInputAsync` and `HandleChangeAsync` raise `ValueChanged`. Additive `bool? Immediate`: `null` or `true` = today's every-keystroke behaviour; `false` = change/blur only. Options change it only when `ImmediateByDefault = false`. Fallback stays `true`. |
| **`TmNumberInput`** | **No** | Only `@onchange`. It does not distinguish input from change, so Immediate would invent a channel. |
| **`TmSearchInput`** | **No** | Own `DebounceMs` model on input, with change cancelling the pending timer. Immediate would collide with debounce. |
| **`TmDecimalInput`** | **No** | Distinguishes input (`_liveText`) from change (parse + culture format). Committing on every keystroke would break in-progress text (`"1,"`, `"1.2e"`) and culture-aware formatting. |
| Other inputs (`TmSelect`, `TmCheckbox`, `TmToggle`, `TmOtpInput`, pickers, …) | **No** | Each has its own commit model; ImmediateByDefault is about text `input` vs `change`. |

This list is closed for 2.13. Extending it is a separate decision.

## Compatibility

Changing `TmTextArea.Immediate` from `bool` to `bool?` is Razor-assignment compatible (`Immediate="true"`)
but **breaks C# reads** (`if (ta.Immediate)`) and the binary signature. 2.13 ships no Breaking section,
so the `bool` stays and `ImmediateMode` is additive. See the ADR.
