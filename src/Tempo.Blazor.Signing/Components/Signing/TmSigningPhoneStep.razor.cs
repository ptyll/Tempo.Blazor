using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Renders a phone verification signing step with country code, phone input, OTP state and a
/// functional resend countdown on the core <see cref="TmSelect"/> / <see cref="TmTextInput"/>
/// components (Signing redesign phase 4, SI1; countdown and verification in phase 6, S2-5). Every
/// control carries a visible localized label; the OTP input carries
/// <c>inputmode="numeric"</c> + <c>autocomplete="one-time-code"</c>.
/// </summary>
/// <remarks>
/// <para>
/// SECURITY NOTE (phase 6 contract): raising <see cref="OnSendCode"/> and <see cref="OnVerifyCode"/>
/// only NOTIFIES the host — this component never dispatches an SMS and never decides whether a
/// code is valid. Inside <see cref="TmSigningFormRunner"/> the host verifies through the runner's
/// <c>OnVerifyPhoneCode</c> hook, and the step renders the confirmed state through
/// <see cref="Verified"/>; a standalone host must wire both itself. The typed OTP is NEVER stored
/// as the field value — the normalized phone number is, so a leaked code cannot complete the step.
/// </para>
/// <para>
/// VERIFIED STATE (phase 6 review): the confirmed state LOCKS the number input and the country
/// select and replaces "Send code" with a Secondary "Change number" action. Unlocking is
/// host-mediated: the button raises <see cref="OnChangeNumber"/> (the runner clears its
/// confirmation), so the host-owned state never flips without the host knowing.
/// </para>
/// <para>
/// HOST-REPORTED SEND (phase 6 review fix): inside <see cref="TmSigningFormRunner"/> (which
/// cascades <c>TmSigningRunnerHosted</c>) the send click NEVER fakes progress — the OTP row, the
/// countdown and the "Code sent" announcement follow ONLY the host-reported
/// <see cref="CodeSent"/> flip: the runner clears the sent state while its dispatch runs, so a
/// successful send/resend flips it back to true (announcement, focus, fresh window) and a FAILED
/// one leaves the plain phone form with the runner's alert. A STANDALONE step keeps the local
/// behaviour: the send callback returning without throwing IS the success signal.
/// </para>
/// <para>
/// LIVE REGION (WCAG 2.4.3/4.1.3): one ALWAYS-RENDERED visually-hidden <c>role="status"</c>
/// region announces the phase events ("Code sent", "Verified", "Resend available"); the ticking
/// countdown text itself carries <c>aria-live="off"</c> so it never re-announces every second.
/// </para>
/// <para>
/// DISPOSAL: the component is <see cref="IAsyncDisposable"/> because the Blazor renderer prefers
/// the async interface when both are present (the runner documents the same decision) — the
/// countdown <see cref="ITimer"/> is released there and the tick is guarded by a
/// <c>_disposed</c> flag so a timer racing the dispose never touches a dead renderer.
/// </para>
/// </remarks>
public partial class TmSigningPhoneStep : IAsyncDisposable
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
    private bool _sendInFlight;
    private bool _verifyInFlight;
    private bool _disposed;
    private bool _numberUnlocked;
    private bool _lastVerified;

    // The runner-hosted send (phase 6 review fix): the host-reported CodeSent FLIP — not the send
    // click — starts the countdown, the announcement and the OTP focus. null = "not seen yet" (the
    // mount baseline: a step that mounts with CodeSent already true — the runner keeps the state
    // across navigation — resumes the countdown without re-announcing a send that happened
    // before the navigation and without stealing the runner's step-change focus).
    private bool? _lastHostCodeSent;

    // The polite announcement (phase 6 review, WCAG 4.1.3): EVENT text written into the
    // always-rendered live region — never the ticking countdown itself.
    private string? _announcement;

    // The OTP input focus moves (phase 6 review, WCAG 2.4.3): after a send/resend the input
    // becomes (or already is) the next thing to type into. A pending flag defers the move to
    // OnAfterRenderAsync when the input only exists AFTER the send's re-render.
    private TmTextInput? _otpInput;
    private bool _pendingOtpFocus;

    // Functional resend countdown (Signing redesign phase 6, S2-5): the clock is injectable so a
    // test advances a FakeTimeProvider instead of sleeping — the same seam the runner's autosave
    // debounce uses. Resolved lazily: hosts that never called AddTempoBlazorSigning keep working
    // off TimeProvider.System. -1 means "no send happened on this instance yet".
    private TimeProvider? _clock;
    private ITimer? _countdownTimer;
    private int _secondsRemaining = -1;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private TimeProvider Clock => _clock ??= Services.GetService<TimeProvider>() ?? TimeProvider.System;

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

    /// <summary>
    /// Whether the CURRENT validation message targets the OTP input (phase 6 review — error
    /// ownership): the input carries <c>aria-invalid</c> + the error state and references the
    /// validation id, while the phone input stays clean.
    /// </summary>
    [Parameter] public bool OtpInvalid { get; set; }

    /// <summary>Seconds shown in the resend countdown state.</summary>
    [Parameter] public int ResendSeconds { get; set; } = 30;

    /// <summary>Available country code options.</summary>
    [Parameter] public IReadOnlyList<PhoneCountryOption> CountryOptions { get; set; } = DefaultCountryOptions;

    /// <summary>Callback invoked when a verification code should be sent.</summary>
    [Parameter] public EventCallback<string> OnSendCode { get; set; }

    /// <summary>
    /// Whether the host CONFIRMED the typed code (Signing redesign phase 6). The state is
    /// host-owned — inside the runner it flips only after the host verifier answered
    /// <c>true</c> — and replaces the OTP input, the Verify action and the resend affordances
    /// with the localized verified status.
    /// </summary>
    [Parameter] public bool Verified { get; set; }

    /// <summary>
    /// Raised when the signer asks to verify the typed code (Signing redesign phase 6). The step
    /// itself never decides validity — the RUNNER checks it against the host verifier; a
    /// standalone host binds its own check here and mirrors the result back through
    /// <see cref="Verified"/>.
    /// </summary>
    [Parameter] public EventCallback OnVerifyCode { get; set; }

    /// <summary>
    /// Raised when the signer asks to change the verified number (phase 6 review): the runner
    /// clears its confirmation so <see cref="Verified"/> flips back to <c>false</c> and the
    /// number input unlocks. A standalone host mirrors the same clear through its own state.
    /// </summary>
    [Parameter] public EventCallback OnChangeNumber { get; set; }

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

    /// <summary>
    /// Whether <see cref="TmSigningFormRunner"/> hosts this step (cascaded). Hosted, the send
    /// click NEVER fakes progress: the OTP row, the countdown and the announcement follow ONLY the
    /// host-reported <see cref="CodeSent"/> flip, so a FAILED host send leaves the plain phone
    /// form with the runner's alert. Standalone (no cascade), the local send success keeps the
    /// documented behaviour.
    /// </summary>
    [CascadingParameter(Name = "TmSigningRunnerHosted")]
    private bool IsRunnerHosted { get; set; }

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

        // The runner-hosted contract (phase 6 review fix): the send click NEVER fakes progress —
        // the host-reported CodeSent flip is the only signal that announces "Code sent", starts
        // the countdown and moves the OTP focus. A flip back to false (the runner clears the sent
        // state while its dispatch runs, so a FAILED send/resend lands here, as does Change
        // number) removes exactly those affordances — no stale countdown, no row.
        if (IsRunnerHosted)
        {
            if (_lastHostCodeSent is null)
            {
                _lastHostCodeSent = CodeSent;
            }
            else if (CodeSent != _lastHostCodeSent.Value)
            {
                _lastHostCodeSent = CodeSent;
                if (CodeSent)
                {
                    _announcement = Loc["TmSigningStep_CodeSent"];
                    _pendingOtpFocus = true;
                    StartCountdown();
                }
                else
                {
                    StopCountdown();
                    _secondsRemaining = -1;
                }
            }
        }

        // A host-driven sent state WITHOUT a local send (a mount that arrives with CodeSent
        // already true — the runner keeps the state across navigation — or a standalone
        // host-driven step) seeds the ticking countdown with the initial window, so a RESUMED
        // countdown keeps running instead of freezing at the seeded value.
        if ((_codeSent || CodeSent) && _secondsRemaining < 0 && ResendSeconds > 0)
        {
            StartCountdown();
        }

        // The verified state is host-owned: when the host flips it back to false (the runner
        // cleared the confirmation through Change number, or a new code was sent), the local
        // unlock flag resets so the state purely mirrors the host again.
        if (!Verified)
        {
            _numberUnlocked = false;
        }

        // The live region announces the HOST confirmation event (the transition, not the state —
        // re-rendering the same text would not announce anything, a flip re-renders it once).
        if (Verified && !_lastVerified)
        {
            _announcement = Loc["TmSigningStep_Verified"];
        }

        // The countdown stops on a STATE TRANSITION only — once the host confirms (the OTP row is
        // replaced) or once the confirmation is cleared (Change number). An unconditional stop
        // would kill a live countdown on every ordinary re-render: the runner re-renders after
        // each keystroke while the step is still unverified.
        if (_lastVerified != Verified)
        {
            StopCountdown();
        }

        _lastVerified = Verified;
    }

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    /// <summary>
    /// The effective verified state: the host's <see cref="Verified"/> unless the signer asked to
    /// change the number (the local unlock until the host's re-render arrives).
    /// </summary>
    private bool IsVerifiedState => Verified && !_numberUnlocked;

    /// <summary>Whether a code has been sent (locally or through the host-driven <see cref="CodeSent"/>).</summary>
    private bool HasSentCode => _codeSent || CodeSent;

    /// <summary>Whether the send action is throttled by a running countdown (phase 6 review, item 9).</summary>
    private bool IsCountingDown => HasSentCode && CountdownValue > 0;

    /// <summary>
    /// The invalid state the PHONE input mirrors: the phone input stays CLEAN while the error
    /// belongs to the OTP input (<see cref="OtpInvalid"/>, phase 6 review item 8).
    /// </summary>
    private bool? PhoneValidationState => HasValidationMessage && !OtpInvalid ? false : null;

    /// <summary>The invalid state the OTP input mirrors (aria-invalid + error class) — OTP errors only.</summary>
    private bool? OtpValidationState => OtpInvalid ? false : null;

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
    /// rendered DOM (Signing redesign phase 2). While the error targets the OTP input the phone
    /// input drops the validation reference (item 8).
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

            if (HasValidationMessage && !OtpInvalid)
            {
                parts.Add(_validationId);
            }

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    /// <summary>The ids the OTP input references: the validation id while an OTP error shows.</summary>
    private string? OtpDescribedBy => OtpInvalid ? _validationId : null;

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
    private Dictionary<string, object> BuildOtpAttributes()
    {
        var attributes = new Dictionary<string, object>
        {
            ["inputmode"] = "numeric",
            ["autocomplete"] = "one-time-code"
        };
        if (OtpDescribedBy is { } describedBy)
        {
            attributes["aria-describedby"] = describedBy;
        }

        return attributes;
    }

    /// <summary>Countdown text must never re-announce every tick (WCAG 4.1.3 — the live region owns the events).</summary>
    private static Dictionary<string, object> BuildCountdownAttributes() => new()
    {
        ["aria-live"] = "off"
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
        // OTP error ownership (phase 6 review item 8): a NEW draft invalidates the old error —
        // the message and the OTP invalid state clear as soon as the signer types again.
        if (!string.IsNullOrEmpty(value) && HasValidationMessage && OtpInvalid)
        {
            _validationMessage = null;
        }

        return OtpCodeChanged.InvokeAsync(OtpCode);
    }

    /// <summary>The Change-number action of the VERIFIED state (phase 6 review item 1): unlocks the
    /// number editing locally, stops the stale countdown and asks the HOST to clear its
    /// confirmation — the send state resets so the new number starts a fresh flow.</summary>
    private async Task HandleChangeNumberAsync()
    {
        _numberUnlocked = true;
        _codeSent = false;
        _secondsRemaining = -1;
        StopCountdown();
        await OnChangeNumber.InvokeAsync();
    }

    private async Task SendCodeAsync()
    {
        if (_sendInFlight || Disabled || IsVerifiedState)
        {
            return;
        }

        var normalized = NormalizePhone(EffectiveCountryCode, PhoneNumber);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            // A code cannot be sent to nothing (phase 6 review item 5): the required message
            // explains the empty field; a non-required empty field just stays quiet.
            if (Field.Required)
            {
                _validationMessage = SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_Required"]);
            }

            return;
        }

        _validationMessage = null;
        _sendInFlight = true;
        try
        {
            await ValueChanged.InvokeAsync(normalized);
            await OnSendCode.InvokeAsync(normalized);
            // The countdown (and the OTP row) start only after the host accepted the send (phase 6
            // review item 5) — a failing host must not fake progress. RUNNER-HOSTED, the host
            // reports the success through the CodeSent flip (OnParametersSet) and the local state
            // stays untouched, so a failing host send shows NO row at all; STANDALONE, the send
            // callback returning (without throwing) IS the success signal.
            if (!IsRunnerHosted)
            {
                _codeSent = true;
                _pendingOtpFocus = true;
                _announcement = Loc["TmSigningStep_CodeSent"];
                StartCountdown();
            }
        }
        catch (OperationCanceledException)
        {
            // The signer (or the host) cancelled the dispatch: no progress is faked.
        }
        catch (Exception)
        {
            _validationMessage = Loc["TmSigningStep_VerificationUnavailable"];
        }
        finally
        {
            _sendInFlight = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>The resend action at the end of the countdown: the same validation, the same
    /// send, a fresh window — and the OTP input regains focus (phase 6 review item 7; the
    /// pending focus flag applies it after the resend's render).</summary>
    private Task ResendCodeAsync() => SendCodeAsync();

    private async Task VerifyCodeAsync()
    {
        if (_verifyInFlight || Disabled)
        {
            return;
        }

        if (Field.Required && string.IsNullOrWhiteSpace(OtpCode))
        {
            _validationMessage = SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_Required"]);
            return;
        }

        // The step never decides validity — raising the callback hands the code to the host
        // (the runner's OnVerifyPhoneCode). A "false" answer arrives as the runner's validation
        // message through the step shell; a THROWING host shows the localized unavailable
        // message and never flips any state (phase 6 review item 5).
        _validationMessage = null;
        _verifyInFlight = true;
        try
        {
            await OnVerifyCode.InvokeAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the host: no state change, no fake progress.
        }
        catch (Exception)
        {
            _validationMessage = Loc["TmSigningStep_VerificationUnavailable"];
        }
        finally
        {
            _verifyInFlight = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// The visible countdown: a locally started countdown's remaining seconds, otherwise the
    /// initial window (the host-driven <see cref="CodeSent"/> state without a local send).
    /// </summary>
    private int CountdownValue => _secondsRemaining >= 0 ? _secondsRemaining : ResendSeconds;

    /// <summary>At zero (or for a zero/negative window) the countdown text turns into the real resend action.</summary>
    private bool ShowResendButton => CountdownValue <= 0;

    private void StartCountdown()
    {
        _secondsRemaining = Math.Max(0, ResendSeconds);
        _countdownTimer?.Dispose();
        _countdownTimer = Clock.CreateTimer(_ => _ = InvokeAsync(CountdownTickAsync), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private Task CountdownTickAsync()
    {
        // A timer racing the dispose must never touch a dead renderer (phase 6 review item 11 —
        // the old nested InvokeAsync left an unobserved faulting task behind on teardown).
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        if (_secondsRemaining > 0)
        {
            _secondsRemaining--;
            if (_secondsRemaining <= 0)
            {
                StopCountdown();
                _announcement = Loc["TmSigningStep_ResendAvailable"];
            }
        }
        else
        {
            StopCountdown();
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    private void StopCountdown()
    {
        _countdownTimer?.Dispose();
        _countdownTimer = null;
    }

    /// <summary>
    /// After a send/resend the OTP input (when rendered) takes the focus (WCAG 2.4.3) — WITHOUT
    /// <c>preventScroll</c>: the browser's native focus scrolling brings the code row into view
    /// in EVERY scrollable ancestor (the runner's capped panel and the 390 bottom sheet's internal
    /// scroll included), so the row the focus moves to is actually visible. The old
    /// <c>preventScroll: true</c> left the sheet parked on the phone row with the code input
    /// focused below the fold.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_pendingOtpFocus)
        {
            return;
        }

        if (_otpInput is null || IsVerifiedState)
        {
            return;
        }

        // The runner-hosted flip arrives while the send is STILL in flight (the step renders the
        // input disabled then) — a browser ignores focus on a disabled element, so the move is
        // deferred to the render that re-enables the input instead of being silently dropped.
        if (_sendInFlight || _verifyInFlight || Disabled)
        {
            return;
        }

        _pendingOtpFocus = false;
        await _otpInput.FocusAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // The ticking timer must not outlive the component: the flag goes up BEFORE the timer is
        // released, so a tick already scheduled on the thread pool finds the component gone and
        // becomes a no-op instead of faulting a task into a disposed renderer.
        _disposed = true;
        StopCountdown();
        return ValueTask.CompletedTask;
    }

    private Task NotifyNormalizedPhoneAsync(string? code = null)
    {
        return ValueChanged.InvokeAsync(NormalizePhone(code ?? EffectiveCountryCode, PhoneNumber));
    }

    private static string? NormalizePhone(string countryCode, string? phoneNumber)
    {
        var raw = phoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // The runner feeds the stored (already normalized) value back through PhoneNumber, so a
        // re-normalization must never DOUBLE the calling code ("+420" + "420777…" dialled a
        // wrong number on every runner-hosted send). A number that already carries the SELECTED
        // calling code keeps the digits behind it verbatim; anything else is digit-stripped and
        // prefixed as before.
        var localDigits = raw.StartsWith(countryCode, StringComparison.Ordinal)
            ? new string(raw[countryCode.Length..].Where(char.IsDigit).ToArray())
            : new string(raw.Where(char.IsDigit).ToArray());

        return localDigits.Length == 0 ? null : $"{countryCode}{localDigits}";
    }

    /// <summary>Country calling code option for phone signing steps.</summary>
    public sealed record PhoneCountryOption(string Code, string Label);
}
