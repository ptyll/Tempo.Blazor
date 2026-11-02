using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The setup guide / progress tracker of an application: a checklist of onboarding steps the user
/// works through, with a counted progress line, collapsible groups, an optional call-to-action per
/// step and a celebration block once every required step is done.
/// </summary>
/// <remarks>
/// <para>
/// Pure Blazor — no JavaScript interop. Each step is a native <c>&lt;input type="checkbox"&gt;</c>
/// associated with its own <c>&lt;label&gt;</c>, so Enter/Space activation, the tab order and the
/// checked state are the platform's; each group header is a native <c>&lt;button&gt;</c> carrying
/// <c>aria-expanded</c>/<c>aria-controls</c>, so the disclosure needs no key handling either.
/// </para>
/// <para>
/// Accessibility contract: exactly ONE polite live region per checklist (the visually hidden
/// announcer). A completed step, a reopened step and the overall completion are announced there and
/// nowhere else — the visible celebration block is not a live region, and the group disclosures
/// announce nothing because <c>aria-expanded</c> already reports their state. The progress bar is
/// named by its role ("Progress") while the visible sentence carries the counts, so no text is read
/// twice. When <see cref="CollapseCompletedGroups"/> collapses the group whose last step was just
/// checked, focus moves out of the hidden body onto that group's header — otherwise it would land
/// on <c>&lt;body&gt;</c>.
/// </para>
/// <para>
/// The checklist never edits the host's collection: it writes <see cref="OnboardingTask.IsDone"/> on
/// the instances it was given (so it is usable without a bound handler) and raises
/// <see cref="OnTaskToggle"/>. Dismissal hides the checklist locally in every case — a host that
/// never binds <see cref="OnDismiss"/> is not stuck with a surface it cannot close — and a NEW
/// <see cref="Tasks"/> list identity brings it back. A host that removes the checklist from its own
/// layout after <see cref="OnDismiss"/> owns the focus: move it to a stable element (a page heading
/// or the element that opened the guide), because the control that had focus no longer exists.
/// </para>
/// <para>
/// Host values never reach a DOM id: the checkbox, description and group-body ids are built from a
/// step's POSITION (its group index and its index inside that group), so two ids that reduce to the
/// same id-safe form cannot collide. An <see cref="OnboardingTask.Id"/> is the suffix of the step's
/// <c>data-testid</c> only. A step's <see cref="OnboardingTask.ActionHref"/> renders only when it is
/// a relative URL or uses the <c>http</c>, <c>https</c> or <c>mailto</c> scheme — a <c>javascript:</c>
/// or <c>data:</c> value is no target at all, so the action renders nothing.
/// </para>
/// </remarks>
public partial class TmOnboardingChecklist : TmComponentBase
{
    /// <summary>Separator that keeps the "no group" bucket from colliding with a group named like it.</summary>
    private const string UngroupedKey = "\u0000ungrouped";

    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementReference> _groupToggles = new(StringComparer.Ordinal);

    private string? _announcement;
    private bool _dismissed;
    private string? _pendingFocusGroup;
    private IReadOnlyList<OnboardingTask>? _seenTasks;


    /// <summary>
    /// The steps to work through, in render order. A step without a <see cref="OnboardingTask.Group"/>
    /// renders in the leading plain list, the grouped ones under their collapsible header (groups in
    /// the order of their first step). An empty or null list renders the localized empty state.
    /// </summary>
    [Parameter, EditorRequired] public IReadOnlyList<OnboardingTask> Tasks { get; set; } = [];

    /// <summary>
    /// Fires after a step was toggled, with the step whose <see cref="OnboardingTask.IsDone"/> the
    /// checklist has already written. Persist the value (or the whole list) from the handler.
    /// </summary>
    [Parameter] public EventCallback<OnboardingTask> OnTaskToggle { get; set; }

