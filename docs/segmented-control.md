# Segmented control (convention, not a component)

There is no `TmSegmentedControl`. Two existing components already cover the two jobs a segmented
control is asked to do, and a third component would only duplicate them. Pick by what the control
changes:

| The segments change… | Use | Why |
| --- | --- | --- |
| **which content is shown** (a view, a panel, a page region) | `TmTabs Variant="TabVariant.Pill"` | The selection owns panels, so it needs the tab semantics (`tablist` / `tab` / `tabpanel`) and the roving tabindex `TmTabs` already implements. |
| **a value** (Desktop/Tablet/Mobile, MJML/JSON, an alignment) | `TmRadioGroup Appearance="RadioAppearance.Segmented"` | The selection is one value out of a set, so it stays a `radiogroup`. Each segment is a native `<button role="radio">`, which keeps the one-activation rule: Enter and Space fire exactly once. |

```razor
@* content *@
<TmTabs Variant="TabVariant.Pill" @bind-ActiveTabId="view">
    <TmTabPanel Id="preview" Title="Preview">…</TmTabPanel>
    <TmTabPanel Id="source" Title="Source">…</TmTabPanel>
</TmTabs>

@* a value *@
<TmRadioGroup T="string"
              Appearance="RadioAppearance.Segmented"
              Options="devices"
              @bind-Value="device" />
```

`Appearance` defaults to `RadioAppearance.Radio`, so an existing radio group is unchanged. A new
component waits until a third, genuinely different need appears.
