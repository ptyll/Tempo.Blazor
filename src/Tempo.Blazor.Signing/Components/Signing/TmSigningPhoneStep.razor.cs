using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Renders a phone verification signing step with country code, phone input, and OTP state on the
/// core <see cref="TmSelect"/> / <see cref="TmTextInput"/> components (Signing redesign phase 4,
/// SI1). Every control carries a visible localized label; the OTP input carries
/// <c>inputmode="numeric"</c> + <c>autocomplete="one-time-code"</c>.
/// </summary>
/// <remarks>
/// KNOWN LIMITATION (cosmetic in this package): "Send code" only raises <see cref="OnSendCode"/> —
/// no SMS is dispatched and the OTP is not checked against anything. The typed code is stored
/// verbatim as the field value; a host must wire a real OTP provider (Signing redesign phase 6)
/// before treating the step as verified.
/// </remarks>
public partial class TmSigningPhoneStep
{
    private static readonly PhoneCountryOption[] DefaultCountryOptions =
    [
        new("+1", "US +1"),
        new("+420", "CZ +420"),
        new("+421", "SK +421"),
        new("+44", "UK +44")
    ];

    /// <summary>Example dialling formats shown as the number input's placeholder (per calling code).</summary>
    private static readonly Dictionary<string, string> ExamplePhoneFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["+1"] = "(555) 123-4567",
        ["+420"] = "777 123 456",
        ["+421"] = "912 345 678",
        ["+44"] = "7911 123456"
    };

    private bool _codeSent;
    private string? _validationMessage;

    // The picked country is LOCAL state (Signing redesign phase 4 review): the caller is not
    // required to bind CountryCodeChanged, so without the local field the select snapped back to
    // the culture default on the next render — the pick was lost before the first keystroke.
    private string? _countryCode;
    private string? _lastCountryCodeParameter;

    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Phone };

    /// <summary>
    /// Selected country calling code. When omitted (null), the default comes from
    /// <see cref="Culture"/>: +420 for Czech, +421 for Slovak and +1 for every other
    /// culture — the old hardcoded "+1" default opened a Czech signer's step on the wrong prefix
    /// (Signing redesign phase 4). A culture default the host's <see cref="CountryOptions"/>
    /// do not offer falls back to the FIRST option, so the select never shows a value it cannot
    /// actually represent.
    /// </summary>
    [Parameter] public string? CountryCode { get; set; }

    /// <summary>Callback invoked when country code changes.</summary>
    [Parameter] public EventCallback<string> CountryCodeChanged { get; set; }

    /// <summary>Local phone number.</summary>
    [Parameter] public string? PhoneNumber { get; set; }

    /// <summary>Callback invoked when phone number changes.</summary>
    [Parameter] public EventCallback<string?> PhoneNumberChanged { get; set; }

    /// <summary>Normalized full phone number.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Callback invoked when the normalized phone value changes.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Current OTP code.</summary>
    [Parameter] public string? OtpCode { get; set; }

    /// <summary>Callback invoked when OTP code changes.</summary>
    [Parameter] public EventCallback<string?> OtpCodeChanged { get; set; }

    /// <summary>Whether the OTP input is already visible.</summary>
    [Parameter] public bool CodeSent { get; set; }

    /// <summary>Seconds shown in the resend countdown state.</summary>
    [Parameter] public int ResendSeconds { get; set; } = 30;

    /// <summary>Available country code options.</summary>
    [Parameter] public IReadOnlyList<PhoneCountryOption> CountryOptions { get; set; } = DefaultCountryOptions;

    /// <summary>Callback invoked when a verification code should be sent.</summary>
    [Parameter] public EventCallback<string> OnSendCode { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether the controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Culture used to resolve localized field text and to derive the default country code.</summary>
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

    private string ShellClass => string.Join(" ", new[] { "tm-signing-phone-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The host's CountryCode stays the source of truth whenever it CHANGES (including back to
        // null); between renders the local pick from HandleCountryChangedAsync wins, so a host
        // without a @bind-CountryCode does not snap the select back.
        if (!string.Equals(_lastCountryCodeParameter, CountryCode, StringComparison.Ordinal))
        {
            _lastCountryCodeParameter = CountryCode;
            _countryCode = CountryCode;
        }
    }

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>The invalid state the phone input mirrors (aria-invalid + error class); the shell owns the alert.</summary>
    private bool? ValidationState => HasValidationMessage ? false : null;

    /// <summary>
    /// The country code in effect: the LOCAL pick (the select's own state), then the host's
    /// <see cref="CountryCode"/>, then the culture default — which falls back to the FIRST
    /// available option when the options do not offer it.
    /// </summary>
    private string EffectiveCountryCode
    {
        get
        {
            var candidate = _countryCode ?? CountryCode;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate!;
            }

            var cultureDefault = DefaultCountryFromCulture(Culture);
            return CountryOptions.Any(option => string.Equals(option.Code, cultureDefault, StringComparison.Ordinal))
                ? cultureDefault
                : CountryOptions.Count > 0
                    ? CountryOptions[0].Code
                    : cultureDefault;
        }
    }

    private List<SelectOption<string>> CountrySelectOptions => CountryOptions
        .Select(option => new SelectOption<string>(option.Code, option.Label))
        .ToList();

    /// <summary>
    /// The ids the phone input references — per-instance, and the SAME instances the shell renders
    /// (they are passed to the shell as parameters), so every reference resolves inside the
    /// rendered DOM (Signing redesign phase 2).
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

    private Dictionary<string, object> BuildPhoneAttributes()
    {
        var attributes = new Dictionary<string, object>();
        if (DescribedBy is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        return attributes;
    }

    /// <summary>The mobile keyboard and OTP autofill hints on the native OTP input.</summary>
    private static Dictionary<string, object> BuildOtpAttributes() => new()
    {
        ["inputmode"] = "numeric",
        ["autocomplete"] = "one-time-code"
    };

    private static string BoolText(bool value) => value ? "true" : "false";

    /// <summary>
    /// The number input's placeholder: the template's own placeholder when it carries one,
    /// otherwise an EXAMPLE FORMAT for the selected calling code (a repeated "Phone number"
    /// would only duplicate the visible label). Unknown codes keep the localized generic hint.
    /// </summary>
    private string PhonePlaceholderText
    {
        get
        {
            var fieldPlaceholder = SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture);
            if (!string.IsNullOrWhiteSpace(fieldPlaceholder))
            {
                return fieldPlaceholder;
            }

            return ExamplePhoneFormats.TryGetValue(EffectiveCountryCode, out var example)
                ? example
                : Loc["TmSigningStep_PhonePlaceholder"];
        }
    }

    private string OtpPlaceholderText => Loc["TmSigningStep_OtpPlaceholder"];

    /// <summary>The culture-derived default country code (+420 cs, +421 sk, otherwise +1).</summary>
    private static string DefaultCountryFromCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return "+1";
        }

        var language = culture.Split('-')[0].ToLowerInvariant();
        return language switch
        {
            "cs" => "+420",
            "sk" => "+421",
            _ => "+1"
        };
    }

    private async Task HandleCountryChangedAsync(string? value)
    {
        var code = string.IsNullOrWhiteSpace(value) ? EffectiveCountryCode : value;
        // Record the pick BEFORE raising the callbacks: a host that does not bind
        // CountryCodeChanged re-renders the component with the old parameter, and the local state
        // is the only thing keeping the picker on the chosen code (Signing redesign phase 4).
        _countryCode = code;
        await CountryCodeChanged.InvokeAsync(code);
        await NotifyNormalizedPhoneAsync(code);
    }

    private Task HandlePhoneInputAsync(string? value)
    {
        PhoneNumber = value;
        return Task.WhenAll(
            PhoneNumberChanged.InvokeAsync(PhoneNumber),
            NotifyNormalizedPhoneAsync());
    }

    private Task HandleOtpInputAsync(string? value)
    {
        OtpCode = value;
        return OtpCodeChanged.InvokeAsync(OtpCode);
    }

    private async Task SendCodeAsync()
    {
        var normalized = NormalizePhone(EffectiveCountryCode, PhoneNumber);
        if (Field.Required && string.IsNullOrWhiteSpace(normalized))
        {
            _validationMessage = SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_Required"]);
            return;
        }

        _validationMessage = null;
        _codeSent = true;
        await ValueChanged.InvokeAsync(normalized);
        await OnSendCode.InvokeAsync(normalized ?? string.Empty);
    }

    private Task NotifyNormalizedPhoneAsync(string? code = null)
    {
        return ValueChanged.InvokeAsync(NormalizePhone(code ?? EffectiveCountryCode, PhoneNumber));
    }

    private static string? NormalizePhone(string countryCode, string? phoneNumber)
    {
        var digits = new string((phoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return null;
        }

        return $"{countryCode}{digits}";
    }

    /// <summary>Country calling code option for phone signing steps.</summary>
    public sealed record PhoneCountryOption(string Code, string Label);
}
