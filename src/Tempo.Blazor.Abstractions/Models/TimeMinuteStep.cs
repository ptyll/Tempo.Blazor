namespace Tempo.Blazor.Models;

/// <summary>
/// Allowed minute-step values for <c>TmTimeSelector</c> / <c>TmTimePicker</c>: the positive
/// divisors of 60 other than 60 itself (1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30).
/// </summary>
public static class TimeMinuteStep
{
    /// <summary>Returns <see langword="true"/> when <paramref name="minuteStep"/> is a permitted step.</summary>
    public static bool IsAllowed(int minuteStep)
        => minuteStep is > 0 and < 60 && 60 % minuteStep == 0;

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="minuteStep"/> is not
    /// a divisor of 60 in the open interval (0, 60). The default parameter name is <c>MinuteStep</c>
    /// so it matches the component parameter that calls this from <c>OnParametersSet</c>; a range
    /// picker can pass <c>EndMinuteStep</c> (or another name) instead.
    /// </summary>
    /// <param name="minuteStep">The candidate step in minutes.</param>
    /// <param name="paramName">
    /// The name reported on the exception. Defaults to <c>MinuteStep</c>; pass the caller's own
    /// parameter name when this is not the start-side step.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not an allowed step.</exception>
    public static void Validate(int minuteStep, string paramName = "MinuteStep")
    {
        if (!IsAllowed(minuteStep))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                minuteStep,
                $"{paramName} must be a positive divisor of 60 other than 60 (1, 2, 3, 4, 5, 6, 10, 12, 15, 20 or 30).");
        }
    }
}
