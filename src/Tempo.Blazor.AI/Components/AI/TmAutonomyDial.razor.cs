using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How much an agent may do on its own: the stops the host declares, the grade the reader picked, the ceiling a plan
/// imposes and the limits somebody else enforces.
/// <para>
/// The surface is a CONTROL, not a read-out: a real <c>role="slider"</c> with one tab stop (arrow keys step one grade,
/// Home and End reach the ends of what is ALLOWED) or a segmented radio group in which every grade is its own target,
/// plus a pair of native step buttons as the pointer alternative (WCAG 2.5.7). The value is exposed as an invariant number
/// AND as a localized value text, so a screen reader hears "Ask when risky" instead of "1"; a change at or above
/// <see cref="ConfirmAbove"/> asks first, and the confirmation keeps the focus (Escape cancels, the focus returns to the
/// slider).
/// </para>
/// <para>
/// The component reports every change through <see cref="ValueChanged"/> and never writes to the host's state; the grade it
/// shows is its own copy, so the reader sees the change before the round-trip, and a host that re-renders the SAME value
/// cannot undo the reader's pick. ONE polite live region announces a committed change — the confirmation does not get a
/// second region, because its message is already in front of the reader.
/// </para>
/// </summary>
public partial class TmAutonomyDial : TmComponentBase
{
    private readonly string _id = $"tm-ai-autonomy-{Guid.NewGuid():n}"[..24];

    private List<AutonomyLevel> _levels = [];
    private int _value;
    private bool _valueSeeded;
    private int _lastValueParameter;

    private AutonomyDialPresentation _presentation;

    private int? _pendingConfirmation;

    private string _announcement = string.Empty;
    private bool _announcementMarker;

    private ElementReference _slider;
    private ElementReference _confirmAccept;
    private bool _focusSliderAfterRender;
    private bool _focusConfirmAfterRender;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The grades of autonomy, in any order: they are rendered in ascending <see cref="AutonomyLevel.Value"/> order and a
    /// duplicate value is kept once. Null or empty renders the three localized defaults (ask first / ask when risky / act
    /// freely).
    /// </summary>
    [Parameter] public IReadOnlyList<AutonomyLevel>? Levels { get; set; }

    /// <summary>
    /// The grade the reader picked, two-way bound with <see cref="ValueChanged"/> (<c>@bind-Value</c>). A value that is
    /// not one of the stops, or one above <see cref="MaxAllowed"/>, renders the nearest allowed stop; the surface never
    /// changes the host's value behind its back, so the next change reports an allowed grade.
    /// </summary>
    [Parameter] public int Value { get; set; }

    /// <summary>Fires with the newly committed grade.</summary>
    [Parameter] public EventCallback<int> ValueChanged { get; set; }

    /// <summary>
    /// How the stops are presented. Default <see cref="AutonomyDialPresentation.Dial"/>; applies to this render only and is
    /// owned by the host. An unknown value falls back to the dial.
    /// </summary>
    [Parameter] public AutonomyDialPresentation Presentation { get; set; }

    /// <summary>
    /// The limits the reader cannot lift, each naming its action and (when known) who enforces it and why. Null or empty
    /// renders no limits section. Locks are FACTS about the surface, never controls.
    /// </summary>
    [Parameter] public IReadOnlyList<AutonomyLock>? Locks { get; set; }

    /// <summary>
    /// The highest grade a plan allows, as a <see cref="AutonomyLevel.Value"/>. Null (the default) allows every declared
    /// stop. The stops above it still render — marked unavailable — and the surface prints an explanation instead of
    /// silently refusing the key.
    /// </summary>
    [Parameter] public int? MaxAllowed { get; set; }

    /// <summary>
    /// The host's own explanation of <see cref="MaxAllowed"/>. Null renders the localized default ("Your plan does not
    /// allow a higher level.").
    /// </summary>
    [Parameter] public string? MaxAllowedReason { get; set; }

    /// <summary>
    /// The grade at or above which a change asks for confirmation. Null (the default) commits every change at once; a
    /// change is held until the reader confirms it and Escape cancels without reporting anything.
    /// </summary>
    [Parameter] public int? ConfirmAbove { get; set; }

    /// <summary>
    /// The heading above the surface. Null renders the localized default ("Autonomy"); the heading also names the slider,
    /// so the name a reader hears is the one they see.
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Whether the heading is rendered. Default <c>true</c>.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>The heading level of the rendered title, clamped to 2–6. Default 3.</summary>
    [Parameter] public int HeadingLevel { get; set; } = 3;

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The grade the surface currently shows, clamped into the stops it may offer.</summary>
    public int EffectiveValue => _value;

