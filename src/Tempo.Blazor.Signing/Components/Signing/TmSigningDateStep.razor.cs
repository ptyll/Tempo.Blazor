using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Renders a date, month, or date-time signing step.</summary>
public partial class TmSigningDateStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private string? _validationMessage;

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Date };

    /// <summary>Current date value formatted for the selected input type.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Callback invoked when the date value changes.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether the input is disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether to show the button that fills today's date.</summary>
    [Parameter] public bool ShowTodayButton { get; set; } = true;

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

    private string InputType => Field.Preferences.Format?.ToLowerInvariant() switch
    {
        "month" => "month",
        "datetime" or "datetime-local" => "datetime-local",
        _ => "date"
    };

    private string ShellClass => string.Join(" ", new[] { "tm-signing-date-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private string PlaceholderText => SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture);

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

    private static string BoolText(bool value) => value ? "true" : "false";

    private async Task HandleValueChangedAsync(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        _validationMessage = Field.Required && string.IsNullOrWhiteSpace(value)
            ? SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_Required"])
            : null;
        await ValueChanged.InvokeAsync(value);
    }

    private Task SetTodayAsync()
    {
        var today = DateTime.Today;
        var value = InputType switch
        {
            "month" => today.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            "datetime-local" => today.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            _ => today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        _validationMessage = null;
        return ValueChanged.InvokeAsync(value);
    }

    private string? NormalizeDateToken(string? value)
    {
        if (string.Equals(value, "{{date}}", StringComparison.OrdinalIgnoreCase))
        {
            return InputType == "month"
                ? DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value;
    }
}
