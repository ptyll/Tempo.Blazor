using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Renders a phone verification signing step with country code, phone input, and OTP state.</summary>
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

    private bool _codeSent;
    private string? _validationMessage;

    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Phone };

    /// <summary>Selected country calling code.</summary>
    [Parameter] public string CountryCode { get; set; } = "+1";

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

    private string ShellClass => string.Join(" ", new[] { "tm-signing-phone-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

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

    private static string BoolText(bool value) => value ? "true" : "false";

    private string PhonePlaceholderText => SigningLocalizationResolver.ResolveFieldPlaceholder(Field, Culture, FallbackCulture, Loc["TmSigningStep_PhonePlaceholder"]);

    private string OtpPlaceholderText => Loc["TmSigningStep_OtpPlaceholder"];

    private async Task HandleCountryChangedAsync(ChangeEventArgs args)
    {
        CountryCode = args.Value?.ToString() ?? CountryCode;
        await CountryCodeChanged.InvokeAsync(CountryCode);
        await NotifyNormalizedPhoneAsync();
    }

    private async Task HandlePhoneChangedAsync(ChangeEventArgs args)
    {
        await HandlePhoneInputAsync(args.Value?.ToString());
    }

    private async Task HandlePhoneInputAsync(string? value)
    {
        PhoneNumber = value;
        await PhoneNumberChanged.InvokeAsync(PhoneNumber);
        await NotifyNormalizedPhoneAsync();
    }

    private Task HandleOtpChangedAsync(ChangeEventArgs args)
    {
        return HandleOtpInputAsync(args.Value?.ToString());
    }

    private Task HandleOtpInputAsync(string? value)
    {
        OtpCode = value;
        return OtpCodeChanged.InvokeAsync(OtpCode);
    }

    private async Task SendCodeAsync()
    {
        var normalized = NormalizePhone(CountryCode, PhoneNumber);
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

    private Task NotifyNormalizedPhoneAsync()
    {
        return ValueChanged.InvokeAsync(NormalizePhone(CountryCode, PhoneNumber));
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
