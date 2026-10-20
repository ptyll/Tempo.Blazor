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

## Culture of formatters

Numbers and dates are formatted with an explicit culture, never with the server's thread culture:

```csharp
// per render (service / preview)
await renderer.RenderAsync(document, model, new TemplateRenderOptions { Culture = new CultureInfo("cs-CZ") });

// or let the document language decide
await renderer.RenderAsync(document, model, new TemplateRenderOptions { UseDocumentLanguageCulture = true });
```

| `TemplateRenderOptions` | Result |
|---|---|
| `Culture = cs-CZ` | `{{ price | math.format "N2" }}` → `1 234,50`, `{{ created | date.to_string "%d. %B %Y" }}` → `20. říjen 2026` |
| `Culture = null`, `UseDocumentLanguageCulture = true` | the culture of `document.Language` (`cs`, `en-US`, …) |
| `Culture = null`, `UseDocumentLanguageCulture = false` (default) | `CultureInfo.InvariantCulture` → `1,234.50`, `20. October 2026` |

Rules:

- An explicit `Culture` always wins over `UseDocumentLanguageCulture`.
- A document language that is empty or not a known culture falls back to the invariant culture — a
  render never fails because of it.
- The renderer resolves the culture **once** and applies it to every pass (subject, preheader, body,
  text version), so `{{ price }}` in the subject and in the body are formatted identically.
- `ITemplateEngine.Render` has no document, so `UseDocumentLanguageCulture` alone falls back to the
  invariant culture there; the document language is resolved by `IEmailTemplateRenderer`.

The invariant default is what keeps tests and previews deterministic: a Czech server and an English
server produce the same bytes unless the host asks for a culture.

> `date.to_string` / `math.format` take their format argument in **double quotes**
> (`{{ price | math.format "N2" }}`), and comparisons work as usual (`{{ if price > 100 }}`). The
> generator masks each Scriban code span before it sanitizes authored block content and restores it
> afterwards, so literals, `>` and `&&` inside `mj-text`/`mj-accordion-text` reach the engine intact.

---

## Application functions (host registry)

An application can expose its own filters without forking the engine by implementing
`ITemplateFunctionProvider` and registering it as a service:

```csharp
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

public sealed class CurrencyFunctions : ITemplateFunctionProvider
{
    public string Namespace => "app";

    public IReadOnlyDictionary<string, Delegate> GetFunctions() => new Dictionary<string, Delegate>
    {
        ["format_currency"] = new Func<decimal, string>(
            value => value.ToString("N2", CultureInfo.InvariantCulture) + " Kč"),
    };

    public IReadOnlyList<TemplateFunctionInfo> Describe() =>
        [new TemplateFunctionInfo("format_currency", "Formats an amount as Czech crowns.")];
}

services.AddSingleton<ITemplateFunctionProvider, CurrencyFunctions>();
services.AddTempoEmailTemplateEngine();
```

```
{{ total | app.format_currency }}
{{ app.format_currency total }}
```

Rules enforced when the engine is constructed (i.e. when `ITemplateEngine` is resolved, so a
misconfiguration fails at startup, never in the middle of a render):

| Rule | Detail |
|---|---|
| Namespace | `^[a-z][a-z0-9_]*$` (for example `app`, `money`, `app_v2`). |
| Builtins are off limits | `string`, `array`, `object`, `math`, `date`, `html`, `regex`, `timespan` (and every other Scriban namespace) cannot be used or shadowed. |
| One namespace, one provider | Two providers must not share a namespace. |
| Function name | `^[a-z][a-z0-9_]*$`. |
| Synchronous delegates only | A delegate returning `Task`/`ValueTask` is refused. |
| Contract types only | Parameters and the return value are `string`, `bool`, a numeric type, `decimal`, `DateTime`, `DateTimeOffset` or an `IEnumerable<T>` of those. `object`, `Type`, delegates, `char`, `Guid` and arbitrary host types are refused. |
| `Describe()` | Must describe functions `GetFunctions()` actually exposes (documentation of a missing function is a registration error). Empty list is fine. |
| Registration lifetime | **Singleton only** (`services.AddSingleton<ITemplateFunctionProvider, MyProvider>()`); a scoped or transient registration makes resolving `ITemplateEngine` fail with `InvalidOperationException`, because the engine snapshots the functions once. |
| Keyword namespace | A Scriban keyword (`for`, `this`, `if`, `end`, `func`, `null`, `true`, `false`, `in`, `while`, `case`, `when`, `else`, `ret`, `break`, `continue`, `capture`, `readonly`, `import`, `with`, `wrap`, `tablerow`, `empty`) could never be called and is refused. |

What a provider **cannot** do:

- escape the sandbox — its results pass the same member filter and the same limits (`LoopLimit`,
  `MaxOutputLength`, `Timeout`, …), and its arguments are template values the member filter already
  vetted;
- hand out a `Type`, a `MemberInfo`, a delegate, a value of a forbidden namespace or an internal
  escaping marker (`TrustedHtml`/`EscapedHtml`) — the signature is validated at registration and the
  result is re-checked on every call (fail closed: the render fails and the refusal is audited);
- leak a host object graph through an enumerable result: the value is materialized into a script array of
  scalars (an enumeration is bounded by `LoopLimit`), so `class Leaky : IEnumerable<string> { public string
  Password }` exposes no `password` member to the template;
- leak an exception message: a delegate that throws is logged and the render error only says
  `Template function 'app.x' failed.`;
- skip HTML escaping — the result of a provider function is escaped exactly like any other
  expression result, so `{{ app.tag }}` returning `<b>x</b>` renders `&lt;b&gt;x&lt;/b&gt;`.
  `html.trusted` stays the only opt-out and always runs the sanitizer.

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
