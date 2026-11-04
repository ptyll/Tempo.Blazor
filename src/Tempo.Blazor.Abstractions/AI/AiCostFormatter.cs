using System.Globalization;

namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Formats an agent run's cost. The model stores MICRO-UNITS (<see cref="AgentRunNode.CostMicroUnits"/>):
/// one million micro-units is 1.00 of the currency, the smallest unit an LLM bill is usually quoted in, so
/// a run whose individual calls cost a fraction of a cent keeps its precision instead of rounding to zero.
/// <para>
/// The default format is the culture's own currency ("$1.50", "1,50 Kč", "￥1,500"), because a reader
/// comparing a run's cost with their plan's price thinks in money, not in micro-units. The number of decimals
/// is the currency's own (<see cref="NumberFormatInfo.CurrencyDecimalDigits"/>): two for a dollar or a koruna,
/// none for a yen. Below one hundredth of a unit the formatter keeps six decimals: a plain currency format
/// would paint every cheap call as "0.00", and a column of zeros is worse than no column.
/// </para>
/// <para>
/// A host with its own price model (credits, a fixed exchange rate) overrides the presentation with the
/// component's <c>CostFormatter</c> parameter; this helper is the default a host gets for free.
/// </para>
/// </summary>
public static class AiCostFormatter
{
    /// <summary>How many micro-units make one whole unit of the currency.</summary>
    public const long MicroUnitsPerUnit = 1_000_000;

    /// <summary>Decimal places kept for an amount below one hundredth of a unit (the micro-unit
    /// precision).</summary>
    public const int PreciseDecimals = 6;

    /// <summary>
    /// Formats <paramref name="microUnits"/> as a currency amount in <paramref name="provider"/>'s culture.
    /// </summary>
    /// <param name="microUnits">The amount in micro-units (1_000_000 is 1.00).</param>
    /// <param name="provider">The culture (and therefore the currency symbol, the decimal separator, the
    /// symbol's position and the currency's decimal places). Null uses
    /// <see cref="CultureInfo.CurrentCulture"/>.</param>
    /// <returns>The amount, e.g. <c>$1.50</c> or <c>0,001234 Kč</c>.</returns>
    public static string Format(long microUnits, IFormatProvider? provider = null)
        => Format(microUnits / (decimal)MicroUnitsPerUnit, provider);

    /// <summary>
    /// Formats an amount already expressed in whole units.
    /// </summary>
    /// <param name="units">The amount in whole units (1.50 is one and a half).</param>
    /// <param name="provider">The culture. Null uses <see cref="CultureInfo.CurrentCulture"/>.</param>
    /// <returns>The amount in the culture's currency, with the currency's own decimal places — or six below
    /// one hundredth of a unit.</returns>
    public static string Format(decimal units, IFormatProvider? provider = null)
    {
        // Any IFormatProvider is honoured, not just a CultureInfo: a host with a custom
        // NumberFormatInfo (a fixed exchange rate, a credit symbol) must be able to format through it.
        var effective = provider ?? CultureInfo.CurrentCulture;
        var decimals = units != 0m && Math.Abs(units) < 0.01m
            ? PreciseDecimals
            // "Two decimals" is the rule of a two-decimal currency, not of every currency: a yen amount has no
            // minor unit, and forcing two would invent precision the bill does not have.
            : NumberFormatInfo.GetInstance(effective).CurrencyDecimalDigits;

        return units.ToString(
            "C" + decimals.ToString(CultureInfo.InvariantCulture),
            effective);
    }
}
