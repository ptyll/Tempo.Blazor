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
French `_One`, `_Many`, `_Other`, Czech all four. The plain `Key` is used only when no family exists
anywhere. A host that overrides only the plain key does **not** keep working when it
delegates to the built-in localizer (the documented `PartialOverrideLocalizer` /
`MyPartialLocalizer` pattern): the default `TryGetString` reads `Key_One` through the indexer, the
inner localizer returns the shipped family, and the plain-key override is bypassed. A delegating
localizer must override the family keys — `_One` / `_Other`, plus `_Few` / `_Many` where its
language needs them. A family must show the count (`{0}`) in every form; a unit test enforces that
and that all three languages provide every family.

```csharp
// before — bypassed. Plural asks for Key_One, the indexer forwards it, the shipped family wins.
public string this[string key] => key == "TmModelingIssuePanel_Count" ? "Nálezy: {0}" : inner[key];

// after — override the family the lookup actually reads.
public string this[string key] => key switch
{
    "TmModelingIssuePanel_Count_One" => "{0} nález",
    "TmModelingIssuePanel_Count_Few" => "{0} nálezy",
    "TmModelingIssuePanel_Count_Many" => "{0} nálezu",
    "TmModelingIssuePanel_Count_Other" => "{0} nálezů",
    _ => inner[key],
};
```

### Which culture

`ITmLocalizer.Culture` (default `CultureInfo.CurrentUICulture`) is the UI culture: it picks the plural
rules and the resource pattern. It does not format the number. The implementation formats `{0}` with
its own culture — `CurrentCulture` in the default localizer — so a host that separates the two keeps
its number format. A null `Culture` (what a mocking proxy returns) falls back to `CurrentUICulture`.
A localizer bound to one fixed language — a test double, a per-tenant localizer — overrides
`Culture`. `MockTmLocalizer` in the test base is bound to `en` or `cs`.

`ITmLocalizer.TryGetString(key, out value)` is a default interface member that tells "translated"
from "missing" without relying on the key text; `DefaultTmLocalizer` overrides it precisely. A host
localizer written before plurals existed still compiles. Two caveats: a class that already declares a
public `Culture` or `TryGetString` now implicitly implements the new members (a signature mismatch
will not compile), and a mocking proxy returns null for the unimplemented `Culture` — `Plural` and
the relative-time formatter treat that null as `CurrentUICulture` instead of throwing.

### Reserved suffixes

`_One`, `_Few`, `_Many` and `_Other` are reserved. A key that ends in one of them is read as a plural
form, so a new resource must not use them for anything else. Two existing keys predate the
reservation and are excluded from the parity guard: `TmDiagramProperties_Arrowhead_*` and
`TmDiagramProperties_Cardinality_*` (labels such as "Many", not counts). Do not add to that list.

`TmTimeline_JustNow`, `TmTimeline_MinutesAgo`, `TmTimeline_HoursAgo`, `TmTimeline_DaysAgo` and
`TmTimeline_DateFormat` are deprecated. The library no longer reads them — relative time goes through
`ITmRelativeTimeFormatter` and `Tm_Time_*`. The resources remain so a host that reads them is not
broken; new code must not.

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
  clock that is a few seconds ahead never prints "in 0 min". A future minute or hour **rounds up**
  (4m59s is "in 5 min", not "in 4 min", which would read as already elapsed); the past keeps
  truncating.
- **Capitalisation.** "Just now" / "Právě teď" / "À l'instant" stay sentence case: they open the
  stamp. Everything else is lower case, including the day words — the resource is "yesterday at {0}",
  not "Yesterday at {0}" ("yesterday at 4:20 PM", "5 min ago", "před 5 min"). Do not capitalise the
  in-sentence forms to match the opening stamps, and do not lowercase the opening stamps either.
- **Time zone.** "Yesterday" is a calendar-day judgement made in the zone you pass (default: the
  clock's local zone), from the *instant* — the offset a `DateTimeOffset` happens to carry is
  irrelevant. A component that knows the user's zone (the Scheduler's `ResolveTimeZone()`) passes it.
  No component takes a time-zone parameter for this: the zone comes from the clock.
