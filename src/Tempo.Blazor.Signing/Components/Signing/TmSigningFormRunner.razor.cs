using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Coordinates a linear signing ceremony over document pages and signing field steps.</summary>
/// <remarks>
/// <para>
/// Signing redesign phase 3: the panel is a guided wizard — a titled header with a completed-field
/// progress bar (the bar itself is decorative, the text announces), the PERMANENT field list (SI4;
/// the old "Accessibility mode" toggle was removed), a polite success status once every required
/// field is done, and a sticky contextual action footer (Previous / Next → Complete, Skip only for
/// an optional non-final field). <see cref="DocumentTitle"/>, <see cref="ShowDocumentToolbar"/>
/// (zoom controls; pagination comes later) and <see cref="DocumentHeaderActions"/> add the U4
/// document chrome; the default stays today's chrome-free look.
/// </para>
/// <para>
/// Signing redesign phase 5: the runner classifies its OWN container (F1 — see
/// <c>TmSigningFormRunner.Layout.razor.cs</c>) so a narrow container presents the mobile sheet with
/// radio-card choice fields (SI1) and resolves the <see cref="DefaultSignatureMode"/>; the active
/// field is scrolled into view above the sheet by the package's <c>signing-runner.js</c> module
/// after every landing render.
/// </para>
/// <para>
/// KNOWN LIMITATION (security note — reworked in Signing redesign phase 6): the external steps are
/// placeholders. Clicking "Start verification" / "Checkout" (the runner's
/// <c>OnStart</c>/<c>OnCheckout</c> handlers store <c>true</c> for the field) marks the step
/// COMPLETE WITHOUT ANY VERIFICATION actually taking place, and the phone step's OTP and the
/// attachment step's upload are equally cosmetic in this package — no SMS is sent and no file
/// leaves the browser. A host MUST wire its own provider-backed flow (or treat the field value as
/// untrusted) until that phase lands; completion of such a step is presentation state, never proof
/// that a verification, payment, or upload happened.
/// </para>
/// </remarks>
public partial class TmSigningFormRunner : IAsyncDisposable
{
    private Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, object?>? _lastValues;
    private int _currentStepIndex;
    private string? _validationMessage;
    private string? _completionMessage;
    private string? _autoSaveError;
    private string _autoSaveState = "idle";
    private double _documentScale = 1.0;
    private readonly string _stepPanelId = $"tm-signing-runner-panel-{Guid.NewGuid():N}";
    private readonly TmSigningStepFocusSignal _stepFocusSignal = new();
    private readonly Dictionary<string, TmSignatureCaptureMode> _signatureCaptureModes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _autoSaveCts;

    // Plan cache (Signing redesign phase 2): the plan is a pure function of Fields / Pages /
    // values / SubmitterUuid, and it used to be recomputed on EVERY access — several times per
    // render. The cache keys track the inputs; in-place value mutations bump _valuesVersion
    // because the dictionary instance itself stays the same.
    private SigningStepPlan? _planCache;
    private IReadOnlyList<SigningField>? _planFields;
    private IReadOnlyList<SigningDocumentPage>? _planPages;
    private IReadOnlyDictionary<string, object?>? _planValues;
    private string? _planSubmitterUuid;
    private int _planValuesVersion;
    private int _valuesVersion;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private TimeProvider? _clock;

    // signing-runner.js interop (phase 5) and the layout classification it serves live in the
    // TmSigningFormRunner.Layout.razor.cs partial (phase 5 review split) — the runner only disposes the
    // interop from here.

    // The autosave debounce runs on an injectable clock so tests advance a FakeTimeProvider instead
    // of sleeping. Resolved lazily (not [Inject]-ed directly): an unregistered optional service
    // fails property injection outright, and hosts that never called AddTempoBlazorSigning must
    // keep working off TimeProvider.System.
    private TimeProvider Clock => _clock ??= Services.GetService<TimeProvider>() ?? TimeProvider.System;

    /// <summary>Document pages shown during signing.</summary>
    [Parameter] public IReadOnlyList<SigningDocumentPage> Pages { get; set; } = [];

