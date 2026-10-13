using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// A color gradient picker that allows users to select a color by adjusting
/// saturation/value on a 2D gradient, hue on a slider, and optionally alpha.
/// </summary>
/// <remarks>
/// The pointer gesture lives in <c>tm-color-gradient.js</c> over the shared <c>pointer-drag.js</c>
/// helper: the module measures the real element size (never the event offset, which jumps on the
/// thumb child, and never a fixed pixel size), streams fractions through rAF-throttled
/// <see cref="OnGradientDrag"/> calls, commits exactly once through <see cref="OnGradientCommit"/>,
/// and reports Escape / pointercancel / detach through <see cref="OnGradientCancel"/> — a cancelled
/// drag restores the state the press started from and changes nothing. The three sliders are
/// keyboard-operable (WCAG 2.1.1/2.5.7) and every key commits through <c>NotifyChangedAsync</c>,
/// the single commit path shared with the RGBA number inputs.
/// </remarks>
public partial class TmColorGradient : IAsyncDisposable
{
    private double _hue;        // 0-360
    private double _saturation; // 0-1
    private double _value;      // 0-1
    private byte _r, _g, _b;
    private double _alpha = 1.0;

    // ── Parameters ───────────────────────────────────────────────

    /// <summary>The current color value (hex, rgb, or rgba).</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Event fired when the color value changes.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Output format. Default is <see cref="ColorFormat.Hex"/>.</summary>
    [Parameter] public ColorFormat Format { get; set; } = ColorFormat.Hex;

    /// <summary>Whether to show the alpha slider and input. Default true.</summary>
    [Parameter] public bool ShowAlpha { get; set; } = true;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional attributes spread onto the wrapper.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Lifecycle ────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ColorGradientInterop? _interop;
    private DotNetObjectReference<TmColorGradient>? _dotnetRef;
    private ElementReference _rootRef;
    private readonly string _gradientId = Guid.NewGuid().ToString("N");
    private bool? _attachedShowAlpha;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The value this component itself committed echoes back through the @bind-Value round
        // trip; re-parsing it would rebuild HSV→RGB through the lossy conversion and let the
        // colour drift a unit per render. Only a genuinely external value is parsed again.
        if (!string.Equals(Value, _lastEmitted, StringComparison.OrdinalIgnoreCase))
        {
            ParseValue(Value);
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Attached once; re-attached when ShowAlpha toggles, because the alpha track (part of the
        // attachment) appears or disappears with it.
        if (firstRender || _attachedShowAlpha != ShowAlpha)
        {
            _attachedShowAlpha = ShowAlpha;
            _interop ??= new ColorGradientInterop(JS);
            _dotnetRef ??= DotNetObjectReference.Create(this);
            await _interop.AttachAsync(_rootRef, _dotnetRef, _gradientId);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_interop is not null)
        {
            await _interop.DetachAsync(_gradientId);
            await _interop.DisposeAsync();
            _interop = null;
        }
        _dotnetRef?.Dispose();
        _dotnetRef = null;
    }

    // ── Value parsing / formatting ───────────────────────────────

    private void ParseValue(string? value)
    {
        var (r, g, b, a) = ColorHelper.Parse(value);
        _r = r; _g = g; _b = b; _alpha = a;
        (_hue, _saturation, _value) = ColorHelper.RgbToHsv(_r, _g, _b);
        UpdateHueColor();
    }

    private string _hueColor = "#ff0000";

    private void UpdateHueColor()
    {
        var (hr, hg, hb) = ColorHelper.HsvToRgb(_hue, 1, 1);
        _hueColor = ColorHelper.ToHex(hr, hg, hb);
    }

    private string FormatValue()
    {
        return Format switch
        {
            ColorFormat.Rgb => ColorHelper.ToRgb(_r, _g, _b),
            ColorFormat.Rgba => ColorHelper.ToRgba(_r, _g, _b, _alpha),
            _ => ColorHelper.ToHex(_r, _g, _b, ShowAlpha ? _alpha : 1.0)
        };
    }

    private async Task NotifyChangedAsync()
    {
        var formatted = FormatValue();
        _lastEmitted = formatted;
        await ValueChanged.InvokeAsync(formatted);
    }

    // The value this component last pushed through ValueChanged; OnParametersSet skips the
    // re-parse when the incoming Value is just that echo (OrdinalIgnoreCase — hex case is noise).
    private string? _lastEmitted;

    // ── RGB input handlers ───────────────────────────────────────

    private async Task OnRedChanged(ChangeEventArgs e)
    {
        if (byte.TryParse(e.Value?.ToString(), out var v)) { _r = v; }
        UpdateHsvFromRgb();
        await NotifyChangedAsync();
    }

    private async Task OnGreenChanged(ChangeEventArgs e)
    {
        if (byte.TryParse(e.Value?.ToString(), out var v)) { _g = v; }
        UpdateHsvFromRgb();
        await NotifyChangedAsync();
    }

    private async Task OnBlueChanged(ChangeEventArgs e)
    {
        if (byte.TryParse(e.Value?.ToString(), out var v)) { _b = v; }
        UpdateHsvFromRgb();
        await NotifyChangedAsync();
    }

    private async Task OnAlphaInputChanged(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            _alpha = Math.Clamp(v, 0.0, 1.0);
        }
        await NotifyChangedAsync();
    }

