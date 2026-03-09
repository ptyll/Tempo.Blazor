using FluentValidation;
using FluentValidation.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Tempo.Blazor.FluentValidation;

/// <summary>
/// A Blazor component that integrates FluentValidation with EditForm.
/// Place inside an EditForm to enable FluentValidation-based validation.
/// Works as a drop-in replacement for DataAnnotationsValidator.
/// </summary>
public class FluentValidationValidator : ComponentBase, IDisposable
{
    [CascadingParameter] private EditContext? CurrentEditContext { get; set; }
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private ValidationMessageStore? _messageStore;

    protected override void OnInitialized()
    {
        if (CurrentEditContext is null)
        {
            throw new InvalidOperationException(
                $"{nameof(FluentValidationValidator)} requires a cascading {nameof(EditContext)}. " +
                $"Ensure it is used inside an EditForm.");
        }

        _messageStore = new ValidationMessageStore(CurrentEditContext);
        CurrentEditContext.OnValidationRequested += OnValidationRequested;
        CurrentEditContext.OnFieldChanged += OnFieldChanged;
    }

    private async void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        await ValidateModelAsync();
    }

    private async void OnFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        await ValidateFieldAsync(e.FieldIdentifier);
    }

    private async Task ValidateModelAsync()
    {
        if (CurrentEditContext is null) return;

        var validator = GetValidator(CurrentEditContext.Model.GetType());
        if (validator is null) return;

        _messageStore!.Clear();

        var context = new ValidationContext<object>(CurrentEditContext.Model);
        var result = await validator.ValidateAsync(context);

        foreach (var error in result.Errors)
        {
            var fieldId = CurrentEditContext.Field(error.PropertyName);
            _messageStore.Add(fieldId, error.ErrorMessage);
        }

        CurrentEditContext.NotifyValidationStateChanged();
    }

    private async Task ValidateFieldAsync(FieldIdentifier fieldIdentifier)
    {
        if (CurrentEditContext is null) return;

        var validator = GetValidator(CurrentEditContext.Model.GetType());
        if (validator is null) return;

        _messageStore!.Clear(fieldIdentifier);

        var context = new ValidationContext<object>(
            CurrentEditContext.Model,
            new PropertyChain(),
            new MemberNameValidatorSelector(new[] { fieldIdentifier.FieldName }));
        var result = await validator.ValidateAsync(context);

        foreach (var error in result.Errors)
        {
            _messageStore.Add(fieldIdentifier, error.ErrorMessage);
        }

        CurrentEditContext.NotifyValidationStateChanged();
    }

    private IValidator? GetValidator(Type modelType)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(modelType);
        return ServiceProvider.GetService(validatorType) as IValidator;
    }

    public void Dispose()
    {
        if (CurrentEditContext is not null)
        {
            CurrentEditContext.OnValidationRequested -= OnValidationRequested;
            CurrentEditContext.OnFieldChanged -= OnFieldChanged;
        }
    }
}