    /// <summary>
    /// Optional document title shown above the pages (Signing redesign phase 3, U4). Rendered only
    /// when set (or when <see cref="DocumentHeaderActions"/> has content), so the default look
    /// stays today's chrome-free document area.
    /// </summary>
    [Parameter] public string? DocumentTitle { get; set; }

    /// <summary>
    /// Shows the document toolbar's ZOOM controls on every page viewer (Signing redesign phase 3,
    /// U4). The zoom level is SHARED across all pages through the runner's own scale state.
    /// Pagination controls stay off — the runner presents one page at a time, so per-page
    /// previous/next buttons would be dead chrome (they come with a later phase if a host needs
    /// them). Defaults to <c>false</c> — today's chrome-free look.
    /// </summary>
    [Parameter] public bool ShowDocumentToolbar { get; set; }

    /// <summary>
    /// Host actions rendered in the document header row next to <see cref="DocumentTitle"/>
    /// (Signing redesign phase 3, U4) — for example a language or metadata control the host owns.
    /// </summary>
    [Parameter] public RenderFragment? DocumentHeaderActions { get; set; }

    /// <summary>Signing fields available to the signer.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Current field values keyed by field UUID.</summary>
    [Parameter] public IReadOnlyDictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Callback invoked whenever field values change.</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, object?>> ValuesChanged { get; set; }

    /// <summary>Optional signer role filter.</summary>
    [Parameter] public string? SubmitterUuid { get; set; }

    /// <summary>
    /// Submitter roles whose colours are painted on the field overlays
    /// (<see cref="TmSigningFieldOverlay.RoleColor"/>). The colour resolves from the field's own
    /// submitter, falling back to <see cref="SubmitterUuid"/>; roles without a colour keep the
    /// primary-tint overlay default.
    /// </summary>
    [Parameter] public IReadOnlyList<SigningSubmitterRole> SubmitterRoles { get; set; } = [];

    /// <summary>Callback invoked when the current step is submitted by moving forward.</summary>
    [Parameter] public EventCallback<SigningStepItem> OnStepSubmit { get; set; }

    /// <summary>Callback invoked after debounced autosave.</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, object?>> OnAutoSave { get; set; }

    /// <summary>Callback invoked when all required fields are complete and the signer completes the form.</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, object?>> OnComplete { get; set; }

    /// <summary>Culture used to render the signing layer.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Callback invoked when the signing culture changes.</summary>
    [Parameter] public EventCallback<string?> CultureChanged { get; set; }

    /// <summary>Fallback culture used when field translations are missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Supported cultures offered by the language selector.</summary>
    [Parameter] public IReadOnlyList<string> SupportedCultures { get; set; } = [];

    /// <summary>Whether the runner should show a language selector when multiple cultures are available.</summary>
    [Parameter] public bool ShowLanguageSelector { get; set; }

    /// <summary>Callback invoked with a resolved localization snapshot for audit storage.</summary>
    [Parameter] public EventCallback<SigningSubmissionLocalizationSnapshot> OnLocalizationSnapshotChanged { get; set; }

    /// <summary>Delay before autosave is invoked after a value change.</summary>
    [Parameter] public TimeSpan AutoSaveDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Whether navigation and inputs are currently blocked by an external operation.</summary>
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>Whether the complete action is currently running.</summary>
    [Parameter] public bool IsCompleting { get; set; }

    /// <summary>Initial presentation mode for the mobile bottom panel.</summary>
    [Parameter] public TmSigningFormRunnerMobilePanelMode MobilePanelMode { get; set; } = TmSigningFormRunnerMobilePanelMode.Expanded;

    /// <summary>Optional selector for a fixed complete target that the mobile panel must avoid.</summary>
    [Parameter] public string? MobileCompleteTargetSelector { get; set; }

    /// <summary>
    /// The capture mode a signature step STARTS in when the template's
    /// <c>Preferences.Format</c> is silent (Signing redesign phase 5, SI3). <see cref="TmSigningDefaultSignatureMode.Auto"/>
    /// — the default — resolves to Draw on a coarse pointer (a finger) and to Typed elsewhere;
    /// the template's stored format always wins over this setting.
    /// </summary>
    [Parameter] public TmSigningDefaultSignatureMode DefaultSignatureMode { get; set; } = TmSigningDefaultSignatureMode.Auto;

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>
    /// Observability for the plan cache test (InternalsVisibleTo): counts how often the step plan
    /// was recomputed. While the plan is uncached this increments on every access, so the cache
    /// test has teeth before and after the cache lands.
    /// </summary>
    internal int PlanComputationsForTests { get; private set; }

