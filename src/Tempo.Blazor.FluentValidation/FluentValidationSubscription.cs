using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using Microsoft.AspNetCore.Components.Forms;

namespace Tempo.Blazor.FluentValidation;

/// <summary>
/// The live wiring between one <see cref="EditContext"/> and FluentValidation, created by
/// <see cref="EditContextFluentValidationExtensions.AddFluentValidation(EditContext, IServiceProvider, FluentValidationOptions)"/>.
/// Owns a single <see cref="ValidationMessageStore"/> holding two layers:
/// <list type="bullet">
/// <item><description><b>local</b> — failures produced by running the validator here (recomputed on every run), and</description></item>
/// <item><description><b>external</b> — failures injected via <see cref="SetExternalFailures"/> (e.g. a server 422
/// response), which persist across local runs until replaced or cleared, or — with
/// <see cref="FluentValidationOptions.ClearExternalFailureOnFieldChange"/> — until their field is edited.</description></item>
/// </list>
/// Both layers render through the same field mapping and message formatting, and an external failure identical
/// to a local one (same mapped field, same rendered text) is shown once.
/// </summary>
public sealed class FluentValidationSubscription : IDisposable
{
    private readonly EditContext _editContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly FluentValidationOptions _options;
    private readonly ValidationMessageStore _messageStore;

    private List<(FieldIdentifier Field, string Message)> _local = [];
    private List<(FieldIdentifier Field, string Message)> _external = [];
    private int _runVersion;
    private bool _disposed;

    internal FluentValidationSubscription(EditContext editContext, IServiceProvider serviceProvider, FluentValidationOptions options)
    {
        _editContext = editContext;
        _serviceProvider = serviceProvider;
        _options = options;
        _messageStore = new ValidationMessageStore(editContext);
        _editContext.OnValidationRequested += OnValidationRequested;
        _editContext.OnFieldChanged += OnFieldChanged;
    }

