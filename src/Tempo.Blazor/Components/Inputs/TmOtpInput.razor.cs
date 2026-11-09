using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// Partial class backing <see cref="TmOtpInput"/>: per-cell model state, value normalization and
/// the lazy ES-module interop. All DOM input handling (filtering, composition, navigation,
/// auto-advance, focus redirect) lives in <c>tm-otp-input.js</c>; the module reports edits through
/// <see cref="OnCellInputAsync"/> and <see cref="DistributeFromAsync"/> and receives corrections
/// back through <c>setCellValue</c>.
/// </summary>
public partial class TmOtpInput : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-otp-input.js";
    private const int MinLength = 4;
    private const int MaxLength = 10;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The complete verification code. Equals the concatenation of all cell values.</summary>
    [Parameter] public string Value { get; set; } = string.Empty;

    /// <summary>Fires when the code changes (every accepted or removed character).</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Number of cells. Must be between 4 and 10. Defaults to 6.</summary>
    [Parameter] public int Length { get; set; } = 6;

    /// <summary>Which characters are accepted and which keyboard is presented. Defaults to <see cref="OtpInputKind.Numeric"/>.</summary>
    [Parameter] public OtpInputKind Kind { get; set; } = OtpInputKind.Numeric;

    /// <summary>
    /// Renders an <c>aria-hidden</c> separator between groups of this many cells (0 = no separators,
    /// the default). Values of <see cref="Length"/> or more render no separators.
    /// </summary>
    [Parameter] public int GroupSize { get; set; }

    /// <summary>Renders every cell as <c>type="password"</c> so the code is hidden while typing.</summary>
    [Parameter] public bool Masked { get; set; }

    /// <summary>Disables every cell and applies the <c>tm-otp-input--disabled</c> root modifier.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Renders every cell <c>readonly</c> and applies the <c>tm-otp-input--readonly</c> root
    /// modifier: the code stays visible and focusable but cannot be edited.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// Error state: applies the <c>tm-otp-input--invalid</c> root modifier (with the shake
    /// animation), sets <c>aria-invalid="true"</c> and an <c>aria-describedby</c> pointing at the
    /// <see cref="ErrorText"/> message on the group and every cell. Setting
    /// <c>Invalid="true"</c> WITHOUT <see cref="ErrorText"/> is the supported way to surface a
    /// message owned by a surrounding <c>TmFormField</c> (the field error is already reachable
    /// through the host <see cref="AriaDescribedBy"/>). The message text is announced through a
    /// single always-rendered polite live region; call <see cref="ShowErrorAsync"/> to replay the
    /// shake and re-announce an unchanged error.
    /// </summary>
    [Parameter] public bool Invalid { get; set; }

    /// <summary>
    /// Error message rendered below the input (and announced politely) while
    /// <see cref="Invalid"/> is <c>true</c>. Localize it through <c>ITmLocalizer</c>; null or
    /// empty renders no error element and adds no <c>{Id}-error</c> reference — use that when a
    /// surrounding <c>TmFormField</c> renders the message instead.
    /// </summary>
    [Parameter] public string? ErrorText { get; set; }

    /// <summary>
    /// Verified state: blocks editing (the cells become <c>readonly</c>), applies the
    /// <c>tm-otp-input--verified</c> root modifier and renders the localized verified status
    /// (check icon + text) below the input, referenced from every cell through
    /// <c>aria-describedby="{Id}-status"</c> and announced through the polite live region.
    /// </summary>
    [Parameter] public bool Verified { get; set; }

    /// <summary>
    /// Optional text of the verified status row (additive): overrides the localized
    /// <c>TmOtpInput_VerifiedStatus</c> default in the status element AND in the polite live
    /// region, so a host with its own verified wording (e.g. a signing step reusing the host's
    /// "Verified" key) keeps ONE consistent text everywhere.
    /// </summary>
    [Parameter] public string? VerifiedText { get; set; }

    /// <summary>Moves focus to the first cell after the first render.</summary>
    [Parameter] public bool AutoFocus { get; set; }

    /// <summary>HTML id attribute of the root group. Defaults to a generated unique id.</summary>
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>Additional CSS class(es) applied to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// <c>aria-label</c> of the group. Defaults to the localized "Verification code" label;
    /// ignored when <see cref="AriaLabelledBy"/> is set (the reference wins per WAI-ARIA).
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary><c>aria-labelledby</c> of the group. When set, <see cref="AriaLabel"/> is not rendered.</summary>
    [Parameter] public string? AriaLabelledBy { get; set; }

    /// <summary><c>aria-describedby</c> of the group (e.g. a hint or error message id).</summary>
    [Parameter] public string? AriaDescribedBy { get; set; }

    /// <summary>
    /// Fires exactly once each time the code transitions from incomplete to complete. Clearing a
    /// character and completing the code again fires it again. A host-supplied pre-filled
    /// <see cref="Value"/> never fires it — only edits made after the component rendered do.
    /// </summary>
    [Parameter] public EventCallback<string> OnComplete { get; set; }

    /// <summary>Additional attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Id of the first cell input (<c>{Id}-cell-0</c>). Use it to label or focus the input.</summary>
    public string InputId => $"{Id}-cell-0";

    private string[] _cells = Array.Empty<string>();
    private ElementReference _rootElement;
    private DotNetObjectReference<TmOtpInput>? _self;
    private bool _complete;
    private bool _attached;
    private Task<IJSObjectReference>? _moduleTask;

    private string RootCssClass
    {
        get
        {
            var builder = new System.Text.StringBuilder("tm-otp-input");
            if (Disabled)
            {
                builder.Append(" tm-otp-input--disabled");
            }

            if (ReadOnly)
            {
                builder.Append(" tm-otp-input--readonly");
            }

            if (Invalid)
            {
                builder.Append(" tm-otp-input--invalid");
            }

            if (Verified)
            {
                builder.Append(" tm-otp-input--verified");
            }

            if (!string.IsNullOrWhiteSpace(Class))
            {
                builder.Append(' ').Append(Class);
            }

            return builder.ToString();
        }
    }

    private string? GroupAriaLabel => AriaLabelledBy is null ? AriaLabel ?? Loc["TmOtpInput_GroupLabel"] : null;

    private string? GroupAriaDescribedBy
    {
        get
        {
            var errorId = Invalid && !string.IsNullOrEmpty(ErrorText) ? ErrorId : null;
            if (errorId is null)
            {
                return AriaDescribedBy;
            }

            return string.IsNullOrEmpty(AriaDescribedBy) ? errorId : $"{AriaDescribedBy} {errorId}";
        }
    }

    private string ErrorId => $"{Id}-error";

    private string StatusId => $"{Id}-status";

    /// <summary>
    /// Text announced through the single polite live region: the error while invalid, the
    /// verified status while verified, empty otherwise. <see cref="ShowErrorAsync"/> clears it
    /// for one render so an unchanged message is re-announced.
    /// </summary>
    private string? _liveTextOverride;

    private string LiveText => _liveTextOverride
        ?? (Invalid && !string.IsNullOrEmpty(ErrorText) ? ErrorText
            : Verified ? VerifiedStatusText
            : string.Empty);

    /// <summary>The verified status text in effect: the host's <see cref="VerifiedText"/> first,
    /// then the localized default.</summary>
    private string VerifiedStatusText
        => !string.IsNullOrEmpty(VerifiedText) ? VerifiedText! : Loc["TmOtpInput_VerifiedStatus"];

    /// <summary>
    /// <c>aria-describedby</c> of every cell: the host reference, the error message id while
    /// <see cref="Invalid"/> renders one and the verified status id while <see cref="Verified"/>.
    /// </summary>
    private string? CellAriaDescribedBy
    {
        get
        {
            var refs = new List<string>(3);
            if (!string.IsNullOrEmpty(AriaDescribedBy))
            {
                refs.Add(AriaDescribedBy!);
            }

            if (Invalid && !string.IsNullOrEmpty(ErrorText))
            {
                refs.Add(ErrorId);
            }

            if (Verified)
            {
                refs.Add(StatusId);
            }

            return refs.Count == 0 ? null : string.Join(' ', refs);
        }
    }

    private bool CellReadOnly => ReadOnly || Verified;

    private string CellInputType => Masked ? "password" : "text";

    private string InputMode => Kind == OtpInputKind.Numeric ? "numeric" : "text";

    private string CellId(int index) => $"{Id}-cell-{index}";

    private string CellAriaLabel(int index) => Kind == OtpInputKind.Numeric
        ? Loc["TmOtpInput_DigitLabel", index + 1, Length]
        : Loc["TmOtpInput_CharacterLabel", index + 1, Length];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Length < MinLength || Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Length), Length,
                $"{nameof(TmOtpInput)}.{nameof(Length)} must be between {MinLength} and {MaxLength}.");
        }

        if (GroupSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(GroupSize), GroupSize,
                $"{nameof(TmOtpInput)}.{nameof(GroupSize)} cannot be negative.");
        }

        if (_cells.Length != Length)
        {
            var resized = Enumerable.Repeat(string.Empty, Length).ToArray();
            Array.Copy(_cells, resized, Math.Min(_cells.Length, Length));
            _cells = resized;
        }

        // Host-driven value (two-way bounce, reset, prefill): only rewrite the cells when the
        // incoming value disagrees with what the cells already hold. A pre-filled complete Value
        // only seeds _complete — it must not fire OnComplete.
        var incoming = Normalize(Value);
        if (!string.Equals(incoming, CurrentValue(), StringComparison.Ordinal))
        {
            _cells = Enumerable.Repeat(string.Empty, Length).ToArray();
            for (var i = 0; i < incoming.Length; i++)
            {
                _cells[i] = incoming[i].ToString();
            }

            _complete = incoming.Length == Length;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await AttachAsync();
        if (AutoFocus)
        {
            await FocusCellAsync(0, select: true);
        }
    }

    /// <summary>
    /// Called by the module when a single cell changed: <paramref name="value"/> is the new cell
    /// content (one accepted character or an empty string when the cell was cleared). The module
    /// already filtered the character per <see cref="Kind"/>; this method is the defensive model
    /// application plus the value/completion callbacks.
    /// </summary>
    /// <param name="index">Cell index (0-based).</param>
    /// <param name="value">New cell content (0 or 1 accepted characters).</param>
    /// <returns>A task that completes when the model and the DOM are in sync.</returns>
    [JSInvokable]
    public async Task OnCellInputAsync(int index, string value)
    {
        if (index < 0 || index >= Length || IsEditingBlocked)
        {
            return;
        }

        var accepted = NormalizeCell(value);
        if (string.Equals(_cells[index], accepted, StringComparison.Ordinal))
        {
            return;
        }

        _cells[index] = accepted;
        await RaiseChangeAsync();
        await SyncCellAsync(index);
        StateHasChanged();
    }

    /// <summary>
    /// Called by the module for a multi-character insert (paste, autofill, drop) starting at
    /// <paramref name="index"/>: the accepted characters are distributed over consecutive cells
    /// from that index on (truncated at the last cell) and focus moves behind the last written
    /// cell. Paste text containing spaces or dashes is cleaned by the same normalization as a
    /// host-supplied <see cref="Value"/>.
    /// </summary>
    /// <param name="index">Cell index the insert started at.</param>
    /// <param name="raw">Inserted text (already filtered per Kind by the module).</param>
    /// <returns>A task that completes when the model and the DOM are in sync.</returns>
    [JSInvokable]
    public async Task DistributeFromAsync(int index, string raw)
    {
        if (index < 0 || index >= Length || IsEditingBlocked)
        {
            return;
        }

        var accepted = Normalize(raw);
        var touched = false;
        for (var i = 0; i < accepted.Length && index + i < Length; i++)
        {
            var target = index + i;
            if (_cells[target] != accepted[i].ToString())
            {
                _cells[target] = accepted[i].ToString();
                touched = true;
            }
        }

        await SyncCellsAsync();
        if (touched)
        {
            await RaiseChangeAsync();
        }

        var next = Array.IndexOf(_cells, string.Empty, Math.Min(index + accepted.Length, Length - 1));
        if (next >= 0)
        {
            await FocusCellAsync(next, select: true);
        }

        StateHasChanged();
    }

    private async Task RaiseChangeAsync()
    {
        var value = CurrentValue();
        var complete = value.Length == Length;
        // Decide BEFORE any await: an OnComplete handler may reset Value through a bound host,
        // re-enter OnParametersSet and clear _complete; the flag must reflect the transition that
        // just happened, not overwrite the re-entrant reset afterwards.
        var fire = complete && !_complete;
        _complete = complete;

        await ValueChanged.InvokeAsync(value);
        if (fire)
        {
            await OnComplete.InvokeAsync(value);
        }
    }

    private string CurrentValue() => string.Concat(_cells);

    /// <summary>The module already guards the DOM; the model must not accept edits either.</summary>
    private bool IsEditingBlocked => Disabled || ReadOnly || Verified;

    private bool IsAccepted(char c) => Kind == OtpInputKind.Numeric
        ? c is >= '0' and <= '9'
        : c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private char NormalizeChar(char c)
        => Kind == OtpInputKind.Alphanumeric ? char.ToUpperInvariant(c) : c;

    /// <summary>Takes the first accepted character of a raw cell value (empty string when none).</summary>
    private string NormalizeCell(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        for (var i = 0; i < raw.Length; i++)
        {
            if (IsAccepted(raw[i]))
            {
                return NormalizeChar(raw[i]).ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>Filters a host-supplied <see cref="Value"/> down to accepted characters.</summary>
    private string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var chars = new char[Math.Min(raw.Length, Length)];
        var count = 0;
        foreach (var c in raw)
        {
            if (IsAccepted(c))
            {
                chars[count++] = NormalizeChar(c);
                if (count == Length)
                {
                    break;
                }
            }
        }

        return new string(chars, 0, count);
    }

    private Task<IJSObjectReference> Module => _moduleTask ??=
        JS.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    private async Task AttachAsync()
    {
        try
        {
            _self ??= DotNetObjectReference.Create(this);
            var module = await Module;
            await module.InvokeVoidAsync("attach", _rootElement, _self,
                new { inputMethod = nameof(OnCellInputAsync), distributeMethod = nameof(DistributeFromAsync) });
            _attached = true;
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
            // The circuit or the element is gone before the attach landed — nothing to wire.
        }
    }

    /// <summary>Pushes a C#-corrected cell value into the DOM (never raises events).</summary>
    private async Task SyncCellAsync(int index)
    {
        try
        {
            var module = await Module;
            await module.InvokeVoidAsync("setCellValue", _rootElement, index, _cells[index]);
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
        }
    }

    /// <summary>Pushes the whole model into the DOM in one batched call (never raises events).</summary>
    private async Task SyncCellsAsync()
    {
        try
        {
            var module = await Module;
            await module.InvokeVoidAsync("setCellValues", _rootElement, (object)_cells);
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
        }
    }

    private async Task FocusCellAsync(int index, bool select)
    {
        if (Disabled)
        {
            return;
        }

        try
        {
            var module = await Module;
            await module.InvokeVoidAsync(select ? "selectCell" : "focusCell", _rootElement, index);
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
            // The circuit/runtime/element is gone; there is nothing left to focus.
        }
    }

    /// <summary>
    /// Moves focus to the first empty cell (selecting it); a complete code focuses the first cell.
    /// No-op while <see cref="Disabled"/> — readOnly and Verified cells stay focusable.
    /// </summary>
    /// <returns>A task that completes when the focus request was handed to the module.</returns>
    public async Task FocusAsync()
    {
        var index = Array.IndexOf(_cells, string.Empty);
        await FocusCellAsync(index < 0 ? 0 : index, select: true);
    }

    /// <summary>
    /// Clears every cell: raises <see cref="ValueChanged"/> with the empty string, pushes the
    /// cleared values into the DOM and re-arms <see cref="OnComplete"/> so completing the code
    /// again fires it anew. No-op while editing is blocked.
    /// </summary>
    /// <returns>A task that completes when the model and the DOM are in sync.</returns>
    public async Task ClearAsync()
    {
        if (IsEditingBlocked)
        {
            return;
        }

        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = string.Empty;
        }

        await RaiseChangeAsync();
        await SyncCellsAsync();

        StateHasChanged();
    }

    /// <summary>
    /// Re-triggers the invalid shake and re-announces the error message. Call it after (re)applying
    /// <see cref="Invalid"/>/<see cref="ErrorText"/> when the same error is already shown: the
    /// shake replays via a JS reflow and the live region re-announces the message (it is cleared
    /// for one render and then restored, because a polite live region only announces CHANGES).
    /// </summary>
    /// <returns>A task that completes when the shake was handed to the module and the text restored.</returns>
    public async Task ShowErrorAsync()
    {
        _liveTextOverride = string.Empty;
        StateHasChanged();
        await Task.Yield();
        _liveTextOverride = null;
        StateHasChanged();
        await RestartShakeAsync();
    }

    private async Task RestartShakeAsync()
    {
        try
        {
            var module = await Module;
            await module.InvokeVoidAsync("restartShake", _rootElement);
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
            // A torn-down circuit has no animation left to restart.
        }
    }

    private static bool IsInteropTeardown(Exception ex)
        => ex is JSException or InvalidOperationException or TaskCanceledException or ObjectDisposedException;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_moduleTask is { } task)
            {
                var module = await task;
                if (_attached)
                {
                    await module.InvokeVoidAsync("detach", _rootElement);
                }

                await module.DisposeAsync();
            }
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
            // A torn-down circuit has nothing left to release.
        }
        finally
        {
            _self?.Dispose();
        }
    }
}