    /// <summary>The stop the surface currently shows.</summary>
    public AutonomyLevel? CurrentLevel
        => _levels.FirstOrDefault(level => level.Value == _value);

    /// <summary>The stops being offered, in ascending grade order (the host's, or the localized defaults).</summary>
    public IReadOnlyList<AutonomyLevel> Stops => _levels;

    /// <summary>The effective heading level, clamped into the range a sectioning element allows.</summary>
    public int EffectiveHeadingLevel => Math.Clamp(HeadingLevel, 2, 6);

    /// <summary>The heading text: the host's <see cref="Title"/> or the localized default.</summary>
    public string HeadingText => string.IsNullOrWhiteSpace(Title) ? Loc["TmAutonomyDial_Title"] : Title!;

    private bool IsSegmented
        => AutonomyPresentation.IsKnownPresentation(_presentation)
           && _presentation == AutonomyDialPresentation.Segmented;

    private string PresentationSlug => IsSegmented ? "segmented" : "dial";

    private string TitleId => _id + "-title";

    private string DescriptionId => _id + "-description";

    private string ConfirmMessageId => _id + "-confirm";

    private string SliderLabel => Loc["TmAutonomyDial_SliderLabel"];

    private int LowestValue => _levels.Count > 0 ? _levels[0].Value : 0;

    private int HighestValue => _levels.Count > 0 ? _levels[^1].Value : 0;

    /// <summary>The highest grade the ceiling still allows (the whole range when a ceiling would allow nothing).</summary>
    private int HighestAvailableValue => AvailableLevels.Count > 0 ? AvailableLevels[^1].Value : HighestValue;

    /// <summary>
    /// The stops the reader may reach. A ceiling that would allow NONE of them falls back to the whole range, because a
    /// ceiling no stop can satisfy is a host mistake — a dial nobody can move is not a control.
    /// </summary>
    private IReadOnlyList<AutonomyLevel> AvailableLevels
    {
        get
        {
            var available = new List<AutonomyLevel>(_levels.Count);
            foreach (var level in _levels)
            {
                if (IsAvailable(level))
                {
                    available.Add(level);
                }
            }

            return available.Count > 0 ? available : _levels;
        }
    }

    private string CurrentLabel => CurrentLevel is { } stop
        ? AutonomyPresentation.LevelLabel(Loc, stop, PositionOf(stop))
        : string.Empty;

    private string CurrentDescription => CurrentLevel is { } stop
        ? AutonomyPresentation.LevelDescription(stop)
        : string.Empty;

    private bool HasDescription => !string.IsNullOrWhiteSpace(CurrentDescription);

    private string PositionText => Loc["TmAutonomyDial_Position",
        PositionOf(CurrentLevel).ToString(CultureInfo.CurrentCulture),
        _levels.Count.ToString(CultureInfo.CurrentCulture)];

    private bool HasCeiling => MaxAllowed is int ceiling && ceiling < HighestValue;

    private string CeilingLabel
    {
        get
        {
            var ceilingStop = _levels.LastOrDefault(IsAvailable) ?? CurrentLevel;
            return ceilingStop is null ? string.Empty : AutonomyPresentation.LevelLabel(Loc, ceilingStop, PositionOf(ceilingStop));
        }
    }

    private string CeilingReason => string.IsNullOrWhiteSpace(MaxAllowedReason)
        ? Loc["TmAutonomyDial_LimitDefaultReason"]
        : MaxAllowedReason!;

    private bool CanStepDown => IndexOfAvailable(_value) > 0;

    private bool CanStepUp
    {
        get
        {
            var index = IndexOfAvailable(_value);
            return index >= 0 && index < AvailableLevels.Count - 1;
        }
    }

    private IReadOnlyList<AutonomyLock> VisibleLocks
        => Locks is null ? [] : [.. Locks.Where(lockItem => lockItem is not null && !string.IsNullOrWhiteSpace(lockItem.ActionLabel))];

    private IReadOnlyList<SegmentedOption<int>> SegmentOptions
        => [.. _levels.Select(level => new SegmentedOption<int>
        {
            Label = AutonomyPresentation.LevelLabel(Loc, level, PositionOf(level)),
            Value = level.Value,
            IsDisabled = !IsAvailable(level),
        })];

    private string FillStyle
    {
        get
        {
            var span = HighestValue - LowestValue;
            var progress = span <= 0 ? 100d : (_value - LowestValue) * 100d / span;
            return string.Create(CultureInfo.InvariantCulture,
                $"--tm-autonomy-progress: {Math.Clamp(progress, 0, 100):0.##}%");
        }
    }

