using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Renders a numeric signing step with min, max, step, and required validation on the core
/// <c>TmTextInput</c> (Signing redesign phase 4, SI1). The input is <c>type="text"</c> +
/// <c>inputmode="decimal"</c> and parses with the FIELD culture, so the Czech decimal comma is a
/// first-class input — the old native <c>type="number"</c> + InvariantCulture parse rejected it
/// (CHANGELOG Migration). The allowed range renders as a localized hint formatted with the field
/// culture instead of native min/max attributes, which are inert on a text input.
/// </summary>
public partial class TmSigningNumberStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private string? _validationMessage;
    private string? _displayText;

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

    /// <summary>Culture used to resolve localized field text and to parse and format the number.</summary>
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
        _displayText = FormatNumber(Value);
    }

    private CultureInfo StepCulture
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Culture))
            {
                return CultureInfo.InvariantCulture;
            }

            try
            {
                return CultureInfo.GetCultureInfo(Culture);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }

    /// <summary>DOM id of the range hint, derived from this instance's description id.</summary>
    private string RangeHintId => $"{_descriptionId}-range";

    private string DisplayValue => _displayText ?? string.Empty;

    private bool HasRangeHint => TryParseDecimal(Field.Validation?.Min, out _)
        || TryParseDecimal(Field.Validation?.Max, out _);

    /// <summary>The allowed range in the field culture ("Allowed range: 1 – 1 000 000" for cs-CZ).</summary>
    private string RangeHintText
    {
        get
        {
            var min = TryParseDecimal(Field.Validation?.Min, out var parsedMin) ? FormatDecimal(parsedMin) : string.Empty;
            var max = TryParseDecimal(Field.Validation?.Max, out var parsedMax) ? FormatDecimal(parsedMax) : string.Empty;
            return string.Format(StepCulture, Loc["TmSigningStep_NumberRange"], min, max);
        }
    }

    private string PlaceholderText => SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture, Loc["TmSigningStep_NumberPlaceholder"]);

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>The invalid state the TmTextInput mirrors (aria-invalid + error class); the shell owns the alert.</summary>
    private bool? ValidationState => HasValidationMessage ? false : null;

    /// <summary>
    /// The attributes the TmTextInput splats onto the native input: the shell title id as the
    /// accessible name (phase 2 contract), the decimal keyboard hint, and — as the description —
    /// the range hint, the field description and the validation alert (while they exist).
    /// </summary>
    private Dictionary<string, object> BuildInputAttributes()
    {
        var attributes = new Dictionary<string, object>
        {
            ["aria-labelledby"] = _titleId,
            ["inputmode"] = "decimal"
        };

        var describedBy = JoinIds(
            HasDescription ? _descriptionId : null,
            HasRangeHint ? RangeHintId : null,
            HasValidationMessage ? _validationId : null);
        if (describedBy is not null)
        {
            attributes["aria-describedby"] = describedBy;
        }

        return attributes;
    }

    private string ShellClass => string.Join(" ", new[] { "tm-signing-number-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    /// <summary>
    /// The per-keystroke input path: update the display, raise the parsed value (null while the
    /// text does not parse yet) and CLEAR an existing message. Validation itself runs on COMMIT
    /// (blur / change) — an alert in the middle of typing is noise, not feedback.
    /// </summary>
    private async Task HandleValueChangedAsync(string? text)
    {
        _displayText = text;
        if (string.IsNullOrWhiteSpace(text))
        {
            await ValueChanged.InvokeAsync(null);
            return;
        }

        await ValueChanged.InvokeAsync(TryParseNumber(text, out var number) ? number : null);
        ClearOwnValidationMessage();
    }

    /// <summary>
    /// The COMMIT path (blur): this is where the message is computed — required, a finished but
    /// unparsable token, or the min/max range.
    /// </summary>
    private Task HandleCommitAsync()
    {
        var text = _displayText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _validationMessage = Field.Required
                ? SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_Required"])
                : null;
            return Task.CompletedTask;
        }

        if (!TryParseNumber(text, out var number))
        {
            _validationMessage = Loc["TmSigningStep_InvalidNumber"];
            return Task.CompletedTask;
        }

        _validationMessage = Validate(number);
        return Task.CompletedTask;
    }

    private void ClearOwnValidationMessage()
    {
        _validationMessage = null;
    }

    private Task HandleBlurAsync()
    {
        // A commit only matters when the user actually typed into the field — validating a blur
        // that leaves an untouched (and optional) field would light it up for no reason.
        if (_displayText is null)
        {
            return Task.CompletedTask;
        }

        return HandleCommitAsync();
    }

    private string? Validate(decimal number)
    {
        if (TryParseDecimal(Field.Validation?.Min, out var min) && number < min)
        {
            return SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_MinValue", FormatDecimal(min)]);
        }

        if (TryParseDecimal(Field.Validation?.Max, out var max) && number > max)
        {
            return SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_MaxValue", FormatDecimal(max)]);
        }

        return null;
    }

    /// <summary>
    /// Parses the typed number with the FIELD culture first, then the invariant culture — an en
    /// template rendered under a cs browser (or the reverse) must still accept the number the
    /// user actually typed. Each attempt uses <see cref="NumberStyles.Float"/> — deliberately
    /// WITHOUT <see cref="NumberStyles.AllowThousands"/>: typed text is not preformatted output,
    /// so "1,5" under en-US is NOT one thousand five (it is an invalid half — the decimal point
    /// was mistyped). A group separator is stripped ONLY when the culture groups with a
    /// whitespace-like character (the NBSP of cs-CZ "1 000,5"), where it is an invisible
    /// formatting artifact; a "," or "." group separator stays typed intent and keeps failing.
    /// </summary>
    private bool TryParseNumber(string text, out decimal number)
    {
        foreach (var culture in new[] { StepCulture, CultureInfo.InvariantCulture })
        {
            if (TryParseWithCulture(text, culture, out number))
            {
                return true;
            }
        }

        number = 0m;
        return false;
    }

    private static bool TryParseWithCulture(string text, CultureInfo culture, out decimal number)
    {
        var candidate = text;
        var groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        if (IsWhitespaceSeparator(groupSeparator))
        {
            candidate = new string(candidate.Where(character => !IsWhitespaceLike(character)).ToArray());
        }

        return decimal.TryParse(candidate, NumberStyles.Float, culture, out number);
    }

    private static bool IsWhitespaceSeparator(string separator) =>
        !string.IsNullOrEmpty(separator) && separator.All(IsWhitespaceLike);

    private static bool IsWhitespaceLike(char character) =>
        character is ' ' or '\u00A0' or '\u202F'
        || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.SpaceSeparator;

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private string FormatDecimal(decimal value) =>
        value == decimal.Truncate(value)
            ? value.ToString("N0", StepCulture)
            : value.ToString(StepCulture);

    private string? FormatNumber(decimal? value) =>
        value is null ? null : value.Value.ToString(StepCulture);

    private static string? JoinIds(params string?[] ids)
    {
        var parts = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }
}
