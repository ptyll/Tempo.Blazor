using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Components.Forms;

namespace Tempo.Blazor.FluentValidation;

/// <summary>
/// What a <see cref="EditContext.NotifyFieldChanged(in FieldIdentifier)"/> notification triggers.
/// </summary>
public enum FieldChangedValidation
{
    /// <summary>Validate only the changed member (the default, pre-existing behaviour).</summary>
    Member,

    /// <summary>
    /// Re-run the full-model pipeline (default rule sets / context). Use when the validator's rules are
    /// model-level (e.g. <c>RuleFor(x =&gt; x).Custom(...)</c>) so a member-scoped run would not reach them.
    /// </summary>
    FullModel,

    /// <summary>Do nothing on field change; validation runs only on request or explicitly.</summary>
    None,
}

/// <summary>
/// Programmatic options for <see cref="EditContextFluentValidationExtensions.AddFluentValidation(EditContext, IServiceProvider, FluentValidationOptions)"/>:
/// control over the validated model, rule-set selection, validation-context preparation (RootContextData),
/// failure-to-field mapping and message rendering. Every member is optional — an empty options instance
/// reproduces the parameterless behaviour.
/// </summary>
public sealed class FluentValidationOptions
{
    /// <summary>
    /// Supplies the model to validate. Defaults to the <see cref="EditContext"/>'s own model. Returning
    /// <see langword="null"/> skips the run (local messages are cleared) — useful while a page's data has
    /// not loaded yet.
    /// </summary>
    public Func<object?>? ModelProvider { get; set; }

    /// <summary>
    /// The rule sets included in a full-model run (composed with the member filter on a member-scoped run).
    /// Evaluated per run, so the selection may depend on mutable page state. Null/empty = default rules.
    /// </summary>
    public Func<IReadOnlyCollection<string>?>? RuleSets { get; set; }

    /// <summary>
    /// Called with the FluentValidation context before each run — the hook for
    /// <see cref="IValidationContext.RootContextData"/> (e.g. a workflow/validation context object).
    /// </summary>
    public Action<IValidationContext>? PrepareContext { get; set; }

    /// <summary>
    /// Maps a failure to the <see cref="FieldIdentifier"/> whose message store entry it becomes. Defaults to
    /// <c>editContext.Field(failure.PropertyName)</c>; returning <see langword="null"/> falls back to that default.
    /// Use to strip collection paths (<c>Items[3].Name</c> → <c>Name</c>) or otherwise re-key failures.
    /// </summary>
    public Func<ValidationFailure, FieldIdentifier?>? FieldMapper { get; set; }

    /// <summary>
    /// Renders the message text stored (and displayed) for a failure. Defaults to
    /// <see cref="ValidationFailure.ErrorMessage"/>. Use to resolve resource keys to localized text.
    /// </summary>
    public Func<ValidationFailure, string>? MessageFormatter { get; set; }

    /// <summary>Field-changed behaviour; <see cref="FieldChangedValidation.Member"/> by default.</summary>
    public FieldChangedValidation OnFieldChanged { get; set; } = FieldChangedValidation.Member;

    /// <summary>
    /// Opt-in; <see langword="false"/> by default. When <see langword="true"/>, editing a field drops the
    /// failures injected through <see cref="FluentValidationSubscription.SetExternalFailures"/> (for example a
    /// server 422 response) that landed on that field, so a stale server message does not stay under a value
    /// the user has already changed. The comparison uses the mapped <see cref="FieldIdentifier"/> (after
    /// <see cref="FieldMapper"/>), not the bare property name; failures on other fields are kept, and the
    /// clear happens before the local re-validation (whatever <see cref="OnFieldChanged"/> is set to). A
    /// later <see cref="FluentValidationSubscription.SetExternalFailures"/> call replaces the layer and brings
    /// the field's failure back. Default <see langword="false"/> keeps external failures until replaced or cleared.
    /// </summary>
    public bool ClearExternalFailureOnFieldChange { get; set; }
}
