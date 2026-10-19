using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Coordinates a linear signing ceremony over document pages and signing field steps.</summary>
public partial class TmSigningFormRunner : IDisposable
{
    private Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, object?>? _lastValues;
    private int _currentStepIndex;
    private string? _validationMessage;
    private string? _autoSaveError;
    private string _autoSaveState = "idle";
    private bool _accessibilityMode;
    private bool _mobileExpanded;
    private readonly Dictionary<string, TmSignatureCaptureMode> _signatureCaptureModes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _autoSaveCts;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private TimeProvider? _clock;

    // The autosave debounce runs on an injectable clock so tests advance a FakeTimeProvider instead
    // of sleeping. Resolved lazily (not [Inject]-ed directly): an unregistered optional service
    // fails property injection outright, and hosts that never called AddTempoBlazorSigning must
    // keep working off TimeProvider.System.
    private TimeProvider Clock => _clock ??= Services.GetService<TimeProvider>() ?? TimeProvider.System;

    /// <summary>Document pages shown during signing.</summary>
    [Parameter] public IReadOnlyList<SigningDocumentPage> Pages { get; set; } = [];

    /// <summary>Signing fields available to the signer.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Current field values keyed by field UUID.</summary>
    [Parameter] public IReadOnlyDictionary<string, object?> Values { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Callback invoked whenever field values change.</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, object?>> ValuesChanged { get; set; }

    /// <summary>Optional signer role filter.</summary>
    [Parameter] public string? SubmitterUuid { get; set; }

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

    /// <summary>Additional CSS classes for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private SigningStepPlan Plan => SigningStepPlanner.Plan(Fields, Pages, _values, SubmitterUuid);

    private SigningStepItem? CurrentStep => Plan.Steps.Count == 0
        ? null
        : Plan.Steps[Math.Clamp(CurrentStepIndex, 0, Plan.Steps.Count - 1)];

    private int CurrentStepIndex => Plan.Steps.Count == 0
        ? 0
        : Math.Clamp(_currentStepIndex, 0, Plan.Steps.Count - 1);

    private bool IsPreviousDisabled => IsLoading || CurrentStepIndex == 0;

    private bool IsNextDisabled => IsLoading || IsCompleting || CurrentStepIndex >= Plan.Steps.Count - 1;

    private bool IsSkipDisabled => IsLoading || IsCompleting || CurrentStep is null || CurrentStep.Field.Required;

    private bool IsCompleteDisabled => IsLoading || IsCompleting || !AllRequiredFieldsComplete();

    private string CurrentCulture => !string.IsNullOrWhiteSpace(Culture)
        ? Culture!
        : CultureInfo.CurrentUICulture.Name;

    private bool ShowLanguagePicker => ShowLanguageSelector && SupportedCultures.Count > 1;

    private string RootClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-form-runner" };
            AddClass(classes, IsLoading, "tm-signing-form-runner--loading");
            AddClass(classes, _accessibilityMode, "tm-signing-form-runner--accessibility");
            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes.Add(Class);
            }

