using FluentValidation;

namespace Tempo.Blazor.Demo.Validators;

public class PersonFormValidator : AbstractValidator<PersonFormModel>
{
    public PersonFormValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(50).WithMessage("First name must be 50 characters or less.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(50).WithMessage("Last name must be 50 characters or less.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email address.");

        RuleFor(x => x.Age)
            .InclusiveBetween(18, 65).WithMessage("Age must be between 18 and 65.");

        RuleFor(x => x.Department)
            .NotEmpty().WithMessage("Department is required.");

        RuleFor(x => x.StartDate)
            .NotNull().WithMessage("Start date is required.");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("End date must be after start date.");
    }
}