    /// <summary>
    /// Runs a full-model validation and refreshes the store's local layer. <paramref name="ruleSets"/> and
    /// <paramref name="prepareContext"/> override the options' defaults for this run only. Returns the raw
    /// FluentValidation result (the caller applies its own severity/validity policy).
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        IReadOnlyCollection<string>? ruleSets = null,
        Action<IValidationContext>? prepareContext = null)
    {
        var version = ++_runVersion;
        var model = ResolveModel();
        if (model is null || ResolveValidator(model.GetType()) is not { } validator)
        {
            ApplyLocal(version, []);
            return new ValidationResult();
        }

        var context = BuildContext(model, ruleSets ?? _options.RuleSets?.Invoke(), memberName: null);
        (prepareContext ?? _options.PrepareContext)?.Invoke(context);

        var result = await validator.ValidateAsync(context);

        ApplyLocal(version, result.Errors);
        return result;
    }

    /// <summary>
    /// Replaces the external failure layer (server-side / out-of-band failures) and re-renders the store.
    /// Null or empty clears the layer. Local messages are untouched. The failures persist until replaced or
    /// cleared, or — with <see cref="FluentValidationOptions.ClearExternalFailureOnFieldChange"/> — until their
    /// field is edited.
    /// </summary>
    public void SetExternalFailures(IEnumerable<ValidationFailure>? failures)
    {
        if (_disposed)
        {
            return;
        }

        _external = failures is null ? [] : [.. failures.Select(f => (MapField(f), RenderMessage(f)))];
        RefreshStore();
    }

    /// <summary>Clears both layers and notifies the EditContext.</summary>
    public void Clear()
    {
        _local = [];
        _external = [];
        RefreshStore();
    }

    /// <summary>Unsubscribes from the EditContext and clears all messages this subscription owns.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _editContext.OnValidationRequested -= OnValidationRequested;
        _editContext.OnFieldChanged -= OnFieldChanged;
        _local = [];
        _external = [];
        _messageStore.Clear();
        _editContext.NotifyValidationStateChanged();
    }

    private async void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
        => await ValidateAsync();

    private async void OnFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        // Opt-in: an edit drops the server-side failure of THIS field (compared on the mapped FieldIdentifier,
        // not the bare property name) before the local re-validation re-renders the store.
        if (_options.ClearExternalFailureOnFieldChange)
        {
            var before = _external.Count;
            _external = [.. _external.Where(x => !x.Field.Equals(e.FieldIdentifier))];
            if (_external.Count != before)
            {
                // RefreshStore calls NotifyValidationStateChanged, and the local re-validation below calls it
                // again when it finishes — so a consumer can see two notifications for one edit. That is
                // intentional: this first one removes the stale server message immediately, even when the
                // local run is async, skipped (OnFieldChanged = None) or has no validator to run.
                RefreshStore();
            }
        }

        switch (_options.OnFieldChanged)
        {
            case FieldChangedValidation.FullModel:
                await ValidateAsync();
                break;
            case FieldChangedValidation.Member:
                await ValidateMemberAsync(e.FieldIdentifier);
                break;
            case FieldChangedValidation.None:
                break;
        }
    }

    private async Task ValidateMemberAsync(FieldIdentifier fieldIdentifier)
    {
        var model = ResolveModel();
        if (model is null || ResolveValidator(model.GetType()) is not { } validator)
        {
            return;
        }

        var context = BuildContext(model, _options.RuleSets?.Invoke(), fieldIdentifier.FieldName);
        _options.PrepareContext?.Invoke(context);

        var result = await validator.ValidateAsync(context);
        if (_disposed)
        {
            return;
        }

        // Member-scoped run: replace only this field's local messages (pre-existing behaviour — the
        // errors land on the notified field).
        _local.RemoveAll(entry => entry.Field.Equals(fieldIdentifier));
        _local.AddRange(result.Errors.Select(error => (fieldIdentifier, RenderMessage(error))));
        RefreshStore();
    }

    private void ApplyLocal(int version, IEnumerable<ValidationFailure> failures)
    {
        // A newer run superseded this one while it awaited — drop the stale result.
        if (_disposed || version != _runVersion)
        {
            return;
        }

        _local = [.. failures.Select(f => (MapField(f), RenderMessage(f)))];
        RefreshStore();
    }

    private void RefreshStore()
    {
        _messageStore.Clear();
        var seen = new HashSet<(FieldIdentifier, string)>();
        foreach (var (field, message) in _local.Concat(_external))
        {
            if (seen.Add((field, message)))
            {
                _messageStore.Add(field, message);
            }
        }

        _editContext.NotifyValidationStateChanged();
    }

    private ValidationContext<object> BuildContext(object model, IReadOnlyCollection<string>? ruleSets, string? memberName)
    {
        IValidatorSelector selector = ruleSets is { Count: > 0 }
            ? new RulesetValidatorSelector(ruleSets)
            : ValidatorOptions.Global.ValidatorSelectors.DefaultValidatorSelectorFactory();
        if (memberName is not null)
        {
            selector = new IntersectingSelector(
                selector, new MemberNameValidatorSelector([memberName]));
        }

        return new ValidationContext<object>(model, new PropertyChain(), selector);
    }

    private object? ResolveModel() => _options.ModelProvider is { } provider ? provider() : _editContext.Model;

    private IValidator? ResolveValidator(Type modelType)
        => _serviceProvider.GetService(typeof(IValidator<>).MakeGenericType(modelType)) as IValidator;

    private FieldIdentifier MapField(ValidationFailure failure)
        => _options.FieldMapper?.Invoke(failure) ?? _editContext.Field(failure.PropertyName);

    private string RenderMessage(ValidationFailure failure)
        => _options.MessageFormatter?.Invoke(failure) ?? failure.ErrorMessage;

    /// <summary>Executes a rule only when every inner selector agrees (rule-set filter AND member filter).</summary>
    private sealed class IntersectingSelector : IValidatorSelector
    {
        private readonly IValidatorSelector _first;
        private readonly IValidatorSelector _second;

        public IntersectingSelector(IValidatorSelector first, IValidatorSelector second)
        {
            _first = first;
            _second = second;
        }

        public bool CanExecute(IValidationRule rule, string propertyPath, IValidationContext context)
            => _first.CanExecute(rule, propertyPath, context) && _second.CanExecute(rule, propertyPath, context);
    }
}
