using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

/// <summary>Renders select, radio, multiple-choice, and checkbox signing steps.</summary>
public partial class TmSigningChoiceStep
{
    // Per-instance accessible plumbing (Signing redesign phase 2): ids are owned by THIS component
    // instance and handed to the shell, so two rendered steps never share an id even when they
    // represent the same field uuid.
    private readonly string _titleId = $"tm-signing-step-title-{Guid.NewGuid():N}";
    private readonly string _descriptionId = $"tm-signing-step-description-{Guid.NewGuid():N}";
    private readonly string _validationId = $"tm-signing-step-validation-{Guid.NewGuid():N}";
    private readonly string _hintId = $"tm-signing-choice-hint-{Guid.NewGuid():N}";
    private readonly string _radioName = $"tm-signing-choice-{Guid.NewGuid():N}";
    private string? _validationMessage;

    /// <summary>Primary signing field represented by this step.</summary>
    [Parameter] public SigningField Field { get; set; } = new() { Type = SigningFieldType.Select };

    /// <summary>Optional checkbox fields rendered as one grouped checkbox step.</summary>
    [Parameter] public IReadOnlyList<SigningField> Fields { get; set; } = [];

    /// <summary>Current value. Select/radio use string, multiple and grouped checkboxes use string arrays, checkbox uses bool.</summary>
    [Parameter] public object? Value { get; set; }

    /// <summary>Callback invoked when the value changes.</summary>
    [Parameter] public EventCallback<object?> ValueChanged { get; set; }

    /// <summary>Short text describing where the field appears in the document.</summary>
    [Parameter] public string? AppearsOn { get; set; }

    /// <summary>Whether the input controls are disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether a single checkbox should show a generic instruction instead of the field label.</summary>
    [Parameter] public bool AnonymousCheckbox { get; set; }

    /// <summary>Culture used to resolve localized field and option text.</summary>
    [Parameter] public string? Culture { get; set; }

    /// <summary>Fallback culture used when localized text is missing.</summary>
    [Parameter] public string? FallbackCulture { get; set; }

    /// <summary>Additional CSS classes for the shell element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes passed to the shell element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The runner's step validation, cascaded by <see cref="TmSigningFormRunner"/> (absent for standalone steps).</summary>
    [CascadingParameter(Name = "TmSigningRunnerValidation")]
    private string? RunnerValidationMessage { get; set; }

    private IReadOnlyList<SigningField> CheckboxFields => Fields.Count > 0 ? Fields : [Field];

    private bool IsCheckboxGroup => Field.Type == SigningFieldType.Checkbox && CheckboxFields.Count > 1;

    private string SingleValue => Value?.ToString() ?? string.Empty;

    private bool BoolValue => Value is bool boolValue && boolValue;

    private HashSet<string> SelectedValues => Value switch
    {
        IEnumerable<string> strings => strings.ToHashSet(StringComparer.Ordinal),
        string text when !string.IsNullOrWhiteSpace(text) => [text],
        _ => []
    };

    private string ShellClass => string.Join(" ", new[] { "tm-signing-choice-step", Class }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private bool HasDescription => !string.IsNullOrWhiteSpace(SigningTextResolver.FieldDescription(Field, Culture, FallbackCulture));

    /// <summary>The message the shell renders as the step's single alert (own message first, then the runner's).</summary>
    private string? EffectiveValidationMessage => !string.IsNullOrWhiteSpace(_validationMessage)
        ? _validationMessage
        : RunnerValidationMessage;

    private bool HasValidationMessage => !string.IsNullOrWhiteSpace(EffectiveValidationMessage);

    private string InvalidText => BoolText(HasValidationMessage);

    /// <summary>
    /// The ids a control of this step references — per-instance, and the SAME instances the shell
    /// renders (they are passed to the shell as parameters), so every reference resolves inside
    /// the rendered DOM (Signing redesign phase 2). The checkbox-group hint (id <see cref="_hintId"/>)
    /// is part of the group's description.
    /// </summary>
    private string? DescribedBy => JoinIds(HasDescription ? _descriptionId : null, HasValidationMessage ? _validationId : null);

    /// <summary>The describedby list for the checkbox-group fieldset: hint + description + validation.</summary>
    private string? GroupDescribedBy => JoinIds(
        IsCheckboxGroup ? _hintId : null,
        HasDescription ? _descriptionId : null,
        HasValidationMessage ? _validationId : null);

    private static string? JoinIds(params string?[] ids)
    {
        var parts = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    /// <summary>The visually hidden legend naming the fieldset group.</summary>
    private string GroupLegendText => GetFieldLabel(Field);

    private async Task HandleSingleValueChangedAsync(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        _validationMessage = Field.Required && string.IsNullOrWhiteSpace(value)
            ? RequiredChoiceMessage
            : null;
        await ValueChanged.InvokeAsync(value);
    }

    private async Task HandleMultipleValueChangedAsync(string optionUuid, ChangeEventArgs args)
    {
        var values = SelectedValues;
        if (ToBool(args.Value))
        {
            values.Add(optionUuid);
        }
        else
        {
            values.Remove(optionUuid);
        }

        _validationMessage = Field.Required && values.Count == 0 ? RequiredChoiceMessage : null;
        await ValueChanged.InvokeAsync(values.ToArray());
    }

    private async Task HandleCheckboxChangedAsync(ChangeEventArgs args)
    {
        var value = ToBool(args.Value);
        _validationMessage = Field.Required && !value ? RequiredChoiceMessage : null;
        await ValueChanged.InvokeAsync(value);
    }

    private async Task HandleGroupCheckboxChangedAsync(SigningField checkboxField, ChangeEventArgs args)
    {
        var values = SelectedValues;
        if (ToBool(args.Value))
        {
            values.Add(checkboxField.Uuid);
        }
        else
        {
            values.Remove(checkboxField.Uuid);
        }

        _validationMessage = CheckboxFields.Any(item => item.Required) && values.Count == 0 ? RequiredChoiceMessage : null;
        await ValueChanged.InvokeAsync(values.ToArray());
    }

    private bool IsGroupCheckboxChecked(SigningField field) => SelectedValues.Contains(field.Uuid);

    private static bool ToBool(object? value)
    {
        return value is bool boolValue && boolValue
            || bool.TryParse(value?.ToString(), out var parsed) && parsed;
    }

    private string RequiredChoiceMessage => SigningLocalizationResolver.ResolveValidationMessage(Field.Validation, Culture, FallbackCulture, Loc["TmSigningStep_RequiredChoice"]);

    private string GetOptionLabel(SigningFieldOption option)
    {
        return SigningTextResolver.OptionLabel(option, Culture, FallbackCulture);
    }

    private string GetFieldLabel(SigningField field)
    {
        return SigningTextResolver.FieldLabel(field, Culture, FallbackCulture, Loc);
    }
}
