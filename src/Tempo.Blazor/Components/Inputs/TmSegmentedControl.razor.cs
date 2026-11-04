using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// A small set of mutually exclusive options drawn as one segmented tray with a sliding highlight.
/// <para>
/// The control is the "value" sibling of <c>TmTabs</c>: use it when the segments pick one value out of a
/// few (period, view density, alignment) and a plain <c>TmSelect</c> would bury the choice behind a click.
/// It is NOT a replacement for <c>TmRadioGroup</c> — a radio group is the right control in a form where
/// every option is spelled out above the fold; the segmented tray is the compact, always-visible variant.
/// </para>
/// <para>
/// Two ARIA patterns are available through <see cref="Role"/>: a <c>radiogroup</c> of <c>radio</c>
/// options (default — the segments pick a value) and a <c>tablist</c> of <c>tab</c> options (the segments
/// switch content, and the host owns the panels). Both use a roving tabindex: exactly one segment is in
/// the tab order, the arrow keys move the selection and the focus together, Home and End jump to the first
/// and last ENABLED segment, and the native <c>&lt;button&gt;</c> elements own Enter and Space (so a key
/// press activates exactly once).
/// </para>
/// <para>
/// The sliding highlight is measured in the browser once per render (and on resize) and written as a
/// <c>transform</c> on a decorative strip; <c>prefers-reduced-motion</c> removes the travel. Without
/// JavaScript nothing is measured, the strip is never claimed and the selected segment paints its own
/// background instead, so the control looks intentional in a prerendered page too.
/// </para>
/// </summary>
/// <typeparam name="TValue">The type of the value the segments report.</typeparam>
public partial class TmSegmentedControl<TValue> : TmComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/Components/Inputs/TmSegmentedControl.razor.js";

    /// <summary>The stable DOM id prefix of the options, so every idref resolves inside this instance.</summary>
    private readonly string _instanceId = $"tm-segmented-{Guid.NewGuid():n}"[..20];

    private ElementReference _groupRef;
    private IJSObjectReference? _module;
    private bool _moduleUnavailable;
    private int _pendingFocusIndex = -1;

    // ── Data ─────────────────────────────────────────────────────

    /// <summary>
    /// The segments, in reading order. An empty list renders nothing at all — a tray with no options is
    /// not a control. Disabled segments still render and stay in place: removing one would renumber every
    /// neighbour and move the selection under the reader's finger.
    /// </summary>
    [Parameter] public IReadOnlyList<SegmentedOption<TValue>> Options { get; set; } = [];

    /// <summary>
    /// The selected value, two-way bound with <see cref="ValueChanged"/>. A value that is not among the
    /// segments renders no selection (the first enabled segment stays the tab stop) and a <c>null</c>
    /// value means "nothing selected" rather than "the segment whose value is null".
    /// </summary>
    [Parameter] public TValue? Value { get; set; }

    /// <summary>
    /// Fires when the user picks a different segment. Re-selecting the current value raises nothing, so a
    /// two-way binding cannot loop.
    /// </summary>
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }

    // ── Presentation ─────────────────────────────────────────────

    /// <summary>The tray height. Default <see cref="SegmentedControlSize.Md"/>.</summary>
    [Parameter] public SegmentedControlSize Size { get; set; } = SegmentedControlSize.Md;

    /// <summary>
    /// Stretches the tray to the full width of its container and shares that width equally between the
    /// segments. Default <c>false</c> — the tray hugs its content and scrolls when it runs out of room.
    /// </summary>
    [Parameter] public bool FullWidth { get; set; }

    /// <summary>
    /// Which ARIA pattern the segments expose. Default <see cref="SegmentedControlRole.RadioGroup"/>
    /// (the segments pick a value). <see cref="SegmentedControlRole.Tabs"/> turns them into a tab list —
    /// see <see cref="PanelId"/> for the panels it points at.
    /// </summary>
    [Parameter] public SegmentedControlRole Role { get; set; } = SegmentedControlRole.RadioGroup;

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Disables the whole control: every segment is disabled and nothing is raised.</summary>
    [Parameter] public bool Disabled { get; set; }

    // ── Accessible name ──────────────────────────────────────────

    /// <summary>
    /// An optional VISIBLE label above the tray. It is also the group's accessible name (through
    /// <c>aria-labelledby</c>), so the name a screen reader reads is the name a reader sees — do not add an
    /// <see cref="AriaLabel"/> on top of it. A whitespace-only label counts as no label.
    /// </summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>
    /// The accessible name used when no <see cref="Label"/> is rendered. Null means the localized default
    /// ("Options"). Ignored when <see cref="AriaLabelledBy"/> is set.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>
    /// Id of an element outside this control that names it (a page heading, a legend). Wins over
    /// <see cref="Label"/> and <see cref="AriaLabel"/>.
    /// </summary>
    [Parameter] public string? AriaLabelledBy { get; set; }

    /// <summary>
    /// Id of the panel the tabs control, applied to every tab that does not name its own
    /// <see cref="SegmentedOption.PanelId"/>. Only used with
    /// <see cref="SegmentedControlRole.Tabs"/>; when no id is known the tab carries no
    /// <c>aria-controls</c> at all, because an idref to a missing element is worse than none.
    /// </summary>
    [Parameter] public string? PanelId { get; set; }

    // ── Derived markup values ────────────────────────────────────

    private bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    private string LabelId => $"{_instanceId}-label";

    private string GroupId => $"{_instanceId}-group";

    private bool IsTabs => Role == SegmentedControlRole.Tabs;

    private string GroupRole => IsTabs ? "tablist" : "radiogroup";

    private string OptionRole => IsTabs ? "tab" : "radio";

    private string? GroupAriaLabel => GroupLabelledBy is null
        ? (string.IsNullOrWhiteSpace(AriaLabel) ? Loc["TmSegmentedControl_DefaultLabel"] : AriaLabel)
        : null;

    private string? GroupLabelledBy
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AriaLabelledBy)) return AriaLabelledBy;
            if (HasLabel) return LabelId;
            return null;
        }
    }

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-segmented", SizeClass };
            if (FullWidth) classes.Add("tm-segmented--full-width");
            if (Disabled) classes.Add("tm-segmented--disabled");
            if (IsTabs) classes.Add("tm-segmented--tabs");
            if (!string.IsNullOrEmpty(Class)) classes.Add(Class);
            return string.Join(' ', classes);
        }
    }

    private string GroupClass => "tm-segmented__track";

    private string SizeClass => Size switch
    {
        SegmentedControlSize.Sm => "tm-segmented--sm",
        SegmentedControlSize.Lg => "tm-segmented--lg",
        _ => "tm-segmented--md"
    };

    private string OptionId(int index) => $"{_instanceId}-opt-{index}";

    private bool IsSelected(SegmentedOption<TValue> option)
        => Value is not null && EqualityComparer<TValue>.Default.Equals(Value, option.Value);

    private bool IsOptionDisabled(SegmentedOption<TValue> option) => Disabled || option.IsDisabled;

    private string OptionClass(SegmentedOption<TValue> option)
    {
        var classes = new List<string> { "tm-segmented__option" };
        if (IsSelected(option)) classes.Add("tm-segmented__option--selected");
        if (IsOptionDisabled(option)) classes.Add("tm-segmented__option--disabled");
        return string.Join(' ', classes);
    }

    private string? OptionChecked(SegmentedOption<TValue> option)
        => IsTabs ? null : (IsSelected(option) ? "true" : "false");

    private string? OptionSelected(SegmentedOption<TValue> option)
        => IsTabs ? (IsSelected(option) ? "true" : "false") : null;

    private string? OptionControls(SegmentedOption<TValue> option)
    {
        if (!IsTabs) return null;
        var panelId = !string.IsNullOrWhiteSpace(option.PanelId) ? option.PanelId : PanelId;
        return string.IsNullOrWhiteSpace(panelId) ? null : panelId;
    }

    private string OptionValueAttribute(SegmentedOption<TValue> option)
        => Convert.ToString(option.Value, CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// The single tab stop of the roving tabindex: the selected segment when it exists and is enabled,
    /// otherwise the first enabled segment (a group that is entirely disabled has no tab stop).
    /// </summary>
    private bool IsTabStop(int index)
    {
        if (index < 0 || index >= Options.Count) return false;
        if (IsOptionDisabled(Options[index])) return false;

        var selectedIndex = SelectedIndex();
        if (selectedIndex >= 0) return selectedIndex == index;

        return FirstEnabledIndex() == index;
    }

    private int SelectedIndex()
    {
        for (var index = 0; index < Options.Count; index++)
        {
            if (IsSelected(Options[index]) && !IsOptionDisabled(Options[index])) return index;
        }

        return -1;
    }

    private int FirstEnabledIndex()
    {
        for (var index = 0; index < Options.Count; index++)
        {
            if (!IsOptionDisabled(Options[index])) return index;
        }

        return -1;
    }

    private int LastEnabledIndex()
    {
        for (var index = Options.Count - 1; index >= 0; index--)
        {
            if (!IsOptionDisabled(Options[index])) return index;
        }

        return -1;
    }

    /// <summary>
    /// The next enabled segment in <paramref name="step"/> direction, wrapping at both ends. Wrapping is
    /// the APG radio-group behaviour: a group is a loop, not a dead end.
    /// </summary>
    private int NextEnabledIndex(int from, int step)
    {
        if (Options.Count == 0) return -1;

        var index = from;
        for (var attempt = 0; attempt < Options.Count; attempt++)
        {
            index += step;
            if (index < 0) index = Options.Count - 1;
            if (index >= Options.Count) index = 0;
            if (!IsOptionDisabled(Options[index])) return index;
        }

        return -1;
    }

    // ── Interaction ──────────────────────────────────────────────

    private async Task SelectAsync(SegmentedOption<TValue> option)
    {
        if (IsOptionDisabled(option)) return;
        if (IsSelected(option)) return;

        Value = option.Value;
        await ValueChanged.InvokeAsync(option.Value);
    }

    /// <summary>
    /// The keyboard model of both patterns: arrows move the focus AND the selection, Home/End jump to the
    /// ends. Enter and Space are deliberately absent — the options are native buttons, so the browser owns
    /// them and the click path above runs exactly once per key press (see
    /// docs/keyboard-activation-convention.md).
    /// </summary>
    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (Disabled || Options.Count == 0) return;

        var current = SelectedIndex();
        if (current < 0) current = FirstEnabledIndex();
        if (current < 0) return;

        var target = e.Key switch
        {
            "ArrowRight" or "ArrowDown" => NextEnabledIndex(current, 1),
            "ArrowLeft" or "ArrowUp" => NextEnabledIndex(current, -1),
            "Home" => FirstEnabledIndex(),
            "End" => LastEnabledIndex(),
            _ => -2
        };

        if (target == -2 || target < 0 || target == current)
        {
            // A no-op key changes nothing on purpose: the roving tab stop is COMPUTED from the current
            // selection by IsTabStop on every render, never stored, so leaving the state alone here cannot
            // strand a stale tab stop. (Home on the first segment, or an arrow at a wrap boundary that
            // lands on the same disabled/selected segment, is not an error.)
            return;
        }

        _pendingFocusIndex = target;
        await SelectAsync(Options[target]);
    }

    // ── Browser measurement ──────────────────────────────────────

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await EnsureModuleAsync();

        if (_pendingFocusIndex >= 0)
        {
            var index = _pendingFocusIndex;
            _pendingFocusIndex = -1;
            await InvokeJsAsync(module => module.InvokeVoidAsync("focusOption", _groupRef, index));
        }

        await PlaceIndicatorAsync();
    }

    /// <summary>
    /// Places the sliding highlight on the selected segment. Called after every render (it is two property
    /// writes) and again by the module's own ResizeObserver, so a wrap, a font swap or a container resize
    /// never leaves the strip on the wrong segment.
    /// </summary>
    private async Task PlaceIndicatorAsync()
    {
        if (_moduleUnavailable || Options.Count == 0) return;
        await InvokeJsAsync(module => module.InvokeVoidAsync("place", _groupRef));
    }

    private async Task EnsureModuleAsync()
    {
        if (_module is not null || _moduleUnavailable) return;

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_module is null)
            {
                // A runtime that answers `import` with null (a test double, a stripped host) cannot
                // measure anything — stop asking on every render and keep the CSS fallback.
                _moduleUnavailable = true;
            }
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or NotSupportedException)
        {
            // Prerendering, a host without the static asset, or a browser with JS off: the control falls
            // back to the attribute-free presentation, which is a complete design and not a degraded one.
            _moduleUnavailable = true;
        }
    }

    private async Task InvokeJsAsync(Func<IJSObjectReference, ValueTask> action)
    {
        if (_module is null || _moduleUnavailable) return;

        try
        {
            await action(_module);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or ObjectDisposedException or InvalidOperationException)
        {
            // A disconnected circuit or a released element is not an error a reader can act on.
            _moduleUnavailable = true;
        }
    }

    /// <summary>Releases the JS module (its ResizeObserver disconnects with it).</summary>
    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;

        try
        {
            await _module.InvokeVoidAsync("release", _groupRef);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or ObjectDisposedException or InvalidOperationException)
        {
            // Best effort: the circuit may already be gone.
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or ObjectDisposedException)
        {
            // Same as above.
        }
    }
}
