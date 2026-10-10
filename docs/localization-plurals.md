# Plurals and relative time

Two shared pieces of localization that no component should re-implement: grammatical **plurals**
(`Loc.Plural`) and **relative time** (`ITmRelativeTimeFormatter`).

## Plurals: `Loc.Plural("Key", n)`

"1 čekajících změn" is wrong Czech. `Loc.Plural` picks the grammatical form of the count from the
Unicode CLDR plural rules (`TmPluralRules.Select(culture, n)`) of the localizer's culture and reads it from a family of resource keys:

| Suffix | Czech / Slovak | French | English / default |
|--------|----------------|--------|-------------------|
| `Key_One` | 1 | 0 and 1 (and 1.5) | 1 |
| `Key_Few` | 2–4 | — | — |
| `Key_Many` | fractions (0.5, 2.5) | whole millions | — |
| `Key_Other` | 0, 5+ | everything else | everything else |

Polish (`pl`) is also supported (2–4 except 12–14 are `Few`, other integers `Many`). Any other
language uses the English rules. A region (`cs-CZ`, `fr-CA`) uses its language's rules.

```json
"TmModelingIssuePanel_Count_One":   "{0} nález",
"TmModelingIssuePanel_Count_Few":   "{0} nálezy",
"TmModelingIssuePanel_Count_Many":  "{0} nálezu",
"TmModelingIssuePanel_Count_Other": "{0} nálezů"
```

```razor
<span>@Loc.Plural("TmModelingIssuePanel_Count", Issues.Count)</span>   @* 1 nález · 3 nálezy · 7 nálezů *@
```

The count is always template argument `{0}`; further arguments follow it:
`Loc.Plural("Found", n, folderName)` → `{0}` is `n`, `{1}` is `folderName`.

### Lookup order and fallback

1. the key for the count's category (`Key_Few`);
2. `Key_Other`;
3. the plain `Key`;
4. otherwise whatever the indexer returns for a missing key.

So a language only has to provide the forms its grammar needs — English ships `_One` and `_Other`,
French `_One`, `_Many`, `_Other`, Czech all four — and a host that overrides only the plain `Key`
keeps working. A family must show the count (`{0}`) in every form; a unit test enforces that and
that all three languages provide every family.

### Which culture

`ITmLocalizer.Culture` (default `CultureInfo.CurrentUICulture`, the culture the built-in JSON
localizer resolves strings with) selects the rules, and also formats the count (`1,5` in Czech).
A localizer bound to one fixed language — a test double, a per-tenant localizer — overrides
`Culture`. `MockTmLocalizer` in the test base is bound to `en` or `cs`.

`ITmLocalizer.TryGetString(key, out value)` is a default interface member that tells "translated"
from "missing" without relying on the key text; `DefaultTmLocalizer` overrides it precisely. A host
localizer written before plurals existed needs no change.

### Adding a counted text

1. Add `Key_One` / `Key_Other` to `TmResources.json`, `Key_One/_Few/_Many/_Other` to
   `TmResources.cs.json`, `Key_One/_Many/_Other` to `TmResources.fr.json`.
2. Add the same keys to both maps in `LocalizationTestBase` (`BuildEnglishLocalizer`,
   `BuildCzechLocalizer`).
3. Use `Loc.Plural("Key", n)`.

Avoid the numeral-agreement trap instead of using a plural only when you cannot: a label like
"Položky: {0}" needs no plural, but a sentence that counts does. The B16 stop-gap texts
("Čekající změny: N") were replaced by real plural families.

## Relative time: `ITmRelativeTimeFormatter`

One implementation for every component — `TmTimeline`, `TmActivityTimeline` and
`TmActivityComments` no longer carry private copies.

```csharp
public interface ITmRelativeTimeFormatter
{
    string Format(DateTimeOffset value, DateTimeOffset? now = null, TimeZoneInfo? timeZone = null);
}
```

| Distance from now | Czech | English | French |
|-------------------|-------|---------|--------|
| under a minute (either direction) | Právě teď | Just now | À l'instant |
| under an hour | před 5 min / za 5 min | 5 min ago / in 5 min | il y a 5 min / dans 5 min |
| same calendar day | před 3 h / za 3 h | 3 h ago / in 3 h | il y a 3 h / dans 3 h |
| previous / next calendar day | včera v 16:20 / zítra v 9:00 | yesterday at 4:20 PM / tomorrow at 9:00 AM | hier à 16:20 / demain à 09:00 |
| 2–6 days | před 2 dny · před 5 dny / za 5 dní | 2 days ago / in 5 days | il y a 2 jours / dans 5 jours |
| a week or more | 5. 10. 2026 | Oct 5, 2026 | 5 oct. 2026 |

- **Future times** are handled (`za 3 h`, `zítra v 9:00`); the sub-minute window is symmetric so a
  clock that is a few seconds ahead never prints "in 0 min".
- **Time zone.** "Yesterday" is a calendar-day judgement made in the zone you pass (default: the
  clock's local zone), from the *instant* — the offset a `DateTimeOffset` happens to carry is
  irrelevant. A component that knows the user's zone (the Scheduler's `ResolveTimeZone()`) passes it.
- **Clock.** `AddTempoBlazor()` registers the default `TmRelativeTimeFormatter` over the registered
  `TimeProvider` (the system clock when none is). Tests register a `FakeTimeProvider`
  (`Microsoft.Extensions.Time.Testing`) or pass `now` explicitly — never `DateTime.Now`.
- **Strings.** `Tm_Time_JustNow`, `Tm_Time_MinutesAgo`, `Tm_Time_HoursAgo`, `Tm_Time_InMinutes`,
  `Tm_Time_InHours`, `Tm_Time_YesterdayAt`, `Tm_Time_TomorrowAt`, the plural families
  `Tm_Time_DaysAgo_*` / `Tm_Time_InDays_*`, and two format strings `Tm_Time_TimeFormat` (the clock
  time inside "yesterday at …") and `Tm_Time_DateFormat` (the absolute date). The formats are
  resources because locale conventions differ from the .NET culture default (`H:mm`, not `HH:mm`,
  in Czech).
- **Replacing it.** Register your own `ITmRelativeTimeFormatter` after `AddTempoBlazor()`.

A component resolves it through `TmRelativeTime.Resolve(Services, Loc)`, which falls back to the
default formatter when nothing is registered (a bUnit context with only an `ITmLocalizer`), so the
component never throws for a missing registration.
