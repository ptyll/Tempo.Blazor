using FluentValidation;
using FluentValidation.Internal;
using Microsoft.AspNetCore.Components.Forms;

namespace Tempo.Blazor.FluentValidation;

/// <summary>
/// Extension methods for <see cref="EditContext"/> to enable FluentValidation
/// without using the <see cref="FluentValidationValidator"/> component.
/// Useful for programmatic validation scenarios.
/// </summary>
public static class EditContextFluentValidationExtensions
{
    /// <summary>
    /// Enables FluentValidation for the EditContext.
    /// Attaches OnValidationRequested and OnFieldChanged handlers
    /// that resolve and invoke the appropriate IValidator from DI.
    /// </summary>
    /// <param name="editContext">The EditContext to extend.</param>
    /// <param name="serviceProvider">The service provider to resolve validators from.</param>
    /// <returns>The same EditContext for fluent chaining.</returns>
    public static EditContext AddFluentValidation(this EditContext editContext, IServiceProvider serviceProvider)
    {
        var messageStore = new ValidationMessageStore(editContext);

        editContext.OnValidationRequested += async (sender, _) =>
        {
            var ctx = (EditContext)sender!;
            var validator = ResolveValidator(ctx.Model.GetType(), serviceProvider);
            if (validator is null) return;

            messageStore.Clear();
            var context = new ValidationContext<object>(ctx.Model);
            var result = await validator.ValidateAsync(context);

            foreach (var error in result.Errors)
            {
                var fieldId = ctx.Field(error.PropertyName);
                messageStore.Add(fieldId, error.ErrorMessage);
            }

            ctx.NotifyValidationStateChanged();
        };

        editContext.OnFieldChanged += async (sender, args) =>
        {
            var ctx = (EditContext)sender!;
            var validator = ResolveValidator(ctx.Model.GetType(), serviceProvider);
            if (validator is null) return;

            messageStore.Clear(args.FieldIdentifier);
            var context = new ValidationContext<object>(
                ctx.Model,
                new PropertyChain(),
                new MemberNameValidatorSelector(new[] { args.FieldIdentifier.FieldName }));
            var result = await validator.ValidateAsync(context);

            foreach (var error in result.Errors)
            {
                messageStore.Add(args.FieldIdentifier, error.ErrorMessage);
            }

            ctx.NotifyValidationStateChanged();
        };

        return editContext;
    }

    private static IValidator? ResolveValidator(Type modelType, IServiceProvider serviceProvider)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(modelType);
        return serviceProvider.GetService(validatorType) as IValidator;
    }
}