    private SigningStepPlan Plan
    {
        get
        {
            if (_planCache is null
                || !ReferenceEquals(_planFields, Fields)
                || !ReferenceEquals(_planPages, Pages)
                || !ReferenceEquals(_planValues, _values)
                || _planValuesVersion != _valuesVersion
                || !string.Equals(_planSubmitterUuid, SubmitterUuid, StringComparison.Ordinal))
            {
                _planCache = SigningStepPlanner.Plan(Fields, Pages, _values, SubmitterUuid);
                _planFields = Fields;
                _planPages = Pages;
                _planValues = _values;
                _planValuesVersion = _valuesVersion;
                _planSubmitterUuid = SubmitterUuid;
                PlanComputationsForTests++;
            }

            return _planCache;
        }
    }

    private SigningStepItem? CurrentStep => Plan.Steps.Count == 0
        ? null
        : Plan.Steps[Math.Clamp(CurrentStepIndex, 0, Plan.Steps.Count - 1)];

    private int CurrentStepIndex => Plan.Steps.Count == 0
        ? 0
        : Math.Clamp(_currentStepIndex, 0, Plan.Steps.Count - 1);

    // The layout plumbing (F1), the coarse-pointer probe / signature-mode resolution (SI3) and
    // the pending auto-scroll live in their own partial parts (phase 5 review split):
    // TmSigningFormRunner.Layout.razor.cs. The Previous / Next / Skip navigation is in
    // TmSigningFormRunner.Navigation.cs, the mobile-sheet presentation state in
    // TmSigningFormRunner.MobileSheet.cs; the progress counts are in TmSigningFormRunner.Progress.cs.

