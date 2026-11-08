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
            var baseClass = $"tm-otp-input{(Disabled ? " tm-otp-input--disabled" : "")}";
            return string.IsNullOrWhiteSpace(Class) ? baseClass : $"{baseClass} {Class}";
        }
    }

    private string? GroupAriaLabel => AriaLabelledBy is null ? AriaLabel ?? Loc["TmOtpInput_GroupLabel"] : null;

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
        if (index < 0 || index >= Length)
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
    /// from that index on and focus moves behind the last written cell. Exact paste semantics
    /// (phase 2) may refine this path.
    /// </summary>
    /// <param name="index">Cell index the insert started at.</param>
    /// <param name="raw">Inserted text (already filtered per Kind by the module).</param>
    /// <returns>A task that completes when the model and the DOM are in sync.</returns>
    [JSInvokable]
    public async Task DistributeFromAsync(int index, string raw)
    {
        if (index < 0 || index >= Length)
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

            await SyncCellAsync(target);
        }

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
