using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.FluentValidation;

namespace Tempo.Blazor.FluentValidation.Tests;

#region Demo model + validator (mirrors Demo project)

public class PersonFormModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Department { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public class PersonFormValidator : AbstractValidator<PersonFormModel>
{
    public PersonFormValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.")
            .MaximumLength(50).WithMessage("First name must be 50 characters or less.");
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email address.");
        RuleFor(x => x.Age).InclusiveBetween(18, 65)
            .WithMessage("Age must be between 18 and 65.");
        RuleFor(x => x.Department).NotEmpty().WithMessage("Department is required.");
        RuleFor(x => x.StartDate).NotNull().WithMessage("Start date is required.");
        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("End date must be after start date.");
    }
}

#endregion

public class PersonFormValidationTests : TestContext
{
    private IRenderedFragment RenderPersonForm(PersonFormModel model)
    {
        Services.AddSingleton<IValidator<PersonFormModel>>(new PersonFormValidator());

        return Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent),
                (RenderFragment<EditContext>)(context => childBuilder =>
                {
                    childBuilder.OpenComponent<FluentValidationValidator>(0);
                    childBuilder.CloseComponent();
                    childBuilder.OpenComponent<ValidationSummary>(1);
                    childBuilder.CloseComponent();
                }));
            builder.CloseComponent();
        });
    }

    [Fact]
    public void PersonForm_Submit_WithEmptyFirstName_ShowsError()
    {
        // Arrange
        var model = new PersonFormModel { LastName = "Doe", Email = "j@e.com", Age = 30, Department = "IT", StartDate = DateOnly.FromDateTime(DateTime.Today) };

        // Act
        var cut = RenderPersonForm(model);
        cut.Find("form").Submit();

        // Assert
        var errors = cut.FindAll(".validation-errors li");
        errors.Should().Contain(e => e.TextContent.Contains("First name is required"));
    }

    [Fact]
    public void PersonForm_Submit_WithValidData_NoErrors()
    {
        // Arrange
        var model = new PersonFormModel
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30,
            Department = "Engineering",
            StartDate = DateOnly.FromDateTime(DateTime.Today)
        };

        // Act
        var cut = RenderPersonForm(model);
        cut.Find("form").Submit();

        // Assert
        cut.FindAll(".validation-errors li").Should().BeEmpty();
    }

    [Fact]
    public void PersonForm_EndDateBeforeStartDate_ShowsError()
    {
        // Arrange
        var model = new PersonFormModel
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30,
            Department = "Engineering",
            StartDate = new DateOnly(2024, 6, 1),
            EndDate = new DateOnly(2024, 5, 1) // before start
        };

        // Act
        var cut = RenderPersonForm(model);
        cut.Find("form").Submit();

        // Assert
        var errors = cut.FindAll(".validation-errors li");
        errors.Should().Contain(e => e.TextContent.Contains("End date must be after start date"));
    }

    [Fact]
    public void PersonForm_FixError_ClearsValidationMessage()
    {
        // Arrange — start with error
        var model = new PersonFormModel
        {
            FirstName = "",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 30,
            Department = "Engineering",
            StartDate = DateOnly.FromDateTime(DateTime.Today)
        };

        var cut = RenderPersonForm(model);
        cut.Find("form").Submit();

        // Confirm there is a first name error
        cut.FindAll(".validation-errors li")
            .Should().Contain(e => e.TextContent.Contains("First name is required"));

        // Fix the error
        model.FirstName = "John";
        cut.Find("form").Submit();

        // Assert — error should be cleared
        cut.FindAll(".validation-errors li")
            .Where(e => e.TextContent.Contains("First name"))
            .Should().BeEmpty();
    }

    [Fact]
    public void PersonForm_InvalidEmail_ShowsError()
    {
        // Arrange
        var model = new PersonFormModel
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "not-an-email",
            Age = 30,
            Department = "Engineering",
            StartDate = DateOnly.FromDateTime(DateTime.Today)
        };

        // Act
        var cut = RenderPersonForm(model);
        cut.Find("form").Submit();

        // Assert
        var errors = cut.FindAll(".validation-errors li");
        errors.Should().Contain(e => e.TextContent.Contains("Invalid email"));
    }

    [Fact]
    public void PersonForm_AgeBelowMinimum_ShowsError()
    {
        // Arrange
        var model = new PersonFormModel
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Age = 10, // below minimum 18
            Department = "Engineering",
            StartDate = DateOnly.FromDateTime(DateTime.Today)
        };

        // Act
        var cut = RenderPersonForm(model);
        cut.Find("form").Submit();

        // Assert
        var errors = cut.FindAll(".validation-errors li");
        errors.Should().Contain(e => e.TextContent.Contains("Age must be between 18 and 65"));
    }

    [Fact]
    public void PersonForm_AllFieldsEmpty_ShowsMultipleErrors()
    {
        // Arrange — completely empty model
        var model = new PersonFormModel();

        // Act
        var cut = RenderPersonForm(model);
        cut.Find("form").Submit();

        // Assert — should have errors for FirstName, LastName, Email, Age, Department, StartDate
        var errors = cut.FindAll(".validation-errors li");
        errors.Count.Should().BeGreaterThanOrEqualTo(6);
    }
}