    /// <summary>
    /// Renders the dismiss button (an icon button whose accessible name is the localized "Dismiss
    /// checklist"). Default <c>false</c> — an onboarding checklist that can vanish needs a host that
    /// can bring it back.
    /// </summary>
    [Parameter] public bool Dismissible { get; set; }

    /// <summary>
    /// Fires when the user dismisses the checklist. The checklist hides itself first, so an unbound
    /// host still gets a closed surface; the handler persists the decision and, if it removes the
    /// component from the layout, moves the focus to a stable element.
    /// </summary>
    [Parameter] public EventCallback OnDismiss { get; set; }

    /// <summary>Presentation of the checklist. Defaults to <see cref="OnboardingChecklistLayout.List"/>.</summary>
    [Parameter] public OnboardingChecklistLayout Layout { get; set; } = OnboardingChecklistLayout.List;

    /// <summary>
    /// Visible title of the checklist. When set it also becomes the accessible name of the region
    /// (the root is a <c>&lt;section&gt;</c> named by this heading); when null the region keeps the
    /// localized "Onboarding checklist" name and no header row is rendered (unless
    /// <see cref="Dismissible"/> adds one for the close button).
    /// </summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>
    /// Heading level of <see cref="Title"/> (1–6), so the checklist can sit under the right page
    /// heading. Default 2; a value outside 1–6 falls back to 2.
    /// </summary>
    [Parameter] public int HeadingLevel { get; set; } = 2;

    /// <summary>
    /// Renders the progress block: a <c>TmProgressBar</c> plus the counted sentence
    /// ("3 of 7 tasks done", grammatical plural per language). Default <c>true</c>.
    /// </summary>
    [Parameter] public bool ShowProgress { get; set; } = true;

    /// <summary>
    /// Collapses a group automatically when its last step is checked. Default <c>false</c>: a group
    /// that folds under the user's cursor hides the work they just finished, so the opt-in is
    /// deliberate — and when it is on, the focus moves to the collapsed group's header so a keyboard
    /// user is never dropped on <c>&lt;body&gt;</c>.
    /// </summary>
    [Parameter] public bool CollapseCompletedGroups { get; set; }

    /// <summary>
    /// Renders the celebration block (an icon plus two lines of text — no confetti, no motion-only
    /// signal) once every required step is done. Default <c>true</c>.
    /// </summary>
    [Parameter] public bool ShowCelebration { get; set; } = true;

    /// <summary>
    /// Title of the celebration block. Null uses the localized "All done!".
    /// </summary>
    [Parameter] public string? CompletedTitle { get; set; }

    /// <summary>
    /// Body text of the celebration block. Null uses the localized sentence.
    /// </summary>
    [Parameter] public string? CompletedText { get; set; }

    /// <summary>Additional CSS class(es) on the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── derived state ───────────────────────────────────────────────────────────

    private string TitleId => $"tm-onboarding-checklist-{_instanceId}-title";

    private bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    private string EffectiveHeadingLevelText => HeadingLevel is >= 1 and <= 6
        ? HeadingLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "2";

    private int TotalCount => Tasks?.Count ?? 0;

    private int DoneCount => Tasks?.Count(task => task.IsDone) ?? 0;

    private int RequiredCount => Tasks?.Count(task => !task.IsOptional) ?? 0;

    private int RequiredDoneCount => Tasks?.Count(task => !task.IsOptional && task.IsDone) ?? 0;

    private bool HasTasks => TotalCount > 0;

    /// <summary>
    /// Whether the run is finished: at least one step exists and every required step is done.
    /// Optional steps never block it (a checklist of nothing but optional steps is complete).
    /// </summary>
    private bool IsComplete => HasTasks && RequiredDoneCount == RequiredCount;

    private string RootClass
    {
        get
        {
            var layout = Layout switch
            {
                OnboardingChecklistLayout.Card => "tm-onboarding-checklist--card",
                OnboardingChecklistLayout.Grid => "tm-onboarding-checklist--grid",
                _ => "tm-onboarding-checklist--list",
            };

            return string.IsNullOrWhiteSpace(Class)
                ? $"tm-onboarding-checklist {layout}"
                : $"tm-onboarding-checklist {layout} {Class}";
        }
    }

