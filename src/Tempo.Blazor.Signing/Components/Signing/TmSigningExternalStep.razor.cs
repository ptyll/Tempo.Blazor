using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Provider-agnostic placeholder step for verification, KBA, and payment flows. Phase 4 (SI1)
/// gives it the reference presentation: a hero icon in a primary-subtle circle, a per-type default
/// description (overridable by the field description), the payment amount formatted with the FIELD
/// culture, Start as the primary action with the open link as an outline sibling, and the
/// verified / paid state once the value is <c>true</c>. Phase 6 (SI2) adds the host context:
/// <see cref="ProviderName"/> names the provider and the redirect note accompanies the outbound
/// link.
/// </summary>
/// <remarks>
/// <para>
/// SECURITY NOTE (phase 6 contract — was a known limitation through phase 5): the step only
/// RENDERS the external flow. What happens after <see cref="OnStart"/> / <see cref="OnCheckout"/>
/// is the HOST's contract: inside <see cref="TmSigningFormRunner"/> with
/// <c>OnExternalStepRequested</c> wired the runner stores nothing — the step completes solely
/// after the host stores the provider result (value <c>true</c>) through <c>Values</c>. Without
/// that callback the runner keeps the legacy behaviour of storing <c>true</c> on the click, which
/// is presentation state and NOT evidence that a verification or payment happened. The verified /
/// paid state reflects the stored value, so a host that treats stored values as trusted evidence
/// opens itself to spoofing — always wire the callback.
/// </para>
/// <para>
/// <see cref="ExternalUrl"/> merely opens a link in a new tab with <c>rel="noopener noreferrer"</c>
/// — the provider never receives the signing page's window context.
/// </para>
/// </remarks>
public partial class TmSigningExternalStep
{
    private readonly Dictionary<string, string?> _answers = [];

    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private readonly string _redirectNoteId = $"tm-signing-external-redirect-{Guid.NewGuid():N}";

