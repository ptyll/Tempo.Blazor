namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The shape of a validation rule's presentation keys: every rule key (<see cref="DocumentValidationKeys"/>,
/// <see cref="Templating.TemplateValidationResult.SyntaxErrorKey"/>) is one sentence, and the dialog that lists
/// findings shows it as a short TITLE plus a DESCRIPTION that says what it means and how to fix it.
/// </summary>
/// <remarks>
/// The suffixes are not localization keys themselves, so they deliberately live outside the <c>*Keys</c> classes
/// that <c>TmEmailResourcesParityTests</c> scans for key constants.
/// </remarks>
public static class ValidationKeyShape
{
    /// <summary>The suffix of a rule's title key: <c>validation.button.href_missing_Title</c>.</summary>
    public const string TitleSuffix = "_Title";

    /// <summary>The suffix of a rule's description key: <c>validation.button.href_missing_Description</c>.</summary>
    public const string DescriptionSuffix = "_Description";

    /// <summary>The title key of <paramref name="ruleKey"/>: a short name for the finding.</summary>
    /// <param name="ruleKey">The rule key (for example <c>validation.button.href_missing</c>).</param>
    /// <returns>The title key.</returns>
    public static string TitleOf(string ruleKey) => ruleKey + TitleSuffix;

    /// <summary>The description key of <paramref name="ruleKey"/>: what the finding means and how to fix it.</summary>
    /// <param name="ruleKey">The rule key (for example <c>validation.button.href_missing</c>).</param>
    /// <returns>The description key.</returns>
    public static string DescriptionOf(string ruleKey) => ruleKey + DescriptionSuffix;
}
