using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Shared layout shell for linear signing steps.</summary>
/// <remarks>
/// <para>
/// Signing redesign phase 2: the shell owns the step's accessible plumbing. The title heading
/// carries an HTML id (plus <c>tabindex="-1"</c>) and the shell focuses the title whenever the
/// represented field changes or the host's step navigation signal fires, so keyboard and
/// screen-reader users always land at the top of the step they are shown. The ids travel to
/// descendants through a <see cref="CascadingValue{TValue}"/> of
/// <see cref="TmSigningStepShellContext"/>.
/// </para>
/// <para>
/// The ids are PER COMPONENT INSTANCE: the host (the step component) passes them through the
/// <see cref="TitleId"/> / <see cref="DescriptionId"/> / <see cref="ValidationMessageId"/>
/// parameters, and the shell falls back to ids generated once per instance. Two rendered steps
/// never share an id, even when they represent the same field uuid.
/// </para>
/// <para>
/// Validation shows exactly ONE alert per step: the step's own <see cref="ValidationMessage"/>
/// wins, otherwise the runner's step validation (forwarded through the named
/// <c>TmSigningRunnerValidation</c> cascade) is shown.
/// </para>
/// </remarks>
public partial class TmSigningStepShell
{
    private readonly string _generatedTitleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _generatedDescriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _generatedValidationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private ElementReference _titleElement;
    private bool _fieldInitialized;
    private string _lastFieldUuid = string.Empty;
    private int _seenFocusVersion;
    private bool _pendingTitleFocus;

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField? Field { get; set; }

    /// <summary>Explicit label overriding the field title or name.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Optional description rendered below the label.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Culture used to resolve field labels and descriptions.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when the requested culture is missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Whether the step is required. Defaults to the field requirement.</summary>
    [Parameter] public bool? Required { get; set; }

    /// <summary>Validation message shown below the step content.</summary>
    [Parameter] public string? ValidationMessage { get; set; }

    /// <summary>Optional HTML id applied to the validation message for aria-describedby links. Generated per instance when omitted.</summary>
    [Parameter] public string? ValidationMessageId { get; set; }

    /// <summary>Optional HTML id for the title heading. Generated per instance when omitted.</summary>
    [Parameter] public string? TitleId { get; set; }

    /// <summary>Optional HTML id for the description block. Generated per instance when omitted.</summary>
    [Parameter] public string? DescriptionId { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Step body content.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Step navigation signal cascaded by the form runner (absent for standalone steps).</summary>
    [CascadingParameter]
    private TmSigningStepFocusSignal? FocusSignal { get; set; }

    /// <summary>The runner's step validation, cascaded by <see cref="TmSigningFormRunner"/> (absent for standalone steps).</summary>
    [CascadingParameter(Name = "TmSigningRunnerValidation")]
    private string? RunnerValidationMessage { get; set; }

    /// <summary>Cascaded ids of this step's accessible plumbing.</summary>
    internal TmSigningStepShellContext Context => new(
        TitleElementId,
        HasDescription ? DescriptionElementId : null,
        HasValidationMessage ? ValidationMessageElementId : null);

    /// <summary>The HTML id carried by the title heading.</summary>
    internal string TitleElementId => !string.IsNullOrWhiteSpace(TitleId) ? TitleId! : _generatedTitleId;

    private string DescriptionElementId => !string.IsNullOrWhiteSpace(DescriptionId) ? DescriptionId! : _generatedDescriptionId;

    private string ValidationMessageElementId => !string.IsNullOrWhiteSpace(ValidationMessageId)
        ? ValidationMessageId!
        : _generatedValidationId;

    private bool IsRequired => Required ?? Field?.Required == true;

    private bool HasDescription => !string.IsNullOrWhiteSpace(ResolvedDescription);

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage)
        ? ValidationMessage
        : RunnerValidationMessage;

    private string DisplayLabel => !string.IsNullOrWhiteSpace(Label)
        ? Label
        : SigningTextResolver.FieldLabel(Field, Culture, FallbackCulture, Loc);

    private string ResolvedDescription => !string.IsNullOrWhiteSpace(Description)
        ? Description!
        : SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture);

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-step-shell" };
            if (IsRequired)
            {
                classes.Add("tm-signing-step-shell--required");
            }

            if (HasValidationMessage)
            {
                classes.Add("tm-signing-step-shell--invalid");
            }

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var fieldUuid = Field?.Uuid ?? string.Empty;
        var focusVersion = FocusSignal?.Version ?? 0;

        if (!_fieldInitialized)
        {
            // First render: record the baseline. Focus only when the shell MOUNTS in response to a
            // step navigation (elevated signal version) — never on a plain page load.
            _fieldInitialized = true;
            _lastFieldUuid = fieldUuid;
            _seenFocusVersion = focusVersion;
            _pendingTitleFocus = focusVersion > 0;
            return;
        }

        if (!string.Equals(_lastFieldUuid, fieldUuid, StringComparison.Ordinal))
        {
            _lastFieldUuid = fieldUuid;
            _seenFocusVersion = focusVersion;
            _pendingTitleFocus = true;
            return;
        }

        if (focusVersion != _seenFocusVersion)
        {
            // Navigation signal fired for the same step (the runner re-selected this step).
            _seenFocusVersion = focusVersion;
            _pendingTitleFocus = true;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_pendingTitleFocus)
        {
            return;
        }

        _pendingTitleFocus = false;
        await FocusTitleAsync();
    }

    /// <summary>
    /// Moves focus to the step title heading. Best effort: a view whose JS circuit is already gone
    /// keeps the exception local instead of crashing the host — see
    /// <see cref="SigningFocus.TryFocusAsync"/>.
    /// </summary>
    public async Task FocusTitleAsync()
    {
        await SigningFocus.TryFocusAsync(_titleElement);
    }

    private string GetFieldTypeLabel(SigningFieldType type)
    {
        return SigningTextResolver.FieldTypeLabel(type, Loc);
    }

    private static string FormatDescription(string description)
    {
        var encoded = WebUtility.HtmlEncode(description);
        encoded = Regex.Replace(encoded, "\\*\\*(.+?)\\*\\*", "<strong>$1</strong>");
        encoded = Regex.Replace(encoded, "\\*(.+?)\\*", "<em>$1</em>");
        return encoded.Replace("\n", "<br>");
    }
}

/// <summary>
/// The accessible-name plumbing of one signing step: the shell's title id (the accessible name of
/// every unlabelled input of the step) and, when they exist, the description and validation ids.
/// The shell cascades the context to its content, so a step's inputs (or a host's custom step
/// content) can reference the ids with <c>aria-labelledby</c> / <c>aria-describedby</c>. The ids
/// are unique per rendered step component — the step components own them and pass them to the
/// shell through its <c>TitleId</c> / <c>DescriptionId</c> / <c>ValidationMessageId</c>
/// parameters, so two rendered steps never share an id even for the same field uuid.
/// </summary>
public sealed record TmSigningStepShellContext(string TitleId, string? DescriptionId, string? ValidationMessageId);

/// <summary>
/// Step navigation signal the runner cascades to its step shells: the runner bumps
/// <see cref="Version"/> whenever it moves to another step, and a shell seeing a new version moves
/// focus onto its title. Same instance across renders, so the cascade never forces extra renders.
/// Internal: the signal is coordination between the runner and its own step shells, not host API.
/// </summary>
internal sealed class TmSigningStepFocusSignal
{
    /// <summary>Monotonic navigation generation. Starts at 0 (no navigation yet — no focus on first load).</summary>
    public int Version { get; internal set; }
}
