using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.FluentValidation;

namespace Tempo.Blazor.FluentValidation.Tests;

// ── Test doubles for the programmatic (options-based) pipeline ──────────────────────────────

/// <summary>The model of the programmatic tests (a dedicated type, so the assembly-scan tests keep exactly one IValidator&lt;PersonModel&gt; registration).</summary>
public class SegmentedModel
{
    public string FirstName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
}

/// <summary>A collection item of <see cref="ItemsModel"/> (several items share the field name <c>Email</c>).</summary>
public class ItemModel
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>A model holding a collection, for tests that need the same field name on several objects.</summary>
public class ItemsModel
{
    public List<ItemModel> Items { get; set; } = [];
}

/// <summary>A validator with a default rule, a named rule set and a RootContextData-driven rule.</summary>
public class SegmentedPersonValidator : AbstractValidator<SegmentedModel>
{
    public const string ContextKey = "test-context";

    public SegmentedPersonValidator()
    {
        RuleFor(p => p.Email).NotEmpty().EmailAddress();

        RuleSet("Names", () =>
        {
            RuleFor(p => p.FirstName).NotEmpty();
        });

        RuleSet("Contextual", () =>
        {
            RuleFor(p => p).Custom((_, ctx) =>
            {
                if (ctx.RootContextData.TryGetValue(ContextKey, out var raw) && raw is string message)
                {
                    ctx.AddFailure("CONTEXT_FIELD", message);
                }
            });
        });
    }
}

public class ProgrammaticValidationTests
{
    private static IServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static IServiceProvider WithSegmentedValidator()
        => BuildServiceProvider(s => s.AddSingleton<IValidator<SegmentedModel>>(new SegmentedPersonValidator()));