    // The polite announcement (phase 6 review, WCAG 4.1.3): EVENT text written into the
    // always-rendered live region — "Loading…" while the provider runs, "Verified" / "Paid"
    // once the host confirms.
    private string? _announcement;
    private bool _lastIsLoading;
    private bool _lastVerified;

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Verification };

    /// <summary>
    /// Current field value. When it carries <c>true</c> (a bool or the string "true" — the shape
    /// the runner stores), the step shows the verified / paid state instead of the call to action.
    /// </summary>
    [Parameter] public object? Value { get; set; }

    /// <summary>Whether the external provider operation is loading.</summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>Error message shown for the external provider operation.</summary>
    [Parameter] public string? Error { get; set; }

    /// <summary>External URL for provider-hosted verification or checkout.</summary>
    [Parameter] public string? ExternalUrl { get; set; }

    /// <summary>
    /// Display name of the verification / payment provider (Signing redesign phase 6) — the
    /// runner resolves it from the host's <c>StepContextProvider</c> and renders the localized
    /// provider line above the call to action.
    /// </summary>
    [Parameter] public string? ProviderName { get; set; }

    /// <summary>KBA questions to answer inline.</summary>
    [Parameter] public IReadOnlyList<string> Questions { get; set; } = [];

    /// <summary>Callback invoked when KBA answers change.</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, string?>> AnswersChanged { get; set; }

    /// <summary>Callback invoked when verification or KBA should start.</summary>
    [Parameter] public EventCallback<SigningField> OnStart { get; set; }

    /// <summary>Callback invoked when payment checkout should start.</summary>
    [Parameter] public EventCallback<SigningField> OnCheckout { get; set; }

    /// <summary>
    /// Callback invoked when the signer SUBMITS the inline KBA answers (phase 6 review item 4).
    /// The request carries the field and the typed answers — the ANSWERS ARE NEVER STORED as the
    /// field value; the host's stored provider result is the only completion evidence.
    /// </summary>
    [Parameter] public EventCallback<SigningExternalStepRequest> OnSubmitAnswers { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether the controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Culture used to resolve localized field text and to format the payment amount.</summary>
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

    private string ShellClass => string.Join(" ", new[] { "tm-signing-external-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private bool HasDescription => !string.IsNullOrWhiteSpace(ResolvedDescription);

    /// <summary>The message the shell renders as the step's single alert (own error first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(Error)
        ? Error
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>The invalid state the KBA answer inputs mirror (aria-invalid + error class); the shell owns the alert.</summary>
    private bool? ValidationState => HasValidationMessage ? false : null;

    /// <summary>The ids the KBA answer inputs reference (description + validation while shown).</summary>
    private string? DescribedBy
    {
        get
        {
            var parts = new List<string>(2);
            if (HasFieldDescription)
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

    private bool HasFieldDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    private Dictionary<string, object> BuildAnswerAttributes()
    {
        var attributes = new Dictionary<string, object>();
        if (DescribedBy is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        return attributes;
    }

    /// <summary>Whether the stored value says the external action completed (bool or string "true").</summary>
    private bool IsVerified => Value is true
        || string.Equals(Value as string, bool.TrueString, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The description the shell renders — the field description when the template carries one,
    /// otherwise the localized per-type default (verification / KBA / payment).
    /// </summary>
    private string? ResolvedDescription
    {
        get
        {
            if (HasFieldDescription)
            {
                // null lets the shell resolve the field description itself (with its markdown).
                return null;
            }

            return Field.Type switch
            {
                SigningFieldType.Verification => Loc["TmSigningStep_VerificationDescription"],
                SigningFieldType.Kba => Loc["TmSigningStep_KbaDescription"],
                SigningFieldType.Payment => Loc["TmSigningStep_PaymentDescription"],
                _ => null
            };
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The live region announces the EVENTS (transitions), not the state: "Loading…" when the
        // provider starts, "Verified" / "Paid" once the host confirms (phase 6 review item 7).
        if (IsLoading && !_lastIsLoading)
        {
            _announcement = Loc["TmSigningStep_LoadingExternal"];
        }

        if (IsVerified && !_lastVerified)
        {
            _announcement = VerifiedText;
        }

        _lastIsLoading = IsLoading;
        _lastVerified = IsVerified;
    }

    /// <summary>The verified / paid announcement and card text — "Verified by {provider}" when the
    /// provider names itself (phase 6 review item 14), otherwise the plain state.</summary>
    private string VerifiedText
    {
        get
        {
            if (Field.Type == SigningFieldType.Payment)
            {
                return Loc["TmSigningStep_Paid"];
            }

            return !string.IsNullOrWhiteSpace(ProviderName)
                ? Loc["TmSigningStep_VerifiedBy", ProviderName]
                : Loc["TmSigningStep_Verified"];
        }
    }

    /// <summary>Whether the primary action of the current branch is busy (the provider operation) —
    /// it stays IN PLACE, disabled and aria-busy (phase 6 review item 7).</summary>
    private Dictionary<string, object>? BusyAttributes => IsLoading
        ? new Dictionary<string, object> { ["aria-busy"] = "true" }
        : null;

    /// <summary>
    /// The payment summary from the model data: the amount formatted with the FIELD culture and
    /// the model's currency string. A multi-character currency (ISO code or "Kč") prints after the
    /// amount with a space in every culture; a single-character symbol keeps the culture's native
    /// position ("$1,500.00"). The old rendering was culture-invariant ("EUR 19.50" everywhere).
    /// </summary>
    private string PaymentAmount
    {
        get
        {
            var currency = string.IsNullOrWhiteSpace(Field.Preferences.Currency) ? "USD" : Field.Preferences.Currency;
            var price = Field.Preferences.Price ?? 0m;

            var format = (NumberFormatInfo)StepCulture.NumberFormat.Clone();
            if (currency.Length > 1)
            {
                format.CurrencySymbol = currency;
                format.CurrencyPositivePattern = 3; // n $
                format.CurrencyNegativePattern = 8; // -n $
            }
            else
            {
                format.CurrencySymbol = currency;
            }

            return price.ToString("C", format);
        }
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

    private Task StartAsync() => OnStart.InvokeAsync(Field);

    private Task CheckoutAsync() => OnCheckout.InvokeAsync(Field);

    /// <summary>Hands the typed KBA answers to the host through <see cref="OnSubmitAnswers"/> —
    /// a snapshot, so later edits cannot mutate the request the host already received. The
    /// answers never become the field value.</summary>
    private async Task SubmitAnswersAsync()
    {
        var answers = new Dictionary<string, string?>(_answers, StringComparer.Ordinal);
        await OnSubmitAnswers.InvokeAsync(new SigningExternalStepRequest(Field, answers));
    }

    private string? GetAnswer(string question)
    {
        return _answers.TryGetValue(question, out var answer) ? answer : null;
    }

    private async Task HandleAnswerChangedAsync(string question, string? value)
    {
        _answers[question] = value;
        await AnswersChanged.InvokeAsync(_answers);
    }
}
