namespace Tempo.Blazor.Localization;

/// <summary>
/// The CLDR plural categories Tempo.Blazor resource keys are written for. Each maps to a key suffix
/// (<c>Key_One</c>, <c>Key_Few</c>, <c>Key_Many</c>, <c>Key_Other</c>). English has One and Other;
/// Czech One, Few, Many and Other; French One, Many and Other.
/// </summary>
public enum TmPluralCategory
{
    /// <summary>The general form; also the form every language falls back to.</summary>
    Other = 0,

    /// <summary>Singular (1; in French also 0 and 1.5).</summary>
    One,

    /// <summary>Paucal (Czech 2–4, Polish 2–4 except 12–14).</summary>
    Few,

    /// <summary>Czech and Slovak fractions; Polish 5+ without the teens exception; French millions.</summary>
    Many,
}