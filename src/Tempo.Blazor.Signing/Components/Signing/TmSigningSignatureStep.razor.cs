using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Renders a signature or initials signing step inside the shared step shell.</summary>
/// <remarks>
/// <para>
/// Signing redesign phase 2: the form runner renders this wrapper instead of a bare
/// <see cref="TmSignatureCapture"/>, so a signature step finally carries the step title, the
/// required / optional badge and the "appears on" hint like every other step. The capture is
/// wrapped in a <c>role="group"</c> element named by the step title id, so assistive technology
/// announces which field the canvas belongs to.
/// </para>
/// <para>
/// The shell owns the required / invalid presentation of the step (the badge, the single
/// <c>role="alert"</c> validation message and <c>aria-invalid</c>), so the embedded capture is
/// deliberately driven with <c>Required = false</c> — otherwise a required signature step would
/// show TWO "signature is required" alerts (the capture's own plus the shell's). One alert per
/// step is the contract; do not "fix" the capture back to <c>Required = true</c>.
/// </para>
/// <para>
/// The signing instruction is a mode-neutral sentence ("Add your signature in the field below."):
/// the offered <see cref="Modes"/> can be restricted by the host, and a per-mode instruction would
/// multiply the localized strings for little gain.
/// </para>
/// </remarks>
public partial class TmSigningSignatureStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Signature };

    /// <summary>Current signature value (typed text, drawn SVG or uploaded data URL).</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Callback invoked when the signature value changes.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Whether the capture draws initials instead of a full signature. Defaults to the field type.</summary>
    [Parameter] public bool? Initials { get; set; }

    /// <summary>Current capture mode.</summary>
    [Parameter] public TmSignatureCaptureMode Mode { get; set; } = TmSignatureCaptureMode.Typed;

    /// <summary>Callback invoked when the capture mode changes.</summary>
    [Parameter] public EventCallback<TmSignatureCaptureMode> ModeChanged { get; set; }

    /// <summary>Offered capture modes. Defaults to draw, type and upload.</summary>
    [Parameter] public IReadOnlyList<TmSignatureCaptureMode>? Modes { get; set; }

    /// <summary>Validation message shown by the step shell below the capture.</summary>
    [Parameter] public string? ValidationMessage { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether the controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Culture used to resolve localized field text.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized field text is missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Additional CSS classes for the shell element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the shell element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The runner's step validation, cascaded by <see cref="TmSigningFormRunner"/> (absent for standalone steps).</summary>
    [CascadingParameter(Name = "TmSigningRunnerValidation")]
    private string? RunnerValidationMessage { get; set; }

    private bool IsInitials => Initials ?? Field.Type == SigningFieldType.Initials;

    private bool HasDescription => !string.IsNullOrWhiteSpace(ResolvedDescription);

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage)
        ? ValidationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>
    /// The ids the capture's <c>role="group"</c> wrapper references — per-instance, and the SAME
    /// instances the shell renders (they are passed to the shell as parameters), so every
    /// reference resolves inside the rendered DOM (Signing redesign phase 2).
    /// </summary>
    private string? DescribedBy
    {
        get
        {
            var parts = new List<string>(2);
            if (HasDescription)
            {
                parts.Add(_descriptionId);
            }

            if (HasValidationMessage)
            {
                parts.Add(_validationId);
            }

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    /// <summary>
    /// The step description the shell renders — the field description when the template carries
    /// one, otherwise the localized mode-neutral instruction.
    /// </summary>
    private string? ResolvedDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture))
        ? null
        : Loc["TmSigningSignatureStep_Instruction"];

    private string ShellClass => string.Join(" ", new[] { "tm-signing-signature-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private static string BoolText(bool value) => value ? "true" : "false";
}
