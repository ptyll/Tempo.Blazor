# Email template syntax (Scriban)

Tempo.Blazor email templates use [Scriban](https://github.com/scriban/scriban) for variable
substitution. Expressions live in `{{ … }}`; everything else is emitted verbatim. The engine runs in
a **sandbox** (`ScribanTemplateEngine` + `TemplateSecurityOptions`): `include` is disabled, .NET
reflection is not exposed, and loops/recursion/output/time are bounded.

> Variable access is **snake_case**. A C# property `FirstName` (or a JSON field `firstName`) is
> referenced as `{{ first_name }}`.

---

## Variables

```
{{ name }}
{{ user.email }}
{{ order.customer.name }}
{{ items[0].product_name }}
```

Missing variables render as empty text by default (lenient mode). With
`TemplateSecurityOptions.StrictVariables = true`, referencing an undefined variable is an error.

---

## Escaping (HTML body)

The **output of every expression** in the HTML/MJML body is HTML-encoded. A value can therefore never
open a tag, an attribute or a script — no matter what the data contains:

```
{{ name }}                  data: <script>alert(1)</script>
                            renders: &lt;script&gt;alert(1)&lt;/script&gt;

{{ url }}  in href="…"      data: ?a=1&b=2      renders: ?a=1&amp;b=2
```

What is **not** encoded:

- the literal text of the template (markup you author yourself),
- the **subject**, the **preheader** and the **plain-text version** (they are text, not HTML),
- filter **input** — filters always see the raw value, so `{{ name | string.size }}` measures the raw
  string and only its printed result is encoded.

The mode is owned by the render pipeline; it is not configurable by the host (there is no
"disable escaping" switch on `TemplateRenderOptions`).

### Trusted HTML — `html.trusted`

When a value really is markup (a rich-text field your application already produced), opt out with the
single available filter:

```
{{ body_html | html.trusted }}
```

Use it in **element content**, never in an attribute — a sanitized value carries no quotes (the
sanitizer serializes them as entities), so it cannot break out of an attribute, but markup belongs in
content by design. Its output is **always** passed through the HTML sanitizer first, so the tag/attribute
allow-list still applies: `<b>`, `<i>`, `<u>`, `<strong>`, `<em>`, `<a>` (with a scheme-checked `href`), `<br>`,
`<span>`, `<p>`, `<ul>`, `<ol>`, `<li>`; scripts, styles, event handlers and unsafe URL schemes are
stripped. Markup outside the allow-list is unwrapped to its text.

### `html.escape`

`{{ value | html.escape }}` encodes a value explicitly. The result is written verbatim (it is already
encoded), so it is **not** encoded a second time.

**Filter order:** `html.escape` and `html.trusted` are **terminal** — put them last in the pipe. A string
filter after them converts the marker back to plain text, so the value is encoded a second time
(`{{ x | html.escape | string.upcase }}` is not "escape then uppercase"; write
`{{ x | string.upcase | html.escape }}`).

> **Limitation:** `{{ capture … }}…{{ end }}` captures *already encoded* output, so printing the
> captured variable encodes it again (a literal `&amp;` becomes `&amp;amp;`). Build repeated markup
> with a `for` loop or a filter instead of `capture`.

---

## Links and URL schemes

Every URL that reaches the message is checked **after substitution** — no matter whether it came from
a template expression or straight from the block model (button, image `src`/`href`, navbar link,
social element, carousel image, hero/wrapper/section background). The allow-list is fixed and cannot
be extended by a host:

| Allowed | Refused → replaced with `#` |
|---|---|
| `http:`, `https:`, `mailto:`, `tel:` | `javascript:`, `data:`, `vbscript:`, `file:`, `blob:`, unknown |
| relative paths (`/path`, `path`, `./path`, `../path`) | |
| a fragment (`#anchor`) | |

```
{{ url }}      data: javascript:alert(1)      renders: #
```

A refused URL is replaced by `#` and reported once per scheme as a **warning** `RenderError`
(`RenderErrorSeverity.Warning`) with the localization key `render.url.blocked_scheme`
(`RenderKeys.BlockedUrlScheme`); it is also audited as `TemplateSecurityEventKind.BlockedUrlScheme`,
whose detail names the scheme only, never the URL. The render still completes and `RenderResult.Success`
stays `true` — `RenderResult.HasWarnings` tells a host that a link was neutralised, so an application
that must not deliver a `#` link can decide not to send.

A **network path** is refused too, because "relative" means "no authority": `//cdn.example.com/x`,
`\\host\share`, and `/<backslash>host` would make a mail client resolve a remote (SMB) share. Write the
scheme explicitly (`https://cdn.example.com/x`). `cid:` (inline images) is deliberately **not** in the
allow-list yet — enabling it is a future contract change.

### Markup that can rewrite a link

An SVG animation element (`<set>`, `<animate>`, `<animateMotion>`, `<animateTransform>`, `<discard>`) and
every `<meta http-equiv="refresh">` are removed from the rendered message (warning
`render.markup.unsafe_element_removed`): their `href`/`to`/`from`/`by` values are applied by the browser
*after* the template has been checked, so no template can be allowed to carry them. CSS is checked on a
decoded copy as well, so a `u\72l(javascript:…)` escape, an `image-set(…)` or an `@import` is judged like a
plain `url(…)`; a stylesheet that still embeds a refused target is dropped. An `<!doctype>`/comment/tag
left unterminated at the end of a raw block ends the message (the browser drops it too).

### Event handlers are not template output

A variable inside an `on*` attribute or `<script>`/`<style>` is **not** supported: the browser decodes
the attribute value before it runs it as script, so escaping cannot make it safe. The renderer removes
every `on*` attribute and `srcdoc` from raw markup and reports a warning
(`RenderKeys.EventHandlerRemoved`). Keep behaviour in the template's structure (loops, `VisibleWhen`),
never in inline script.

---

## Filters

Filters transform values with the pipe `|` operator:

```
{{ name | string.upcase }}
{{ name | string.downcase }}
{{ name | string.capitalize }}
{{ name | string.truncate 50 }}
{{ name | object.default "Guest" }}
{{ text | string.replace "old" "new" }}
{{ csv  | string.split "," }}
{{ text | string.strip }}
{{ items | array.join ", " }}
{{ items.size }}
{{ created | date.to_string "%Y-%m-%d" }}
{{ price | math.format "N2" }}
{{ body_html | html.trusted }}
```

The full Scriban built-in function set is available under `string`, `array`, `object`, `math`,
`date`, `regex`, `html` and `timespan`. None of these touch the file system or network.

---

## Conditions

```
{{ if status == "active" }}
  Active
{{ else if status == "pending" }}
  Pending
{{ else }}
  Inactive
{{ end }}
```

Comparison: `==` `!=` `<` `>` `<=` `>=` — Logical: `&&` `||` `!`

Conditions and loops are control flow, not output: escaping never changes what they evaluate to.

---

## Loops

```
{{ for item in items }}
  {{ for.index }}: {{ item.name }}
{{ end }}
```

Loop helpers: `for.index` (0-based), `for.first`, `for.last`, `for.even`, `for.odd`.

> The loop variable (`item` above) is local to the loop and is **not** treated as a template
> variable. The iterated collection (`items`) is detected as a collection-typed variable.

---

## Block visibility (Tempo extension)

Any block can carry a `VisibleWhen` expression (a bare Scriban boolean, no braces). The generator
wraps the block in `{{ if <expr> }} … {{ end }}`, so it only renders when the expression is truthy.

---

## Sandbox limits (`TemplateSecurityOptions`)

| Option | Default | Purpose |
|---|---|---|
| `LoopLimit` | 5000 | Aborts runaway / infinite loops |
| `RecursiveLimit` | 100 | Aborts infinite recursion |
| `MaxOutputLength` | 5,000,000 | Caps rendered output size |
| `Timeout` | 5 s | Cooperative wall-clock limit |
| `StrictVariables` | false | Undefined variable → error when true |

`include` is always disabled (no template loader is configured) and no .NET types/reflection are
reachable from a template.

---

## Migrating a template written before 2.10.0

Before 2.10.0 a variable was inserted verbatim, so a template could carry markup in its data:

```text
before:  {{ description }}          data: <b>Hi</b>      → <b>Hi</b> (bold)
after:   {{ description }}                                → &lt;b&gt;Hi&lt;/b&gt; (literal text)
         {{ description | html.trusted }}                 → <b>Hi</b> (bold again, sanitized)
```

Use `html.trusted` only for values your application produced as trusted HTML; a plain text field must
stay without the filter.