    /// <summary>
    /// The rounded percentage the progress bar reports. Whole percent, because the bar forwards it
    /// verbatim as <c>aria-valuenow</c> and a screen reader must not announce "42.857142857142854".
    /// </summary>
    private int ProgressPercentage => TotalCount == 0
        ? 0
        : (int)Math.Round(DoneCount * 100.0 / TotalCount, MidpointRounding.AwayFromZero);

    private string ProgressText => Loc.Plural("TmOnboardingChecklist_Progress", TotalCount, DoneCount);

    private string CompletedTitleText => string.IsNullOrWhiteSpace(CompletedTitle)
        ? Loc["TmOnboardingChecklist_CompletedTitle"]
        : CompletedTitle!;

    private string CompletedTextText => string.IsNullOrWhiteSpace(CompletedText)
        ? Loc["TmOnboardingChecklist_CompletedText"]
        : CompletedText!;

    private static string ActionClass => "tm-onboarding-checklist__action tm-btn tm-btn-ghost tm-btn-sm";

    private static string ItemClass(OnboardingTask task)
    {
        var css = "tm-onboarding-checklist__item";
        if (task.IsDone)
        {
            css += " tm-onboarding-checklist__item--done";
        }

        if (task.IsOptional)
        {
            css += " tm-onboarding-checklist__item--optional";
        }

        return css;
    }

    private static bool HasAction(OnboardingTask task)
        => !string.IsNullOrWhiteSpace(task.ActionLabel)
           && (task.OnAction is not null || SafeHref(task.ActionHref) is not null);

    /// <summary>
    /// The navigation target an <see cref="OnboardingTask.ActionHref"/> may actually render, or
    /// <c>null</c> when the value is not a safe link. A relative URL and the <c>http</c>, <c>https</c>
    /// and <c>mailto</c> schemes are accepted; everything else — <c>javascript:</c>, <c>data:</c>,
    /// <c>vbscript:</c> — is refused, so a hostile value supplied by the host's data can never run.
    /// </summary>
    /// <remarks>
    /// Relative references are resolved against the page, which is the intent of an onboarding
    /// action ("open the settings page"). A protocol-relative value (<c>//elsewhere.example</c>) is
    /// refused: it is technically relative but navigates off-origin, which is never what a setup step
    /// means. The value is trimmed first, because a browser strips leading control/space characters
    /// before it resolves the scheme (<c>" javascript:…"</c> still runs otherwise).
    /// </remarks>
    private static string? SafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var value = href.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
        {
            return null;
        }

        if (!uri.IsAbsoluteUri)
        {
            return value;
        }

