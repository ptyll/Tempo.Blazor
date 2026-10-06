# Scoped CSS ownership

A Blazor scoped stylesheet (`Component.razor.css`) rewrites every selector to
`.class[b-xxxx]`, where `b-xxxx` is the scope attribute of **the component that owns the
file**. A rule whose class is rendered by a child component never matches: the child carries a
different scope attribute, the rule applies to nothing, and neither the build nor bUnit says so.

## The rule

The key class of a selector — the last compound, the element the declaration actually paints —
must be rendered by the component that owns the stylesheet. When it is not, either:

- add `::deep` (or `:deep()`), which opts the selector out of the rewrite, or
- move the rule into the stylesheet of the component that renders the class, or into the global
  `wwwroot/css/components/` sheet.

```css
/* BAD — .tm-child__row is rendered by the child, so this never applies. */
.tm-child__row { padding: 4px; }

/* GOOD — explicit opt-out. */
::deep .tm-child__row { padding: 4px; }
```

## What the test checks

`ScopedCssOwnershipTests` scans every `*.razor.css` under `src/`. For each selector that does
not use `::deep` / `:deep()`, it takes the classes of the key compound and reports any class
that the owner does not render and some other component does. A class rendered by nobody is
reported by nothing — it may be a typo, but it is not this bug.

The current findings are frozen in
`tests/Tempo.Blazor.Tests/Theme/scoped-css-ownership-baseline.txt`. The baseline may only
shrink: a new foreign class fails the test, and deleting a fixed entry is part of the fix.
Today's rows are Gantt sub-component rules (the Gantt plan moves them), Notion table, editor
and column rules, and one Modeling rule.

## What the heuristic cannot see

Classes built by concatenation (`"tm-gantt__row" + modifier`) or held in a variable are
invisible to the scan, so a rule can be foreign without being reported. The heuristic is a
floor, not a proof. When you add a scoped rule for a class you did not write in the same
`.razor` file, check it by hand.