            return string.Join(" ", classes);
        }
    }

    private string MobilePanelClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-form-runner__mobile-panel" };
            AddClass(classes, _mobileExpanded, "tm-signing-form-runner__mobile-panel--expanded");
            AddClass(classes, !_mobileExpanded, "tm-signing-form-runner__mobile-panel--collapsed");
            AddClass(classes, !string.IsNullOrWhiteSpace(MobileCompleteTargetSelector), "tm-signing-form-runner__mobile-panel--has-complete-target");
            return string.Join(" ", classes);
        }
    }

    private string ProgressAriaLabel => Loc["TmSigningFormRunner_ProgressAria", CurrentStepIndex + 1, Plan.Steps.Count];

    private string AutoSaveStatusText => _autoSaveState switch
    {
        "saving" => Loc["TmSigningFormRunner_Saving"],
        "saved" => Loc["TmSigningFormRunner_Saved"],
        "error" => _autoSaveError ?? Loc["TmSigningFormRunner_SaveError"],
        _ => Loc["TmSigningFormRunner_Unsaved"]
    };

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _mobileExpanded = MobilePanelMode == TmSigningFormRunnerMobilePanelMode.Expanded;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Values, _lastValues))
        {
            _values = Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            _lastValues = Values;
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
        _validationMessage = null;
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

    private TmSignatureCaptureMode GetSignatureCaptureMode(SigningField field)
    {
        if (_signatureCaptureModes.TryGetValue(field.Uuid, out var mode))
        {
            return mode;
        }

        return field.Preferences.Format?.Trim().ToLowerInvariant() switch
        {
            "draw" or "drawn" or "handwritten" => TmSignatureCaptureMode.Draw,
            "upload" => TmSignatureCaptureMode.Upload,
            _ => TmSignatureCaptureMode.Typed
        };
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

    private Task HandleOverlayClickAsync(TmSigningFieldOverlayPointerEventArgs args)
    {
        var index = Plan.Steps.FindIndex(step => step.Fields.Any(field => string.Equals(field.Uuid, args.Field.Uuid, StringComparison.Ordinal)));
        if (index >= 0)
        {
            _currentStepIndex = index;
            _validationMessage = null;
        }

        return Task.CompletedTask;
    }

    private Task SelectStep(int index)
    {
        _currentStepIndex = Math.Clamp(index, 0, Math.Max(0, Plan.Steps.Count - 1));
        _validationMessage = null;
        return Task.CompletedTask;
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

        await PublishValuesAsync();
    }

    private async Task SetFieldValueAsync(string fieldUuid, object? value)
    {
        _values[fieldUuid] = value;
        await PublishValuesAsync();
    }

    private async Task PublishValuesAsync()
    {
        _validationMessage = null;
        var snapshot = SnapshotValues();
        await ValuesChanged.InvokeAsync(snapshot);
        ScheduleAutoSave(snapshot);
    }

    private async Task GoNextAsync()
    {
        if (IsLoading || CurrentStep is null)
        {
            return;
        }

        if (!IsStepComplete(CurrentStep))
        {
            _validationMessage = GetRequiredMessage(CurrentStep);
            _currentStepIndex = FindFirstInvalidRequiredStep();
            return;
        }

        await OnStepSubmit.InvokeAsync(CurrentStep);
        if (_currentStepIndex < Plan.Steps.Count - 1)
        {
            _currentStepIndex++;
        }
    }

    private Task GoPreviousAsync()
    {
        if (!IsPreviousDisabled)
        {
            _currentStepIndex--;
            _validationMessage = null;
        }

        return Task.CompletedTask;
    }

    private Task SkipCurrentAsync()
    {
        if (!IsSkipDisabled && _currentStepIndex < Plan.Steps.Count - 1)
        {
            _currentStepIndex++;
            _validationMessage = null;
        }

        return Task.CompletedTask;
    }

    private async Task CompleteAsync()
    {
        if (IsCompleteDisabled)
        {
            _currentStepIndex = FindFirstInvalidRequiredStep();
            _validationMessage = CurrentStep is null
                ? Loc["TmSigningFormRunner_CompleteBlocked"]
                : GetRequiredMessage(CurrentStep);
            return;
        }

        try
        {
            if (OnLocalizationSnapshotChanged.HasDelegate)
            {
                await OnLocalizationSnapshotChanged.InvokeAsync(CreateLocalizationSnapshot());
            }

            await OnComplete.InvokeAsync(SnapshotValues());
            _validationMessage = null;
        }
        catch (Exception ex)
        {
            _validationMessage = ex.Message;
        }
    }

    private void ToggleAccessibilityMode()
    {
        _accessibilityMode = !_accessibilityMode;
    }

    private void ExpandMobilePanel()
    {
        _mobileExpanded = true;
    }

    private void MinimizeMobilePanel()
    {
        _mobileExpanded = false;
    }

    private bool AllRequiredFieldsComplete()
    {
        return Plan.Steps
            .SelectMany(step => step.Fields)
            .Where(field => field.Required)
            .All(IsFieldComplete);
    }

    private bool IsStepComplete(SigningStepItem step)
    {
        if (!step.Field.Required && step.Fields.All(field => !field.Required))
        {
            return true;
        }

        return step.Fields.Where(field => field.Required || step.Field.Required).All(IsFieldComplete);
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

    private int FindFirstInvalidRequiredStep()
    {
        var index = Plan.Steps.FindIndex(step => step.Fields.Any(field => field.Required && !IsFieldComplete(field)));
        return index >= 0 ? index : CurrentStepIndex;
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

    private string GetStepLabel(SigningStepItem step)
    {
        return SigningLocalizationResolver.ResolveFieldLabel(step.Field, CurrentCulture, FallbackCulture, step.Field.Type.ToString());
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
    public void Dispose()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
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
