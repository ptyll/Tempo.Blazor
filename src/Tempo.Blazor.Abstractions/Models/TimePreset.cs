namespace Tempo.Blazor.Models;

/// <summary>
/// A named time-of-day shortcut for <c>TmTimeSelector</c> and <c>TmTimePicker</c>.
/// A <see langword="null"/> <see cref="Label"/> means the host wants the formatted time shown as the chip text.
/// </summary>
/// <param name="Label">Display label; <see langword="null"/> falls back to the formatted <see cref="Time"/>.</param>
/// <param name="Time">The time the preset commits.</param>
public sealed record TimePreset(string? Label, TimeOnly Time);
