using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Renders a text-like signing step with optional multiline, cells, and regex validation on the
/// core <c>TmTextInput</c> / <c>TmTextArea</c> components (Signing redesign phase 4, SI1).
/// </summary>
public partial class TmSigningTextStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private string? _validationMessage;
    private string? _value;

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new();

    /// <summary>Current text value.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Callback invoked when the text value changes.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Document area used to infer cells maxlength.</summary>
    [Parameter] public SigningFieldArea? Area { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether the input is disabled.</summary>
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

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The counter and the input mirror the host's value so typing stays reflected even when a
        // test host (or a runner render race) has not fed the parameter back yet.
        _value = Value;
    }

    private bool IsMultiline => string.Equals(Field.Preferences.Format, "multiline", StringComparison.OrdinalIgnoreCase)
        || TryGetBoolSetting("multiline");

    private int? MaxLength => Field.Type == SigningFieldType.Cells
        ? GetCellCount()
        : TryGetIntSetting("maxLength");

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    private string? ValidationDescriptionId => HasValidationMessage ? _validationId : null;

    /// <summary>
    /// The invalid state the TmTextInput renders (<c>aria-invalid</c> + the error class): the shell
    /// owns the alert, the input only mirrors the invalid state.
    /// </summary>
    private bool? ValidationState => HasValidationMessage ? false : null;

    /// <summary>
    /// The attributes the Tm input splats onto the native element: this instance's title id as the
    /// accessible name (phase 2 contract) and the description / validation ids (while they render)
    /// as the description. The native <c>maxlength</c> / <c>pattern</c> attributes ride along so
    /// the browser enforces what the field template declares. While a validation message shows,
    /// <c>aria-invalid="true"</c> rides along too: both core inputs render their own attributes
    /// BEFORE the splat, and the multiline <c>TmTextArea</c> has no <c>IsValid</c> parameter, so
    /// the splat is the only channel that marks it invalid.
    /// </summary>
    private Dictionary<string, object> BuildInputAttributes()
    {
        var attributes = new Dictionary<string, object>
        {
            ["aria-labelledby"] = _titleId
        };

        if (HasValidationMessage)
        {
            attributes["aria-invalid"] = "true";
        }

        if (DescribedBy is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        if (MaxLength is { } maxLength)
        {
            attributes["maxlength"] = maxLength;
        }

        if (!IsMultiline && !string.IsNullOrWhiteSpace(Field.Validation?.Pattern))
        {
            attributes["pattern"] = Field.Validation!.Pattern!;
        }

        return attributes;
    }

    /// <summary>
    /// The ids this step's inputs reference — per-instance, and the SAME instances the shell
    /// renders (they are passed to the shell as parameters), so every reference resolves inside
    /// the rendered DOM (Signing redesign phase 2).
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

            if (ValidationDescriptionId is not null)
            {
                parts.Add(ValidationDescriptionId);
            }

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    private string PlaceholderText => SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture, Loc["TmSigningStep_TextPlaceholder"]);

    private string ShellClass => string.Join(" ", new[] { "tm-signing-text-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private bool IsAtLimit(int maxLength) => (_value?.Length ?? 0) >= maxLength;

    /// <summary>
    /// The per-keystroke input path (single line): update the value, raise it and CLEAR an
    /// existing message. Validation runs on COMMIT — <see cref="HandleCommitAsync"/> on blur for
    /// the single-line input; the multiline <c>TmTextArea</c> has no input events to speak of
    /// (<c>Immediate=false</c> raises <see cref="TmTextArea.ValueChanged"/> on the change event),
    /// so its value path IS the commit and validates right away.
    /// </summary>
    private async Task HandleValueChangedAsync(string? value)
    {
        _value = value;
        if (IsMultiline)
        {
            _validationMessage = Validate(value);
        }
        else
        {
            _validationMessage = null;
        }

        await ValueChanged.InvokeAsync(value);
    }

    /// <summary>The COMMIT path (blur) of the single-line input: compute the validation message.</summary>
    private Task HandleCommitAsync()
    {
        _validationMessage = Validate(_value);
        return Task.CompletedTask;
    }

    private string? Validate(string? value)
    {
        if (Field.Required && string.IsNullOrWhiteSpace(value))
        {
            return SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_Required"]);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            if (!string.IsNullOrWhiteSpace(Field.Validation?.Pattern)
                && !Regex.IsMatch(value, Field.Validation.Pattern))
            {
                return SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_InvalidPattern"]);
            }

            // Validation.Min doubles as the minimum LENGTH for text-like fields (see
            // SigningFieldValidation.Min). Only a whole, positive number is a length — a date or
            // numeric min ("2024-01-01", "5.5") is meaningless here.
            if (TryParseLength(Field.Validation?.Min, out var minLength) && value.Length < minLength)
            {
                return Loc["TmSigningStep_MinLength", minLength];
            }
        }

        return null;
    }

    private static bool TryParseLength(string? value, out int length)
    {
        length = 0;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length)
            && length > 0;
    }

    private int? GetCellCount()
    {
        if (Area is { CellWidth: > 0, Width: > 0 } area)
        {
            var cellWidth = area.CellWidth.GetValueOrDefault();
            return Math.Max(1, (int)Math.Floor(area.Width / cellWidth));
        }

        return TryGetIntSetting("cells");
    }

    private bool TryGetBoolSetting(string key)
    {
        return Field.Preferences.AdditionalSettings.TryGetValue(key, out var value)
            && value is bool boolValue
            && boolValue;
    }

    private int? TryGetIntSetting(string key)
    {
        if (!Field.Preferences.AdditionalSettings.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            string text when int.TryParse(text, out var result) => result,
            _ => null
        };
    }
}
