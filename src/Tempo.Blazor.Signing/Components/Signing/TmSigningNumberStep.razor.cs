using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Renders a numeric signing step with min, max, step, and required validation.</summary>
public partial class TmSigningNumberStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private string? _validationMessage;

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Number };

    /// <summary>Current numeric value.</summary>
    [Parameter] public decimal? Value { get; set; }

    /// <summary>Callback invoked when the numeric value changes.</summary>
    [Parameter] public EventCallback<decimal?> ValueChanged { get; set; }

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

    private string? FormattedValue => Value?.ToString(CultureInfo.InvariantCulture);

    private string PlaceholderText => SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture, Loc["TmSigningStep_NumberPlaceholder"]);

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>
    /// The ids the input references — per-instance, and the SAME instances the shell renders (they
    /// are passed to the shell as parameters), so every reference resolves inside the rendered DOM
    /// (Signing redesign phase 2).
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

    private string ShellClass => string.Join(" ", new[] { "tm-signing-number-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private static string BoolText(bool value) => value ? "true" : "false";

    private async Task HandleValueChangedAsync(ChangeEventArgs args)
    {
        var text = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            _validationMessage = Field.Required
                ? SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_Required"])
                : null;
            await ValueChanged.InvokeAsync(null);
            return;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            _validationMessage = Loc["TmSigningStep_InvalidNumber"];
            await ValueChanged.InvokeAsync(null);
            return;
        }

        _validationMessage = Validate(number);
        await ValueChanged.InvokeAsync(number);
    }

    private string? Validate(decimal number)
    {
        if (TryParseDecimal(Field.Validation?.Min, out var min) && number < min)
        {
            return SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_MinValue", min]);
        }

        if (TryParseDecimal(Field.Validation?.Max, out var max) && number > max)
        {
            return SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_MaxValue", max]);
        }

        return null;
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }
}