    /// <summary>
    /// The runner's SINGLE after-render pass (phase 5 review): resolve the coarse-pointer probe
    /// once (SI3), then apply the pending auto-scroll and the pending sheet focus moves — AFTER
    /// the render has applied the new step / sheet state, so the JS side measures settled
    /// geometry and the focus targets are actually visible.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            await ResolvePointerAsync(firstRender);
            await ApplyPendingScrollAsync();
            await ApplyPendingSheetFocusAsync();
        }
        catch (Exception ex) when (ex is JSException
            or JSDisconnectedException
            or ObjectDisposedException
            or TaskCanceledException
            or InvalidOperationException)
        {
            // The after-render pass is best-effort by contract (see SigningRunnerInterop): a
            // circuit that died mid-render keeps the failure local.
        }
    }

    private bool IsCompleteDisabled => IsLoading || IsCompleting || !AllRequiredFieldsComplete();

    /// <summary>
    /// The primary action is contextual (Signing redesign phase 3): "Next" while later steps remain,
    /// otherwise "Complete" — on the LAST step Complete renders (disabled while required work is
    /// missing) so the signer always sees the finishing action instead of a dead Next.
    /// </summary>
    private bool ShowCompleteButton => AllRequiredFieldsComplete() || CurrentStepIndex >= Plan.Steps.Count - 1;

    private string CurrentCulture => !string.IsNullOrWhiteSpace(Culture)
        ? Culture!
        : CultureInfo.CurrentUICulture.Name;

    private bool ShowLanguagePicker => ShowLanguageSelector && SupportedCultures.Count > 1;

    /// <summary>The document header renders only when it has content (default = chrome-free look).</summary>
    private bool HasDocumentHeader => !string.IsNullOrWhiteSpace(DocumentTitle) || DocumentHeaderActions is not null;

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-form-runner" };
            AddClass(classes, IsLoading, "tm-signing-form-runner--loading");
            AddClass(classes, !string.IsNullOrEmpty(LayoutClass), LayoutClass);
            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    private string AutoSaveStatusText => _autoSaveState switch
    {
        "saving" => Loc["TmSigningFormRunner_Saving"],
        "saved" => Loc["TmSigningFormRunner_Saved"],
        "error" => _autoSaveError ?? Loc["TmSigningFormRunner_SaveError"],
        _ => Loc["TmSigningFormRunner_Unsaved"]
    };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // The plan cache is invalidated on EVERY parameter set: a Fields list can be mutated in
        // place by the host (same list instance, new steps), and only a fresh plan observes that.
        // The _valuesVersion bumps below stay — in-place value mutations keep invalidating the
        // cached plan between parameter sets too.
        _planCache = null;

        if (!ReferenceEquals(Values, _lastValues))
        {
            _values = Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _lastValues = Values;
            _valuesVersion++;
        }

        if (_currentStepIndex >= Plan.Steps.Count)
        {
            _currentStepIndex = Math.Max(0, Plan.Steps.Count - 1);
        }
    }

    private async Task HandleCultureChangedAsync(ChangeEventArgs args)
    {
        var culture = args.Value?.ToString();
        Culture = string.IsNullOrWhiteSpace(culture) ? null : culture;
        ClearValidationMessages();
        await CultureChanged.InvokeAsync(Culture);
        if (OnLocalizationSnapshotChanged.HasDelegate)
        {
            await OnLocalizationSnapshotChanged.InvokeAsync(CreateLocalizationSnapshot());
        }
    }

    private IEnumerable<SigningStepOverlayItem> GetPageOverlays(SigningDocumentPage page)
    {
        return Plan.OverlayFields.Where(item =>
            string.Equals(item.Area.AttachmentUuid, page.AttachmentUuid, StringComparison.Ordinal)
            && item.Area.Page == page.PageIndex);
    }

    /// <summary>The role colour for a field overlay: the field's own role first, then the runner's signer.</summary>
    private string? GetOverlayRoleColor(SigningField field)
    {
        return SigningRolePalette.ResolveRoleColor(SubmitterRoles, field.SubmitterUuid ?? SubmitterUuid);
    }

    /// <summary>
    /// The "click to sign / fill in" call to action for the document overlays (Signing redesign
    /// phase 3): only the CURRENT field prompts, and only while it is an empty REQUIRED field —
    /// every other field (and the completed current one) stays quiet, so the document never
    /// fills with prompts and the labels keep their room. Read-only (formula) overlays never
    /// become steps, so they can never be current.
    /// </summary>
    private string? GetOverlayPrompt(SigningField field)
    {
        return IsCurrentField(field) && field.Required && !IsFieldComplete(field)
            ? Loc["TmSigningFieldOverlay_EmptyRequiredPrompt"]
            : null;
    }

    /// <summary>
    /// Whether a step's fields are actually FILLED (every field of the step has a value). This is
    /// the field list's status — distinct from <see cref="IsStepComplete"/>, which treats an
    /// optional empty step as completable: an empty optional field reads "Optional", not "Done".
    /// </summary>
    private bool IsStepFilled(SigningStepItem step)
    {
        return step.Fields.All(IsFieldComplete);
    }

    private Task HandleDocumentScaleChangedAsync(double scale)
    {
        // One shared zoom level across every page viewer (U4).
        _documentScale = scale;
        return Task.CompletedTask;
    }

    private bool IsCurrentField(SigningField field)
    {
        return CurrentStep?.Fields.Any(item => string.Equals(item.Uuid, field.Uuid, StringComparison.Ordinal)) == true;
    }

    private bool IsInvalid(SigningField field)
    {
        return field.Required && !IsFieldComplete(field);
    }

    private object? GetValue(string fieldUuid)
    {
        return _values.TryGetValue(fieldUuid, out var value) ? value : null;
    }

    private object? GetOverlayValue(SigningField field)
    {
        if (!string.IsNullOrWhiteSpace(field.Preferences.Formula))
        {
            return EvaluateFormula(field.Preferences.Formula);
        }

        return GetValue(field.Uuid);
    }

    private Task SetSignatureCaptureModeAsync(string fieldUuid, TmSignatureCaptureMode mode)
    {
        _signatureCaptureModes[fieldUuid] = mode;
        return Task.CompletedTask;
    }

    private string? GetStringValue(string fieldUuid)
    {
        return FormatValue(GetValue(fieldUuid));
    }

    private decimal? GetDecimalValue(string fieldUuid)
    {
        return decimal.TryParse(FormatValue(GetValue(fieldUuid)), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private IReadOnlyList<TmSigningStepAttachment> GetAttachments(string fieldUuid)
    {
        return GetValue(fieldUuid) as IReadOnlyList<TmSigningStepAttachment> ?? [];
    }

    private async Task HandleOverlayClickAsync(TmSigningFieldOverlayPointerEventArgs args)
    {
        var index = Plan.Steps.FindIndex(step => step.Fields.Any(field => string.Equals(field.Uuid, args.Field.Uuid, StringComparison.Ordinal)));
        if (index >= 0)
        {
            if (index != _currentStepIndex)
            {
                _stepFocusSignal.Version++;
            }

            _currentStepIndex = index;
            ClearValidationMessages();

            // Signing redesign phase 5: clicking a field on the document OPENS the sheet on that
            // field's step, landing in the FULL edit state (the collapsed peek must not swallow
            // the click). On desktop the sheet state classes are inert. The scroll itself is
            // requested, not performed: OnAfterRenderAsync runs it after this render.
            LandMobileSheetOnFull();
            RequestScrollToActiveField();
        }
    }

    private async Task SetChoiceValueAsync(SigningStepItem step, object? value)
    {
        if (step.IsCheckboxGroup && value is IEnumerable<string> selected)
        {
            var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
            foreach (var field in step.Fields)
            {
                _values[field.Uuid] = selectedSet.Contains(field.Uuid);
            }
        }
        else
        {
            _values[step.Field.Uuid] = value;
        }

        _valuesVersion++;
        await PublishValuesAsync();
    }

    private async Task SetFieldValueAsync(string fieldUuid, object? value)
    {
        _values[fieldUuid] = value;
        _valuesVersion++;
        await PublishValuesAsync();
    }

    private async Task PublishValuesAsync()
    {
        ClearValidationMessages();
        var snapshot = SnapshotValues();
        await ValuesChanged.InvokeAsync(snapshot);
        ScheduleAutoSave(snapshot);
    }

    private async Task CompleteAsync()
    {
        if (IsCompleteDisabled)
        {
            var invalidIndex = FindFirstInvalidRequiredStep();
            if (invalidIndex != _currentStepIndex)
            {
                // Landing on a different step is a step navigation: move focus to its title,
                // exactly like a failed Next does.
                _stepFocusSignal.Version++;
            }

            _currentStepIndex = invalidIndex;
            if (CurrentStep is null)
            {
                _completionMessage = Loc["TmSigningFormRunner_CompleteBlocked"];
            }
            else
            {
                // The blocked completion is caused by the step the signer lands on: its message
                // travels through the step shell, exactly like a failed Next.
                _validationMessage = GetRequiredMessage(CurrentStep);
            }

            return;
        }

        try
        {
            if (OnLocalizationSnapshotChanged.HasDelegate)
            {
                await OnLocalizationSnapshotChanged.InvokeAsync(CreateLocalizationSnapshot());
            }

            await OnComplete.InvokeAsync(SnapshotValues());
            ClearValidationMessages();
        }
        catch (Exception ex)
        {
            _completionMessage = ex.Message;
        }
    }

    private void ClearValidationMessages()
    {
        _validationMessage = null;
        _completionMessage = null;
    }

    private bool AllRequiredFieldsComplete()
    {
        return Plan.Steps
            .SelectMany(step => step.Fields)
            .Where(field => field.Required)
            .All(IsFieldComplete);
    }

    private bool IsFieldComplete(SigningField field)
    {
        if (!_values.TryGetValue(field.Uuid, out var value))
        {
            value = field.DefaultValue;
        }

        return field.Type switch
        {
            SigningFieldType.Checkbox => value is bool boolValue && boolValue,
            SigningFieldType.Multiple => GetValues(value).Any(),
            SigningFieldType.Image or SigningFieldType.File => value is IReadOnlyCollection<TmSigningStepAttachment> { Count: > 0 },
            SigningFieldType.Verification or SigningFieldType.Kba or SigningFieldType.Payment => value is bool boolValue && boolValue || !string.IsNullOrWhiteSpace(FormatValue(value)),
            _ => !string.IsNullOrWhiteSpace(FormatValue(value))
        };
    }

    private void ScheduleAutoSave(IReadOnlyDictionary<string, object?> snapshot)
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _autoSaveState = "saving";

        _ = InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(AutoSaveDelay, Clock, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await OnAutoSave.InvokeAsync(snapshot);
                _autoSaveError = null;
                _autoSaveState = "saved";
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _autoSaveError = ex.Message;
                _autoSaveState = "error";
            }

            StateHasChanged();
        });
    }

    private IReadOnlyDictionary<string, object?> SnapshotValues()
    {
        return new Dictionary<string, object?>(_values, StringComparer.Ordinal);
    }

    private string GetRequiredMessage(SigningStepItem step)
    {
        var field = step.Fields.FirstOrDefault(item => item.Required && !IsFieldComplete(item)) ?? step.Field;
        return SigningLocalizationResolver.ResolveValidationMessage(field.Validation, CurrentCulture, FallbackCulture, Loc["TmSigningFormRunner_RequiredMissing"]);
    }

    private SigningSubmissionLocalizationSnapshot CreateLocalizationSnapshot()
    {
        return new SigningSubmissionLocalizationSnapshot
        {
            Culture = CurrentCulture,
            FallbackCulture = FallbackCulture,
            GeneratedAt = DateTimeOffset.UtcNow,
            PdfContentTranslated = false,
            Fields = Fields.Select(CreateFieldLocalizationSnapshot).ToList()
        };
    }

    private SigningSubmissionFieldLocalizationSnapshot CreateFieldLocalizationSnapshot(SigningField field)
    {
        return new SigningSubmissionFieldLocalizationSnapshot
        {
            FieldUuid = field.Uuid,
            Label = SigningLocalizationResolver.ResolveFieldLabel(field, CurrentCulture, FallbackCulture, field.Type.ToString()),
            Title = SigningLocalizationResolver.ResolveFieldTitle(field, CurrentCulture, FallbackCulture),
            Description = SigningLocalizationResolver.ResolveFieldDescription(field, CurrentCulture, FallbackCulture),
            ValidationMessage = SigningLocalizationResolver.ResolveValidationMessage(field.Validation, CurrentCulture, FallbackCulture),
            Options = field.Options.Select(option => new SigningSubmissionOptionLocalizationSnapshot
            {
                OptionUuid = option.Uuid,
                Value = option.Value,
                Label = SigningLocalizationResolver.ResolveOptionLabel(option, CurrentCulture, FallbackCulture)
            }).ToList()
        };
    }

    private static string GetCultureDisplayName(string culture)
    {
        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(culture);
            return string.IsNullOrWhiteSpace(cultureInfo.NativeName) ? culture : cultureInfo.NativeName;
        }
        catch (CultureNotFoundException)
        {
            return culture;
        }
    }

    private static void AddClass(List<string> classes, bool condition, string cssClass)
    {
        if (condition)
        {
            classes.Add(cssClass);
        }
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static IEnumerable<string> GetValues(object? value)
    {
        if (value is null)
        {
            return [];
        }

        if (value is string text)
        {
            return string.IsNullOrWhiteSpace(text) ? [] : [text];
        }

        if (value is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>().Select(FormatValue).Where(text => !string.IsNullOrWhiteSpace(text));
        }

        return [FormatValue(value)];
    }

    private decimal? EvaluateFormula(string formula)
    {
        var expression = formula;
        foreach (var token in SigningFormulaHelper.ExtractTokens(formula))
        {
            var value = GetDecimalValue(token) ?? 0m;
            expression = expression.Replace("{{" + token + "}}", value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        try
        {
            var result = new System.Data.DataTable().Compute(expression, null);
            return decimal.TryParse(FormatValue(result), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // The runner is IAsyncDisposable ONLY (phase 5 review): the Blazor renderer disposes via
        // IAsyncDisposable when both interfaces are present, so a sync Dispose() would never run
        // and the pending autosave debounce would fire into a disposed component. Cancel the
        // debounce first, then release the signing-runner.js module instance.
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;

        if (_runnerInterop is not null)
        {
            await _runnerInterop.DisposeAsync();
            _runnerInterop = null;
        }
    }
}

internal static class SigningStepListExtensions
{
    public static int FindIndex(this IReadOnlyList<SigningStepItem> steps, Predicate<SigningStepItem> predicate)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            if (predicate(steps[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