- **Clock and lifetime.** `AddTempoBlazor()` registers `ITmRelativeTimeFormatter` as **scoped**, over
  the `ITmLocalizer` and `TimeProvider` of the current scope (the system clock when no `TimeProvider`
  is registered). Scoped, not singleton: a singleton would capture the first localizer, so a scoped
  localizer either throws under `ValidateScopes` or every circuit shares the first language. Tests
  register a `FakeTimeProvider` (`Microsoft.Extensions.Time.Testing`) or pass `now` explicitly — never
  `DateTime.Now`.
- **Blazor Server / prerender: the user's zone.** `TimeProvider.System.LocalTimeZone` is the
  *server's* zone, so "yesterday at 16:20" is wrong for a circuit whose browser is elsewhere. The host
  registers a scoped `ITmRelativeTimeFormatter` over its own user `TimeProvider`; it never replaces the
  app-wide `TimeProvider`. Kestrel (`PinnedBlockMemoryPoolFactory`) and cookie authentication
  (`PostConfigureAuthenticationSchemeOptions`) are singletons that consume `TimeProvider`, so
  `AddScoped<TimeProvider>` fails the host at startup under `ValidateScopes` ("Cannot consume scoped
  service 'System.TimeProvider' from singleton") and, without validation, makes the user's zone drive
  the server clocks too. Read the IANA id once per circuit
  (`Intl.DateTimeFormat().resolvedOptions().timeZone`), resolve it with
  `TimeZoneInfo.FindSystemTimeZoneById`, and fall back to UTC when the id is unknown. The zone arrives
  from JS after the first render, so `UserTimeProvider` reads `CircuitZoneCache.Zone` lazily — capturing
  it when the clock is first resolved leaves the circuit on UTC. Re-render once the zone arrives (the
  stamp is computed at render time; nothing ticks it). There is no component parameter for this; a
  shared `ITmUserTimeZone` (one browser read, reused by Scheduler and Gantt) is a recorded follow-up,
  not part of this registration.

  ```csharp
  // doc-sample:user-zone
  builder.Services.AddTempoBlazor();
  builder.Services.AddScoped<CircuitZoneCache>();
  builder.Services.AddScoped<ITmRelativeTimeFormatter>(sp => new TmRelativeTimeFormatter(
      sp.GetRequiredService<ITmLocalizer>(),
      new UserTimeProvider(sp.GetRequiredService<CircuitZoneCache>())));

  sealed class UserTimeProvider(CircuitZoneCache cache) : TimeProvider
  {
      // Read lazily: the zone arrives from JS after the first render.
      public override TimeZoneInfo LocalTimeZone => cache.Zone ?? TimeZoneInfo.Utc;
  }
  ```
- **Strings.** `Tm_Time_JustNow`, `Tm_Time_MinutesAgo`, `Tm_Time_HoursAgo`, `Tm_Time_InMinutes`,
  `Tm_Time_InHours`, `Tm_Time_YesterdayAt`, `Tm_Time_TomorrowAt`, the plural families
  `Tm_Time_DaysAgo_*` / `Tm_Time_InDays_*`, and two format strings `Tm_Time_TimeFormat` (the clock
  time inside "yesterday at …") and `Tm_Time_DateFormat` (the absolute date). The formats are
  resources because locale conventions differ from the .NET culture default (`H:mm`, not `HH:mm`,
  in Czech).
- **No live refresh.** The stamp is computed when the component renders. Nothing ticks it forward from
  "5 min ago" to "6 min ago"; the host re-renders (a new item, a circuit refresh) and the formatter
  reads the clock again.
- **Replacing it.** Register your own `ITmRelativeTimeFormatter` after `AddTempoBlazor()`.

A component resolves it through `TmRelativeTime.Resolve(Services, Loc)`, which falls back to the
default formatter when nothing is registered (a bUnit context with only an `ITmLocalizer`), so the
component never throws for a missing registration.
