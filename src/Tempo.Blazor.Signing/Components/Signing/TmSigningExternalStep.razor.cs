using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Provider-agnostic placeholder step for verification, KBA, and payment flows.</summary>
/// <remarks>
/// <para>
/// KNOWN LIMITATION (security note — reworked in Signing redesign phase 6): the step only RENDERS
/// the external flow. Raising <see cref="OnStart"/> or <see cref="OnCheckout"/> marks the field
/// complete; the runner's default handlers store <c>true</c> without contacting any provider, so
/// "Start verification" / "Checkout" completing the step is NOT evidence that a verification or a
/// payment happened. <see cref="ExternalUrl"/> merely opens a link in a new tab. Hosts must treat
/// the resulting field value as untrusted presentation state until a provider-backed contract
/// replaces this placeholder.
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

    /// <summary>Signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Verification };

    /// <summary>Whether the external provider operation is loading.</summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>Error message shown for the external provider operation.</summary>
    [Parameter] public string? Error { get; set; }

    /// <summary>External URL for provider-hosted verification or checkout.</summary>
    [Parameter] public string? ExternalUrl { get; set; }

    /// <summary>KBA questions to answer inline.</summary>
    [Parameter] public IReadOnlyList<string> Questions { get; set; } = [];

    /// <summary>Callback invoked when KBA answers change.</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, string?>> AnswersChanged { get; set; }

    /// <summary>Callback invoked when verification or KBA should start.</summary>
    [Parameter] public EventCallback<SigningField> OnStart { get; set; }

    /// <summary>Callback invoked when payment checkout should start.</summary>
    [Parameter] public EventCallback<SigningField> OnCheckout { get; set; }

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

    private string ShellClass => string.Join(" ", new[] { "tm-signing-external-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own error first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(Error)
        ? Error
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>
    /// The ids the step's inputs reference — per-instance, and the SAME instances the shell
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

            if (HasValidationMessage)
            {
                parts.Add(_validationId);
            }

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private string PaymentAmount
    {
        get
        {
            var currency = string.IsNullOrWhiteSpace(Field.Preferences.Currency) ? "USD" : Field.Preferences.Currency;
            var price = Field.Preferences.Price ?? 0m;
            return string.Create(CultureInfo.InvariantCulture, $"{currency} {price:0.00}");
        }
    }

    private Task StartAsync() => OnStart.InvokeAsync(Field);

    private Task CheckoutAsync() => OnCheckout.InvokeAsync(Field);

    private string? GetAnswer(string question)
    {
        return _answers.TryGetValue(question, out var answer) ? answer : null;
    }

    private async Task HandleAnswerChangedAsync(string question, ChangeEventArgs args)
    {
        _answers[question] = args.Value?.ToString();
        await AnswersChanged.InvokeAsync(_answers);
    }
}
