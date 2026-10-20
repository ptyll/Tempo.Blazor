# MJML 4 attribute parity checklist (E1.31)

Authoritative attribute specification for the Tempo.Blazor email template model. Drives the model
properties (E1.32), the generator emission (E2.24), the importer mapping (EI.6) and the property
panels (E7.3). Defaults are the MJML 4 reference defaults — model property defaults must match so
the generator can omit default-valued attributes for clean output.

Legend: **bold** = required for valid output / a11y. `default` shown after the attribute.
Common positional/colour values use MJML formats (`px`, `%`, hex/rgb/named colours, CSS shorthand).

> Engine note (see E0.9 findings): the default renderer is **Mjml.Net 4.15.0** (attribute parity
> re-verified after the phase-2 bump from 4.11.0 — no change). It supports every
> component below **except `mj-html-attributes`**, which the model still carries for lossless
> round-trip (held, not rendered). The generator must emit `mj-attributes` children with **explicit
> close tags** (self-closing breaks Mjml.Net's head parser).

---

## Global head (`EmailHeadStyles` / `TemplateStyles`, E1.34)

| Source | Field | Default |
|---|---|---|
| `mj-title` | Subject | "" |
| `mj-preview` | Preheader | "" |
| (lang attr on `mjml`) | Language | "cs" |
| `mj-breakpoint width` | Breakpoint | "480px" |
| `mj-font` | Fonts: list of (Name, Href) | empty |
| `mj-style [inline]` | Styles: list of (Css, Inline) | empty |
| `mj-attributes` | per-tag defaults + `mj-all` + `mj-class` defs (E1.35/E1.36) | empty |
| `mj-html-attributes` | selectors + attrs (E1.37, round-trip only) | empty |
| (mj-body) `width` | ContentWidth | "600px" |
| (mj-body) `background-color` | BackgroundColor | "#ffffff" |

---

## Layout

### Section (`mj-section`)
`background-color` none · `background-url` none · `background-position` "top center" ·
`background-position-x` none · `background-position-y` none · `background-repeat` "repeat" ·
`background-size` "auto" · `border` "none" · `border-top/right/bottom/left` none ·
`border-radius` none · `direction` "ltr" · `full-width` false (`full-width`/none) ·
`padding` "20px 0" · `padding-top/right/bottom/left` none · `text-align` "center" · `css-class` none.

### Column (`mj-column`)
`background-color` none · `border` none · `border-top/right/bottom/left` none ·
`border-radius` none · `inner-border` none · `inner-border-radius` none ·
`padding` none · `padding-top/right/bottom/left` none · `vertical-align` "top" ·
`width` (auto = container/ncols) · `css-class` none.

### Group (`mj-group`)
`background-color` none · `direction` "ltr" · `vertical-align` "top" · `width` (auto) · `css-class` none.
Holds columns.

### Wrapper (`mj-wrapper`)
Same attribute set as Section (`background-*`, `border*`, `padding*`, `text-align` "center",
`full-width` false). Holds sections.

---

## Content blocks

### Text (`mj-text`)
`color` "#000000" · `font-family` "Ubuntu, Helvetica, Arial, sans-serif" · `font-size` "13px" ·
`font-style` none · `font-weight` none · `line-height` "1" · `letter-spacing` none ·
`height` none · `text-decoration` none · `text-transform` none · `align` "left" ·
`container-background-color` none · `padding` "10px 25px" · `padding-*` none · `css-class` none.
Body = inline HTML content.

### Button (`mj-button`)
`background-color` "#414141" · `color` "#ffffff" · `font-family` (inherit) · `font-size` "13px" ·
`font-style` none · `font-weight` "normal" · `line-height` "120%" · `letter-spacing` none ·
`text-align` "center" (content) · `text-decoration` "none" · `text-transform` none ·
`href` none · `rel` none · `target` "_blank" · `align` "center" · `vertical-align` "middle" ·
`border` "none" · `border-top/right/bottom/left` none · `border-radius` "3px" ·
`inner-padding` "10px 25px" · `padding` "10px 25px" · `padding-*` none · `width` none · `height` none ·
`container-background-color` none · `css-class` none. Body = button label text.

### Image (`mj-image`)
**`src`** · **`alt`** "" (a11y warning if empty) · `href` none · `name` none · `rel` none ·
`target` "_blank" · `title` none · `align` "center" · `width` (auto) · `height` "auto" ·
`border` "0" · `border-top/right/bottom/left` none · `border-radius` none ·
`padding` "10px 25px" · `padding-*` none · `container-background-color` none ·
`fluid-on-mobile` none · `css-class` none.

### Divider (`mj-divider`)
`border-color` "#000000" · `border-style` "solid" · `border-width` "4px" · `width` "100%" ·
`align` "center" · `padding` "10px 25px" · `padding-*` none · `container-background-color` none · `css-class` none.

### Spacer (`mj-spacer`)
`height` "20px" · `padding` none · `container-background-color` none · `css-class` none.

### Raw (`mj-raw`)
No attributes. Body = verbatim HTML/MJML (NOT escaped). Carries `ContainsRawContent` document flag.

### Table (`mj-table`)
`align` "left" · `border` none · `cellpadding` "0" · `cellspacing` "0" · `color` "#000000" ·
`container-background-color` none · `font-family` "Ubuntu, Helvetica, Arial, sans-serif" ·
`font-size` "13px" · `line-height` "22px" · `padding` "10px 25px" · `padding-*` none ·
`role` none · `table-layout` "auto" · `width` "100%" · `css-class` none.
Model: rows → cells (text + optional align/colspan/rowspan), NOT raw HTML.

### Social (`mj-social`)
`align` "center" · `border-radius` "3px" · `color` "#333333" · `font-family` (inherit) ·
`font-size` "13px" · `font-style` none · `font-weight` none · `icon-size` "20px" ·
`icon-height` none · `icon-padding` "0" · `inner-padding` none · `line-height` "22px" ·
`mode` "horizontal" (horizontal/vertical) · `padding` "10px 25px" · `padding-*` none ·
`text-padding` "4px 4px 4px 0" · `text-decoration` "none" · `container-background-color` none · `css-class` none.

#### Social element (`mj-social-element`)
`name` (known network → icon) · `href` none · `src` (custom icon) · `srcset` none · `alt` "" ·
`title` none · `target` "_blank" · `rel` none · `icon-size` (inherit) · `icon-height` none ·
`icon-padding` none · `text-padding` none · `background-color` (per network) ·
`color` (inherit) · `border-radius` (inherit) · `font-*` (inherit). Body = label text.

### Hero (`mj-hero`)
`mode` "fluid-height" (fluid-height/fixed-height) · `height` "0px" · `background-color` "#ffffff" ·
`background-url` none · `background-width` none · `background-height` none ·
`background-position` "center center" · `border-radius` none · `padding` "0px" · `padding-*` none ·
`vertical-align` "top" · `width` (container) · `css-class` none. Holds blocks.

### Navbar (`mj-navbar`)
`align` "center" · `base-url` none · `hamburger` none · `ico-*` (icon-color/size/line-height/padding/align)
defaults per MJML · `padding` none · `css-class` none. Holds links.

#### Navbar link (`mj-navbar-link`)
`href` none · `rel` none · `target` "_blank" · `color` "#000000" · `font-family` (inherit) ·
`font-size` "13px" · `font-style` none · `font-weight` "normal" · `line-height` "22px" ·
`text-decoration` "none" · `text-transform` "uppercase" · `padding` "15px 10px" · `padding-*` none · `css-class` none.
Body = link text.

### Carousel (`mj-carousel`)
`align` "center" · `border-radius` "6px" · `icon-width` "44px" · `left-icon` (url) ·
`right-icon` (url) · `tb-border` none · `tb-border-radius` "6px" · `tb-hover-border-color` none ·
`tb-selected-border-color` none · `tb-width` none · `thumbnails` "visible" (visible/hidden) ·
`container-background-color` none · `css-class` none. Holds images.

#### Carousel image (`mj-carousel-image`)
**`src`** · `alt` "" · `href` none · `rel` none · `target` "_blank" · `title` none ·
`thumbnails-src` none · `css-class` none.

### Accordion (`mj-accordion`)
`border` "2px solid black" · `font-family` (inherit) · `icon-align` "middle" ·
`icon-height` "32px" · `icon-width` "32px" · `icon-position` "right" (left/right) ·
`icon-wrapped-url` (+/- icons) · `icon-unwrapped-url` · `icon-wrapped-alt` "+" · `icon-unwrapped-alt` "-" ·
`padding` none · `container-background-color` none · `css-class` none. Holds elements.

#### Accordion element (`mj-accordion-element`)
`background-color` none · `border` (inherit) · `font-family` (inherit) · `icon-*` (inherit).
Contains a title and a text.

#### Accordion title (`mj-accordion-title`)
`background-color` none · `color` none · `font-family` (inherit) · `font-size` "13px" ·
`padding` "16px" · `padding-*` none. Body = title text.

#### Accordion text (`mj-accordion-text`)
`background-color` none · `color` none · `font-family` (inherit) · `font-size` "13px" ·
`font-weight` none · `letter-spacing` none · `line-height` "1" · `padding` "16px" · `padding-*` none.
Body = content (inline HTML).

---

## Cross-cutting (E1.5 base, E1.33, E1.36)

- **`css-class`** — string of space-separated CSS class names (every element).
- **`mj-class`** — space-separated named classes referencing `<mj-attributes><mj-class>` defs.
- **`ExtraAttributes`** — `Dictionary<string,string>` on every element: any attribute not modelled
  above is captured here on import and re-emitted on export (lossless forward-compat).
- **`VisibleWhen`** — Tempo extension (not MJML): a Scriban boolean expression; the generator wraps
  the element in `{{ if <expr> }}…{{ end }}` (E2.17). Not an MJML attribute.