    private void UpdateHsvFromRgb()
    {
        (_hue, _saturation, _value) = ColorHelper.RgbToHsv(_r, _g, _b);
        UpdateHueColor();
    }

    // ── Gradient drag (tm-color-gradient.js) ─────────────────────

    // The state the active gesture started from, remembered on its first drag call so a cancel
    // (Escape, pointercancel, detach) can restore it verbatim. The R/G/B are captured too and
    // restored EXACTLY: re-deriving them from the restored HSV would run the lossy HSV→RGB
    // conversion and hand back a colour a unit away from the one the press started from
    // (#3B82F6 came back as #3A82F6). Null while no gesture is running.
    private (double Hue, double Saturation, double Value, double Alpha, byte R, byte G, byte B)? _gestureStart;

    /// <summary>
    /// Live preview from the JavaScript drag: updates HSV/alpha from fractions of the element's
    /// real size and re-renders, WITHOUT firing <see cref="ValueChanged"/> — a cancelled gesture
    /// must leave the bound value untouched.
    /// </summary>
    [JSInvokable]
    public void OnGradientDrag(string kind, double fx, double fy)
    {
        _gestureStart ??= (_hue, _saturation, _value, _alpha, _r, _g, _b);
        Apply(kind, fx, fy);
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The end of a drag (or a tap): applies the final position and commits ONCE through
    /// <see cref="NotifyChangedAsync"/>, the same path the RGBA inputs use.
    /// </summary>
    [JSInvokable]
    public async Task OnGradientCommit(string kind, double fx, double fy)
    {
        _gestureStart = null;
        Apply(kind, fx, fy);
        await NotifyChangedAsync();
    }

    /// <summary>
    /// A cancelled gesture (Escape, pointercancel, lost capture, detach): the state the press
    /// started from is restored — R/G/B verbatim, hue colour re-derived for the gradient only —
    /// and NOTHING is committed. The old pointer handlers committed on every release path, so an
    /// interrupted drag still changed the colour.
    /// </summary>
    [JSInvokable]
    public void OnGradientCancel()
    {
        if (_gestureStart is { } start)
        {
            _hue = start.Hue;
            _saturation = start.Saturation;
            _value = start.Value;
            _alpha = start.Alpha;
            _r = start.R;
            _g = start.G;
            _b = start.B;
            UpdateHueColor();
        }
        _gestureStart = null;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Applies one slider kind's fractions of the element's real size to the HSV state.</summary>
    private void Apply(string kind, double fx, double fy)
    {
        switch (kind)
        {
            case "area":
                // fy is the raw top-based fraction; brightness is 1 at the top.
                _saturation = Math.Clamp(fx, 0, 1);
                _value = Math.Clamp(1 - fy, 0, 1);
                UpdateRgbFromHsv();
                break;
            case "hue":
                _hue = Math.Clamp(fx, 0, 1) * 360;
                UpdateHueColor();
                UpdateRgbFromHsv();
                break;
            case "alpha":
                _alpha = Math.Clamp(fx, 0, 1);
                break;
        }
    }

    // ── Keyboard sliders (the non-drag alternative, WCAG 2.5.7) ──

    private static double KeyStep(bool shift) => shift ? 0.10 : 0.01;

    private async Task OnAreaKeyDownAsync(KeyboardEventArgs e)
    {
        var step = KeyStep(e.ShiftKey);
        switch (e.Key)
        {
            case "ArrowLeft": _saturation = Math.Clamp(_saturation - step, 0, 1); break;
            case "ArrowRight": _saturation = Math.Clamp(_saturation + step, 0, 1); break;
            case "ArrowUp": _value = Math.Clamp(_value + step, 0, 1); break;
            case "ArrowDown": _value = Math.Clamp(_value - step, 0, 1); break;
            case "Home": _saturation = 0; break;
            case "End": _saturation = 1; break;
            default: return;
        }
        UpdateRgbFromHsv();
        await NotifyChangedAsync();
    }

    private async Task OnHueKeyDownAsync(KeyboardEventArgs e)
    {
        var step = e.ShiftKey ? 10 : 1;
        switch (e.Key)
        {
            case "ArrowLeft": _hue = Math.Clamp(_hue - step, 0, 360); break;
            case "ArrowRight": _hue = Math.Clamp(_hue + step, 0, 360); break;
            case "Home": _hue = 0; break;
            case "End": _hue = 360; break;
            default: return;
        }
        UpdateHueColor();
        UpdateRgbFromHsv();
        await NotifyChangedAsync();
    }

    private async Task OnAlphaKeyDownAsync(KeyboardEventArgs e)
    {
        var step = KeyStep(e.ShiftKey);
        switch (e.Key)
        {
            case "ArrowLeft": _alpha = Math.Clamp(_alpha - step, 0, 1); break;
            case "ArrowRight": _alpha = Math.Clamp(_alpha + step, 0, 1); break;
            case "Home": _alpha = 0; break;
            case "End": _alpha = 1; break;
            default: return;
        }
        await NotifyChangedAsync();
    }

    // ── RGB update from HSV ──────────────────────────────────────

    private void UpdateRgbFromHsv()
    {
        (_r, _g, _b) = ColorHelper.HsvToRgb(_hue, _saturation, _value);
    }
}