    private string RootClass
    {
        get
        {
            var root = "tm-ai-autonomy" + (IsSegmented ? " tm-ai-autonomy--segmented" : string.Empty);
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _levels = Normalize(Levels);
        if (_levels.Count == 0)
        {
            _levels = [.. AutonomyPresentation.DefaultLevels(Loc)];
        }

        if (!_valueSeeded || Value != _lastValueParameter)
        {
            _valueSeeded = true;
            _lastValueParameter = Value;
            _value = Resolve(Value);
        }
        else
        {
            // The host re-rendered the same value: the reader's pick wins, but a ceiling that moved still clamps it.
            _value = Resolve(_value);
        }

        // The presentation is host-owned: there is no reader state to preserve, so the parameter always wins.
        _presentation = AutonomyPresentation.IsKnownPresentation(Presentation)
            ? Presentation
            : AutonomyDialPresentation.Dial;

        if (_pendingConfirmation is int proposed && !IsAvailableValue(proposed))
        {
            _pendingConfirmation = null;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The focus move happens AFTER the paint: the control that has to receive it is created (or enabled) by this very
        // render, and a browser only focuses an element once it is patched.
        if (_focusConfirmAfterRender)
        {
            _focusConfirmAfterRender = false;

            if (await TryFocusAsync(_confirmAccept))
            {
                return;
            }
        }

        if (_focusSliderAfterRender)
        {
            _focusSliderAfterRender = false;
            await TryFocusAsync(_slider);
        }
    }

    /// <summary>
    /// Copies the host's stops, drops nulls, keeps one stop per grade and orders them ascending: the surface's reading
    /// order is the grade's order, so ArrowRight always means "more autonomy".
    /// </summary>
    private static List<AutonomyLevel> Normalize(IReadOnlyList<AutonomyLevel>? levels)
    {
        if (levels is null || levels.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<int>();
        var copy = new List<AutonomyLevel>();

        foreach (var level in levels)
        {
            if (level is not null && seen.Add(level.Value))
            {
                copy.Add(level);
            }
        }

        copy.Sort((left, right) => left.Value.CompareTo(right.Value));
        return copy;
    }

    /// <summary>Whether a stop is under the ceiling (or there is no ceiling).</summary>
    private bool IsAvailable(AutonomyLevel level)
        => MaxAllowed is not int ceiling || level.Value <= ceiling;

    private bool IsAvailableValue(int value)
        => _levels.Any(level => level.Value == value && IsAvailable(level));

    /// <summary>
    /// Turns any host value into a grade the surface can show: the exact stop when there is one, otherwise the nearest
    /// allowed stop, clamped into the allowed range.
    /// </summary>
    private int Resolve(int value)
    {
        if (_levels.Count == 0)
        {
            return value;
        }

        var available = AvailableLevels;

        if (available.Any(level => level.Value == value))
        {
            return value;
        }

        var nearest = available[0];
        foreach (var level in available)
        {
            if (Math.Abs(level.Value - value) < Math.Abs(nearest.Value - value))
            {
                nearest = level;
            }
        }

        return nearest.Value;
    }

    private int PositionOf(AutonomyLevel? level)
        => level is null ? 0 : _levels.IndexOf(level) + 1;

    private int IndexOfAvailable(int value)
    {
        var available = AvailableLevels;
        for (var index = 0; index < available.Count; index++)
        {
            if (available[index].Value == value)
            {
                return index;
            }
        }

        return -1;
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    /// <summary>The keyboard model of the slider: one grade per arrow press, Home and End to the ends of what is allowed.</summary>
    private async Task OnSliderKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowRight" or "ArrowUp":
                await StepAsync(1);
                break;
            case "ArrowLeft" or "ArrowDown":
                await StepAsync(-1);
                break;
            case "Home":
                await ProposeAsync(AvailableLevels[0].Value);
                break;
            case "End":
                await ProposeAsync(AvailableLevels[^1].Value);
                break;
            case "Escape" when _pendingConfirmation is not null:
                await CancelConfirmationAsync();
                break;
        }
    }

    /// <summary>Moves one grade up or down, or stays put at an end (never wrapping).</summary>
    /// <param name="direction">+1 for more autonomy, −1 for less.</param>
    /// <returns>A task that completes when the change has been committed or offered for confirmation.</returns>
    public async Task StepAsync(int direction)
    {
        var index = IndexOfAvailable(_value);
        if (index < 0)
        {
            return;
        }

        var target = index + Math.Sign(direction);
        if (target < 0 || target >= AvailableLevels.Count || direction == 0)
        {
            return;
        }

        await ProposeAsync(AvailableLevels[target].Value);
    }

    /// <summary>
    /// Offers a grade: a change below <see cref="ConfirmAbove"/> (or with no threshold) is committed at once, a change at
    /// or above it waits in the confirmation.
    /// </summary>
    /// <param name="value">The grade the reader asked for.</param>
    /// <returns>A task that completes when the request is either committed or pending confirmation.</returns>
    public async Task ProposeAsync(int value)
    {
        // The CALLER decides what it offers: the step, End and the radio group only propose stops the dial may reach, so
        // a value that exists is a value worth committing even when a degenerate ceiling allows none.
        if (value == _value || _pendingConfirmation is not null || !_levels.Any(level => level.Value == value))
        {
            return;
        }

        if (ConfirmAbove is int threshold && value >= threshold)
        {
            _pendingConfirmation = value;
            _focusConfirmAfterRender = true;
            StateHasChanged();
            return;
        }

        await CommitAsync(value);
    }

    /// <summary>Commits the grade the confirmation offers and hands the focus back to the slider.</summary>
    /// <returns>A task that completes when the change has been reported to the host.</returns>
    public async Task ConfirmAsync()
    {
        if (_pendingConfirmation is not int proposed)
        {
            return;
        }

        _pendingConfirmation = null;
        _focusSliderAfterRender = true;
        await CommitAsync(proposed);
    }

    /// <summary>Drops the pending change without reporting anything, and hands the focus back to the slider.</summary>
    /// <returns>A task that completes when the confirmation is closed.</returns>
    public Task CancelConfirmationAsync()
    {
        if (_pendingConfirmation is null)
        {
            return Task.CompletedTask;
        }

        _pendingConfirmation = null;
        _focusSliderAfterRender = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task CommitAsync(int value)
    {
        _value = value;
        Announce(Loc["TmAutonomyDial_Announce", CurrentLabel]);
        StateHasChanged();

        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(value);
        }
    }

    private async Task OnSegmentChangedAsync(int value) => await ProposeAsync(value);

    private async Task OnConfirmKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            await CancelConfirmationAsync();
        }
    }

