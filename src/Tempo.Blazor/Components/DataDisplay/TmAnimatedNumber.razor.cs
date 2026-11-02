using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// An odometer-style number: a metric that counts up to its target value. The component always
/// renders the FINAL formatted value — for assistive technology, for a host that never loads the
/// script, and for the last frame of the animation — and delegates the visual ticker to the
/// <c>tm-animated-number.js</c> module (<c>requestAnimationFrame</c>, one element at a time).
/// </summary>
/// <remarks>
/// <para>
/// Accessibility contract: ONE node carries the value. The visible span always holds the FINAL formatted
/// text that Blazor rendered, so a reader and a copy/paste meet the number exactly once, and the module
/// only repaints that same text between frames — a running ticker is therefore never announced.
/// <see cref="Announce"/> is the only opt-in: when it is on, the component adds exactly one polite,
/// atomic live region as a visually hidden sibling and marks the visual span <c>aria-hidden</c>, so the
/// region is the only text a reader meets. There is never a second announcement channel. A reader who
/// asked for reduced motion gets no animation at all: the machine's preference is probed before the
/// first animation and the component then simply renders the target.
/// </para>
/// <para>
/// The animation is decoration, not data: <see cref="Duration"/> 0, <see cref="Animate"/>
/// <see langword="false"/>, a non-finite value and an unavailable JavaScript runtime all end in the
/// same place — the final value, rendered by Blazor. Nothing is ever read back from JavaScript, so
/// there is no state to reconcile and no failure mode that changes a number.
/// </para>
/// <para>
/// Two-way binding is deliberately absent: a ticker displays a host value, it never produces one.
/// </para>
/// </remarks>
public partial class TmAnimatedNumber : TmComponentBase, IAsyncDisposable
{
    /// <summary>Upper bound of <see cref="Duration"/>, so a bad value cannot freeze a frame loop for minutes.</summary>
    private const int MaxDurationMilliseconds = 10_000;

    /// <summary>The default format: whole, group-separated numbers ("1,234").</summary>
    private const string DefaultFormat = "N0";

    /// <summary>The JavaScript runtime the ticker module is imported from (never used before the first animation).</summary>
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private AnimatedNumberInterop? _interop;
    private ElementReference _valueElement;
    private bool _disposed;
    private bool _started;
    private bool _reducedMotion;
    private double? _animatedTarget;
    private int _animatedDuration = -1;

    /// <summary>
    /// The numeric value to display. The rendered (and therefore accessible) text is
    /// <c>Value.ToString(Format, Culture)</c>, wrapped in <see cref="Prefix"/>/<see cref="Suffix"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public double Value { get; set; }

    /// <summary>
    /// The .NET numeric format string of the value (default <c>"N0"</c> — whole, group-separated
    /// numbers). It decides both the final text and the shape of the intermediate frames; see
    /// <see cref="AnimatedNumberFormat"/> for the formats the ticker can mirror. An unsupported
    /// format still animates — only its in-flight frames fall back to whole numbers.
    /// </summary>
    [Parameter]
    public string Format { get; set; } = DefaultFormat;

    /// <summary>
    /// The culture that formats the number. Default <see langword="null"/> = the current culture at
    /// render time, so a ticker follows the request culture without being told.
    /// </summary>
    [Parameter]
    public CultureInfo? Culture { get; set; }

    /// <summary>Optional text placed before the number (a currency symbol, a sign). Not localized — it is host data.</summary>
    [Parameter]
    public string? Prefix { get; set; }

    /// <summary>Optional text placed after the number (e.g. <c>" %"</c>). Not localized — it is host data.</summary>
    [Parameter]
    public string? Suffix { get; set; }

    /// <summary>
    /// Length of the ticker in milliseconds (default 600). Clamped to 0–10000; 0 means instant, so
    /// no JavaScript is imported at all.
    /// </summary>
    [Parameter]
    public int Duration { get; set; } = 600;

    /// <summary>
    /// The value the very first frame starts from (default <see langword="null"/> = 0). Later value
    /// changes never return here: a mid-animation change retargets from the number on screen.
    /// </summary>
    [Parameter]
    public double? StartValue { get; set; }

    /// <summary>
    /// Whether the ticker runs at all. Default <see langword="true"/>; <see langword="false"/> is a
    /// pure-Blazor path with no import and no preference probe, for tests, static exports and hosts
    /// that want a still number. Reduced motion is honoured independently of this switch.
    /// </summary>
    [Parameter]
    public bool Animate { get; set; } = true;

    /// <summary>
    /// Turns the accessible value into a polite live region, so a changed number is announced once
    /// when it lands. Default <see langword="false"/>: a page with many tickers must not become a
    /// chorus, and the value is still available to a reader without it.
    /// </summary>
    [Parameter]
    public bool Announce { get; set; }

    /// <summary>Additional CSS class for the root element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>The culture the value is formatted with — the parameter when set, the current culture otherwise.</summary>
    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>The animation length clamped into the documented range.</summary>
    private int EffectiveDuration => Math.Clamp(Duration, 0, MaxDurationMilliseconds);

    private string RootClass => string.IsNullOrEmpty(Class) ? "tm-animated-number" : $"tm-animated-number {Class}";

    /// <summary>
    /// The final text — the value formatted for the culture and wrapped in the prefix/suffix. An
    /// invalid format string falls back to the culture's default shape rather than throwing out of a
    /// render.
    /// </summary>
    private string ReaderText
    {
        get
        {
            string number;
            try
            {
                number = Value.ToString(Format, EffectiveCulture);
            }
            catch (FormatException)
            {
                number = Value.ToString(EffectiveCulture);
            }

            return string.Concat(Prefix, number, Suffix);
        }
    }

    /// <summary>
    /// Starts or retargets the ticker when the rendered state calls for it: only when
    /// <see cref="Animate"/> is on, the duration is positive, the value is finite and an element
    /// exists. The reduced-motion preference is probed once, before the first animation.
    /// </summary>
    /// <param name="firstRender">Whether this is the component's first render.</param>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || !Animate)
        {
            return;
        }

        var duration = EffectiveDuration;
        if (duration <= 0 || !double.IsFinite(Value) || _valueElement.Id is null)
        {
            return;
        }

        if (_animatedTarget == Value && _animatedDuration == duration)
        {
            return;
        }

        _interop ??= new AnimatedNumberInterop(JS);

        if (!_started)
        {
            _started = true;
            _reducedMotion = await _interop.PrefersReducedMotionAsync();
        }

        if (_reducedMotion)
        {
            // Remember the settled target so an unchanged value does not re-probe on every render.
            _animatedTarget = Value;
            _animatedDuration = duration;
            return;
        }

        // The first ticker runs from StartValue (or 0); every later change passes null so the module
        // continues from the number it is currently painting and never jumps back to the start.
        var from = _animatedTarget is null ? StartValue ?? 0d : (double?)null;

        _animatedTarget = Value;
        _animatedDuration = duration;

        await _interop.AnimateAsync(
            _valueElement,
            from,
            Value,
            duration,
            AnimatedNumberFormat.Describe(Format, EffectiveCulture),
            Prefix,
            Suffix);
    }

    /// <summary>Stops the running frame loop and releases the module, so no ticker outlives its component.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_interop is null)
        {
            return;
        }

        await _interop.CancelAsync(_valueElement);
        await _interop.DisposeAsync();
    }
}