        // Uri.Scheme is always lower-cased, so the comparison needs no normalisation of its own.
        return uri.Scheme is "http" or "https" or "mailto" ? value : null;
    }

    // ── groups ──────────────────────────────────────────────────────────────────

    /// <summary>One rendered group: the ungrouped bucket (<see cref="Name"/> null) or a named group.</summary>
    private sealed record OnboardingGroupView(string Key, string? Name, IReadOnlyList<OnboardingTask> Tasks)
    {
        public int DoneCount => Tasks.Count(task => task.IsDone);
    }

    /// <summary>The groups of this render, in the order of their first step.</summary>
    private List<OnboardingGroupView> GroupViews
    {
        get
        {
            var order = new List<string>();
            var names = new Dictionary<string, string?>(StringComparer.Ordinal);
            var buckets = new Dictionary<string, List<OnboardingTask>>(StringComparer.Ordinal);

            foreach (var task in Tasks ?? [])
            {
                var name = string.IsNullOrWhiteSpace(task.Group) ? null : task.Group!.Trim();
                var key = name ?? UngroupedKey;
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = [];
                    buckets[key] = bucket;
                    names[key] = name;
                    order.Add(key);
                }

                bucket.Add(task);
            }

            var views = new List<OnboardingGroupView>(order.Count);
            foreach (var key in order)
            {
                views.Add(new OnboardingGroupView(key, names[key], buckets[key]));
            }

            return views;
        }
    }

    private bool IsExpanded(OnboardingGroupView grp) => !_collapsedGroups.Contains(grp.Key);

    private string GroupProgressText(OnboardingGroupView grp)
        => Loc.Plural("TmOnboardingChecklist_Progress", grp.Tasks.Count, grp.DoneCount);

    /// <summary>
    /// The DOM id of one group's body, derived from the group's POSITION in the render. A name-derived
    /// id is lossy, so two different names that reduce to the same id-safe form ("Data.1" and "Data-1")
    /// would share one id and a header's <c>aria-controls</c> would point at its neighbour's body. Host
    /// values never reach an id — <see cref="OnboardingTask.Id"/> stays the suffix of the
    /// <c>data-testid</c> only.
    /// </summary>
    private string GroupBodyId(int groupIndex) => $"tm-onboarding-checklist-{_instanceId}-group-{groupIndex}";

    /// <summary>
    /// The disclosure header of one group. Built with the render tree rather than Razor markup for
    /// ONE reason: the header's element reference has to be captured per group, and <c>@ref</c> takes
    /// a single assignment target whose deferred capture callback would be overwritten by every loop
    /// iteration. The capture delegate below closes over the group's own key, so each header stays
    /// addressable when <see cref="CollapseCompletedGroups"/> has to hand the focus back to it.
    /// </summary>
    private RenderFragment GroupHeader(OnboardingGroupView grp, int index) => builder =>
    {
        var sequence = 0;
        builder.OpenElement(sequence++, "button");
        builder.AddAttribute(sequence++, "type", "button");
        builder.AddAttribute(sequence++, "class", "tm-onboarding-checklist__group-toggle");
        builder.AddAttribute(sequence++, "data-testid", TestId("onboarding-group-toggle-" + index));
        builder.AddAttribute(sequence++, "aria-expanded", BoolText(IsExpanded(grp)));
        builder.AddAttribute(sequence++, "aria-controls", GroupBodyId(index));
        builder.AddAttribute(sequence++, "onclick", EventCallback.Factory.Create(this, () => ToggleGroup(grp.Key)));
        builder.AddElementReferenceCapture(sequence++, element => _groupToggles[grp.Key] = element);

        builder.OpenElement(sequence++, "span");
        builder.AddAttribute(sequence++, "class", "tm-onboarding-checklist__group-title");
        builder.AddContent(sequence++, grp.Name);
        builder.CloseElement();

        builder.OpenElement(sequence++, "span");
        builder.AddAttribute(sequence++, "class", "tm-onboarding-checklist__group-count");
        builder.AddContent(sequence++, GroupProgressText(grp));
        builder.CloseElement();

        builder.OpenElement(sequence++, "span");
        builder.AddAttribute(sequence++, "class", "tm-onboarding-checklist__chevron");
        builder.AddAttribute(sequence++, "aria-hidden", "true");
        builder.OpenComponent<TmIcon>(sequence++);
        builder.AddComponentParameter(sequence++, nameof(TmIcon.Name), IconNames.ChevronDown);
        builder.AddComponentParameter(sequence++, nameof(TmIcon.Size), IconSize.Sm);
        builder.CloseComponent();
        builder.CloseElement();

        builder.CloseElement();
    };

    /// <summary>
    /// The DOM id of one step's checkbox, derived from the step's POSITION (its group and its index
    /// inside that group). Building it from <see cref="OnboardingTask.Id"/> let two different values
    /// that slug alike ("a.b" and "a-b", or two ids sharing their first 64 characters) collide, and a
    /// <c>&lt;label for&gt;</c> then pointed at the neighbour's box.
    /// </summary>
    private string CheckboxId(int groupIndex, int taskIndex)
        => $"tm-onboarding-checklist-{_instanceId}-check-{groupIndex}-{taskIndex}";

    /// <summary>The DOM id of one step's description — positional for the same reason as <see cref="CheckboxId"/>.</summary>
    private string DescriptionId(int groupIndex, int taskIndex)
        => $"tm-onboarding-checklist-{_instanceId}-desc-{groupIndex}-{taskIndex}";

    // ── interaction ─────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        // A NEW Tasks list identity is a new onboarding run: the checklist comes back after a
        // dismissal. The dismissal itself stays local (the host may not bind OnDismiss at all).
        if (!ReferenceEquals(_seenTasks, Tasks))
        {
            _seenTasks = Tasks;
            _dismissed = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusGroup is not { } key)
        {
            return;
        }

        _pendingFocusGroup = null;
        if (_groupToggles.TryGetValue(key, out var toggle))
        {
            await toggle.FocusAsync();
        }
    }

    private async Task ToggleTaskAsync(OnboardingTask task)
    {
        // The checkbox flips the model, not a shadow copy: the component works unbound and the
        // host's OnTaskToggle handler sees the already-updated step.
        task.IsDone = !task.IsDone;
        _announcement = BuildAnnouncement(task, task.IsDone);

        if (task.IsDone && CollapseCompletedGroups)
        {
            CollapseFinishedGroupOf(task);
        }

        await OnTaskToggle.InvokeAsync(task);
    }

    private async Task RunActionAsync(OnboardingTask task)
    {
        if (task.OnAction is null)
        {
            return;
        }

        await task.OnAction(task);
    }

    private void ToggleGroup(string key)
    {
        if (!_collapsedGroups.Remove(key))
        {
            _collapsedGroups.Add(key);
        }
    }

    private async Task DismissAsync()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        await OnDismiss.InvokeAsync();
    }

    private void CollapseFinishedGroupOf(OnboardingTask task)
    {
        var name = string.IsNullOrWhiteSpace(task.Group) ? null : task.Group!.Trim();
        if (name is null)
        {
            return;
        }

        // Only the steps of THIS group decide whether it is finished, so the whole GroupViews list
        // (buckets, order, per-group counts) is not built just to answer that one question.
        var finished = (Tasks ?? []).Where(step => SameGroup(step, name)).All(step => step.IsDone);
        if (!finished || !_collapsedGroups.Add(name))
        {
            return;
        }

        // The step that had focus is now inside a hidden body: hand the focus to the header that
        // reports the new state (aria-expanded="false") instead of letting it fall to <body>.
        _pendingFocusGroup = name;
    }

    /// <summary>
    /// Whether a step belongs to the group named <paramref name="name"/> — the same rule
    /// <see cref="GroupViews"/> buckets by (a blank group is the ungrouped bucket, never a name).
    /// </summary>
    private static bool SameGroup(OnboardingTask task, string name)
        => !string.IsNullOrWhiteSpace(task.Group)
           && string.Equals(task.Group!.Trim(), name, StringComparison.Ordinal);

    private string BuildAnnouncement(OnboardingTask task, bool isDone)
    {
        var text = isDone
            ? Loc["TmOnboardingChecklist_AnnounceTaskDone", task.Title, ProgressText]
            : Loc["TmOnboardingChecklist_AnnounceTaskReopened", task.Title, ProgressText];

        // Two sentences in ONE live-region update — the region carries the finished step AND the
        // overall completion, so the announcement is never split into two competing messages. The
        // separator is part of the resource: punctuation belongs to the translator, not to the C#.
        return isDone && IsComplete
            ? Loc["TmOnboardingChecklist_AnnounceTaskDoneAllComplete", text,
                Loc["TmOnboardingChecklist_AnnounceAllDone"]]
            : text;
    }
}