    // ── ModelProvider ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_WithModelProvider_ValidatesForeignModel()
    {
        // The EditContext model is a plain peg object; the validated model comes from the provider.
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "not-an-email", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
        });

        var result = await subscription.ValidateAsync();

        result.Errors.Should().NotBeEmpty();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_NullModel_ClearsLocalAndReturnsValid()
    {
        var editContext = new EditContext(new object());
        SegmentedModel? model = new SegmentedModel { FirstName = "", Email = "bad", Age = 1 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
        });
        await subscription.ValidateAsync();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().NotBeEmpty();

        model = null;
        var result = await subscription.ValidateAsync();

        result.IsValid.Should().BeTrue();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
    }

    // ── Rule set selection ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_WithRuleSets_RunsOnlySelectedSets()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "bad", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
        });

        var result = await subscription.ValidateAsync(ruleSets: ["Names"]);

        result.Errors.Select(e => e.PropertyName).Should().ContainSingle()
            .Which.Should().Be(nameof(SegmentedModel.FirstName));
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName))).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_UsesOptionsRuleSetsAsDefault()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "bad", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Names"],
        });

        var result = await subscription.ValidateAsync();

        result.Errors.Select(e => e.PropertyName).Should().Equal(nameof(SegmentedModel.FirstName));
    }

    // ── PrepareContext / RootContextData ─────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_PrepareContext_PopulatesRootContextData()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "John", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Contextual"],
            PrepareContext = ctx => ctx.RootContextData[SegmentedPersonValidator.ContextKey] = "from-options",
        });

        var result = await subscription.ValidateAsync();

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "from-options");
        editContext.GetValidationMessages(editContext.Field("CONTEXT_FIELD")).Should().Contain("from-options");
    }

    [Fact]
    public async Task ValidateAsync_PerCallPrepareContext_OverridesOptions()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "John", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Contextual"],
            PrepareContext = ctx => ctx.RootContextData[SegmentedPersonValidator.ContextKey] = "from-options",
        });

        var result = await subscription.ValidateAsync(
            prepareContext: ctx => ctx.RootContextData[SegmentedPersonValidator.ContextKey] = "per-call");

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "per-call");
    }

    // ── Field mapping and message formatting ─────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_FieldMapper_RemapsFailureToField()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "John", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Contextual"],
            PrepareContext = ctx => ctx.RootContextData[SegmentedPersonValidator.ContextKey] = "mapped",
            FieldMapper = failure => editContext.Field("REMAPPED"),
        });

        await subscription.ValidateAsync();

        editContext.GetValidationMessages(editContext.Field("REMAPPED")).Should().Contain("mapped");
        editContext.GetValidationMessages(editContext.Field("CONTEXT_FIELD")).Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_MessageFormatter_FormatsDisplayedMessage()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Names"],
            MessageFormatter = failure => $"[fmt] {failure.PropertyName}",
        });

        await subscription.ValidateAsync();

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName)))
            .Should().Contain("[fmt] FirstName");
    }

    // ── External (server) failures — unified store, no duplicates ────────────────────────────

    [Fact]
    public void SetExternalFailures_AddsMessagesToSameStore()
    {
        var editContext = new EditContext(new object());
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => new SegmentedModel { FirstName = "John", Email = "john@example.com", Age = 30 },
        });

        subscription.SetExternalFailures([new ValidationFailure("SERVER_FIELD", "server-message")]);

        editContext.GetValidationMessages(editContext.Field("SERVER_FIELD")).Should().Contain("server-message");
    }

    [Fact]
    public async Task SetExternalFailures_DeduplicatesAgainstLocalFailures()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Names"],
            MessageFormatter = f => f.ErrorMessage,
        });
        await subscription.ValidateAsync();
        var localMessage = editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName))).Single();

        // The server reports the SAME failure (same field, same rendered message) plus one extra.
        subscription.SetExternalFailures(
        [
            new ValidationFailure(nameof(SegmentedModel.FirstName), localMessage),
            new ValidationFailure("SERVER_ONLY", "tier-c"),
        ]);

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName)))
            .Should().ContainSingle("the identical Tier B + Tier C failure must render once");
        editContext.GetValidationMessages(editContext.Field("SERVER_ONLY")).Should().Contain("tier-c");
    }

    [Fact]
    public async Task ExternalFailures_SurviveLocalRevalidation()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Names"],
        });
        subscription.SetExternalFailures([new ValidationFailure("SERVER_ONLY", "tier-c")]);

        await subscription.ValidateAsync();

        editContext.GetValidationMessages(editContext.Field("SERVER_ONLY")).Should().Contain("tier-c");
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName))).Should().NotBeEmpty();
    }

    [Fact]
    public async Task SetExternalFailures_Null_ClearsExternalKeepsLocal()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Names"],
        });
        await subscription.ValidateAsync();
        subscription.SetExternalFailures([new ValidationFailure("SERVER_ONLY", "tier-c")]);

        subscription.SetExternalFailures(null);

        editContext.GetValidationMessages(editContext.Field("SERVER_ONLY")).Should().BeEmpty();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName))).Should().NotBeEmpty();
    }

    // ── ClearExternalFailureOnFieldChange (opt-in) ───────────────────────────────────────────

    private static (EditContext Context, FluentValidationSubscription Subscription) ExternalOnTwoFields(
        bool clearOnFieldChange,
        Func<ValidationFailure, FieldIdentifier?>? fieldMapper = null,
        FieldChangedValidation onFieldChanged = FieldChangedValidation.Member)
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "John", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            ClearExternalFailureOnFieldChange = clearOnFieldChange,
            FieldMapper = fieldMapper,
            OnFieldChanged = onFieldChanged,
        });
        subscription.SetExternalFailures(
        [
            new ValidationFailure(nameof(SegmentedModel.FirstName), "server-first-name"),
            new ValidationFailure(nameof(SegmentedModel.Email), "server-email"),
        ]);
        return (editContext, subscription);
    }

    [Fact]
    public void ClearExternalFailureOnFieldChange_ClearsOnlyTheEditedField()
    {
        var (editContext, _) = ExternalOnTwoFields(clearOnFieldChange: true);

        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName)))
            .Should().ContainSingle().Which.Should().Be("server-first-name");
        editContext.GetValidationMessages().Should().HaveCount(1);
    }

    [Fact]
    public void ClearExternalFailureOnFieldChange_DefaultOff_KeepsBothFailures()
    {
        var (editContext, _) = ExternalOnTwoFields(clearOnFieldChange: false);

        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().Contain("server-email");
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName))).Should().Contain("server-first-name");
        editContext.GetValidationMessages().Should().HaveCount(2);
    }

    [Fact]
    public void ClearExternalFailureOnFieldChange_FalseIsTheOptionsDefault()
        => new FluentValidationOptions().ClearExternalFailureOnFieldChange.Should().BeFalse();

    [Fact]
    public void ClearExternalFailureOnFieldChange_LaterSetExternalFailures_BringsTheFailureBack()
    {
        var (editContext, subscription) = ExternalOnTwoFields(clearOnFieldChange: true);
        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();

        // A fresh server response (the edited field fails again) is NOT swallowed by the earlier clear.
        subscription.SetExternalFailures(
        [
            new ValidationFailure(nameof(SegmentedModel.FirstName), "server-first-name"),
            new ValidationFailure(nameof(SegmentedModel.Email), "server-email-again"),
        ]);

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().Contain("server-email-again");
        editContext.GetValidationMessages().Should().HaveCount(2);
    }

    [Fact]
    public void ClearExternalFailureOnFieldChange_ComparesTheMappedField_NotTheBareName()
    {
        // The server path "Items[3].Email" is mapped onto the Email field; editing Email must clear it,
        // although the failure's raw PropertyName differs from the edited field name.
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "John", Email = "john@example.com", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            ClearExternalFailureOnFieldChange = true,
            FieldMapper = failure => failure.PropertyName.StartsWith("Items[", StringComparison.Ordinal)
                ? editContext.Field(nameof(SegmentedModel.Email))
                : null,
        });
        subscription.SetExternalFailures(
        [
            new ValidationFailure("Items[3].Email", "server-item-email"),
            new ValidationFailure(nameof(SegmentedModel.FirstName), "server-first-name"),
        ]);
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().Contain("server-item-email");

        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
        editContext.GetValidationMessages().Should().HaveCount(1);
    }

    [Fact]
    public void ClearExternalFailureOnFieldChange_SameFieldNameOnAnotherItem_IsKept()
    {
        // Two collection items share the field NAME "Email"; only the mapped FieldIdentifier (model object +
        // field name) tells them apart. Editing item 1's Email must not drop the failure on item 0's Email.
        var item0 = new ItemModel { Email = "a@example.com" };
        var item1 = new ItemModel { Email = "b@example.com" };
        var model = new ItemsModel { Items = [item0, item1] };
        var editContext = new EditContext(model);
        var subscription = editContext.AddFluentValidation(BuildServiceProvider(), new FluentValidationOptions
        {
            ClearExternalFailureOnFieldChange = true,
            FieldMapper = failure => new FieldIdentifier(model.Items[ItemIndex(failure.PropertyName)], nameof(ItemModel.Email)),
        });
        subscription.SetExternalFailures(
        [
            new ValidationFailure("Items[0].Email", "server-item0-email"),
            new ValidationFailure("Items[1].Email", "server-item1-email"),
        ]);
        var field0 = new FieldIdentifier(item0, nameof(ItemModel.Email));
        var field1 = new FieldIdentifier(item1, nameof(ItemModel.Email));
        editContext.GetValidationMessages(field0).Should().ContainSingle().Which.Should().Be("server-item0-email");
        editContext.GetValidationMessages(field1).Should().ContainSingle().Which.Should().Be("server-item1-email");

        editContext.NotifyFieldChanged(field1);

        editContext.GetValidationMessages(field1).Should().BeEmpty();
        editContext.GetValidationMessages(field0).Should().ContainSingle().Which.Should().Be("server-item0-email");
        editContext.GetValidationMessages().Should().HaveCount(1);
    }

    private static int ItemIndex(string propertyName)
        => int.Parse(propertyName[(propertyName.IndexOf('[') + 1)..propertyName.IndexOf(']')], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ClearExternalFailureOnFieldChange_AlsoAppliesWhenFieldChangedValidationIsNone()
    {
        var (editContext, _) = ExternalOnTwoFields(clearOnFieldChange: true, onFieldChanged: FieldChangedValidation.None);

        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
        editContext.GetValidationMessages().Should().HaveCount(1);
    }

    // ── Field-changed behaviour ──────────────────────────────────────────────────────────────

    [Fact]
    public void OnFieldChanged_FullModel_RevalidatesWholeModel()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "john@example.com", Age = 30 };
        editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            RuleSets = () => ["Names"],
            OnFieldChanged = FieldChangedValidation.FullModel,
        });

        // Touching ANY field triggers a full run — the FirstName failure appears although Email changed.
        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.FirstName))).Should().NotBeEmpty();
    }

    [Fact]
    public void OnFieldChanged_None_DoesNothing()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "bad", Age = 30 };
        editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            OnFieldChanged = FieldChangedValidation.None,
        });

        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
    }

    [Fact]
    public void OnFieldChanged_Member_ValidatesOnlyThatField()
    {
        // Default mode over the EditContext's own model — the pre-existing behaviour.
        var model = new PersonModel { FirstName = "", Email = "bad", Age = 30 };
        var editContext = new EditContext(model);
        var sp = BuildServiceProvider(s => s.AddSingleton<IValidator<PersonModel>>(new PersonValidator()));
        editContext.AddFluentValidation(sp, new FluentValidationOptions());

        editContext.NotifyFieldChanged(editContext.Field(nameof(PersonModel.Email)));

        editContext.GetValidationMessages(editContext.Field(nameof(PersonModel.Email))).Should().NotBeEmpty();
        editContext.GetValidationMessages(editContext.Field(nameof(PersonModel.FirstName))).Should().BeEmpty();
    }

    // ── OnValidationRequested integration (EditContext.Validate) ─────────────────────────────

    [Fact]
    public void Validate_WithOptions_RunsDefaultPipeline()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "bad", Age = 30 };
        editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
        });

        editContext.Validate();

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().NotBeEmpty();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clear_RemovesLocalAndExternalMessages()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "bad", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
        });
        await subscription.ValidateAsync();
        subscription.SetExternalFailures([new ValidationFailure("SERVER_ONLY", "tier-c")]);

        subscription.Clear();

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
        editContext.GetValidationMessages(editContext.Field("SERVER_ONLY")).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_UnsubscribesAndClears()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "bad", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
            OnFieldChanged = FieldChangedValidation.FullModel,
        });
        await subscription.ValidateAsync();

        subscription.Dispose();

        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
        editContext.NotifyFieldChanged(editContext.Field(nameof(SegmentedModel.Email)));
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ValidModel_ReturnsValidAndClearsPreviousLocal()
    {
        var editContext = new EditContext(new object());
        var model = new SegmentedModel { FirstName = "", Email = "bad", Age = 30 };
        var subscription = editContext.AddFluentValidation(WithSegmentedValidator(), new FluentValidationOptions
        {
            ModelProvider = () => model,
        });
        await subscription.ValidateAsync();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().NotBeEmpty();

        model.FirstName = "John";
        model.Email = "john@example.com";
        var result = await subscription.ValidateAsync();

        result.IsValid.Should().BeTrue();
        editContext.GetValidationMessages(editContext.Field(nameof(SegmentedModel.Email))).Should().BeEmpty();
    }
}