    private Task OnConfirmAcceptAsync() => ConfirmAsync();

    private Task OnConfirmCancelAsync() => CancelConfirmationAsync();

    /// <summary>The label and the description the pending confirmation names, so the reader knows what they accept.</summary>
    private string ConfirmationMessage(int proposed)
    {
        var stop = _levels.FirstOrDefault(level => level.Value == proposed);
        var label = stop is null ? string.Empty : AutonomyPresentation.LevelLabel(Loc, stop, PositionOf(stop));
        var description = stop is null ? string.Empty : AutonomyPresentation.LevelDescription(stop);
        return Loc["TmAutonomyDial_ConfirmMessage", label, description];
    }

    private string StopTone(AutonomyLevel level)
        => AutonomyPresentation.LevelTone(level.Value, HighestAvailableValue);

    private string StopPositionStyle(AutonomyLevel level)
    {
        var span = HighestValue - LowestValue;
        var offset = span <= 0 ? 0d : (level.Value - LowestValue) * 100d / span;
        return string.Create(CultureInfo.InvariantCulture,
            $"inset-inline-start: {Math.Clamp(offset, 0, 100):0.##}%");
    }

    /// <summary>
    /// Writes the announcement into the ONE polite region. An identical sentence is re-announced by toggling one invisible
    /// marker, because a live region announces a MUTATION and not a repeat of the same text.
    /// </summary>
    private void Announce(string message)
    {
        if (string.Equals(message, _announcement, StringComparison.Ordinal))
        {
            _announcementMarker = !_announcementMarker;
            _announcement = _announcementMarker ? message + "\u200B" : message;
            return;
        }

        _announcementMarker = false;
        _announcement = message;
    }

    private static async Task<bool> TryFocusAsync(ElementReference element)
    {
        if (element.Id is null)
        {
            return false;
        }

        try
        {
            await element.FocusAsync();
            return true;
        }
        catch (Exception exception) when (exception is Microsoft.JSInterop.JSException
            or Microsoft.JSInterop.JSDisconnectedException
            or InvalidOperationException or NotSupportedException or ObjectDisposedException or ArgumentException)
        {
            // No JavaScript, or the element is already gone: every control is still reachable with Tab, so a failed
            // hand-back must never surface as an unhandled exception.
            return false;
        }
    }
}
