using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F5 — the editor and its request validators speak the packages' own languages in a real browser.
/// <para>
/// The strings used to come from <c>.resx</c> tables resolved through whatever
/// <c>IStringLocalizerFactory</c> the host registered, so a host that replaces that factory (or a
/// published Blazor WebAssembly deployment, where the resource-manager pipeline resolves nothing) showed
/// raw keys such as <c>EmailEditor_Toolbar_Save</c> instead of "Save". They are now flat JSON tables
/// embedded in the package assemblies, which the browser reads like any other manifest resource — exactly
/// what this spec proves end to end: the demo's language switcher flips
/// <c>CultureInfo.CurrentUICulture</c>, the page reloads, and every visible string is text in that
/// language, never a key.
/// </para>
/// <para>
/// The key-shape scan is deliberately narrow (the editor's own key prefixes, see
/// <c>TmEmailResources*.json</c>): prose that merely mentions a dot-separated word stays legitimate, so
/// the assertion fails only on a real leaked key.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorLocalizationE2ETests : EmailTemplateE2ETestBase
{
    /// <summary>Every prefix the email packages' resource keys start with (block/category UI vocabulary included).</summary>
    private static readonly Regex RawKeyShape = new(
        @"(?<![\w-])(?:EmailEditor_[A-Za-z]|(?:block|category|layout|import|render|validation|serialization|template)\.[a-z_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [TestMethod]
    public async Task F5_1_Editor_ShowsLocalizedText_NotRawKeys_InEnglishCzechAndSlovak()
    {
        // ── English (the demo default) ──────────────────────────────────────────────────────
        var page = await OpenEditorAsync();
        (await page.Locator("[data-tm-save]").First.InnerTextAsync()).Trim()
            .Should().Be("Save", "the save action is a shipped English string, not a key");
        await AssertNoRawKeysAsync(page, "en");
        await SaveNamedScreenshotAsync(page, "20-editor-en.png");

        // ── Czech: the string must follow CurrentUICulture, not the host's factory ───────────
        await SwitchEditorLanguageAsync(page, "cs");
        (await page.Locator("[data-tm-save]").First.InnerTextAsync()).Trim()
            .Should().Be("Uložit", "the Czech table is embedded in the package assembly");
        await AssertNoRawKeysAsync(page, "cs");
        await SaveNamedScreenshotAsync(page, "21-editor-cs.png");

        // A validator message is the second, independent table (EmailTemplateValidationResources):
        // a button without a link must be reported in Czech prose, not as "validation.button.href_missing".
        await page.Locator("[data-tm-block='button']").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await page.Locator("[data-tm-validate-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-validation]", new() { Timeout = 15000 });
        var findings = await page.Locator("[data-tm-validation-message]").AllInnerTextsAsync();
        findings.Should().NotBeEmpty("an empty-href button is a validation finding");
        findings.Should().NotContain(
            finding => finding.Contains("validation.", StringComparison.Ordinal)
                       || finding.Contains("EmailEditor_", StringComparison.Ordinal),
            "a validator message rendered as its key means the host's factory served the lookup");
        await SaveNamedScreenshotAsync(page, "22-validation-cs.png");

        // Close the validation dialog (Escape) — its overlay intercepts the language switcher's clicks.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".tm-modal-overlay", new() { State = WaitForSelectorState.Detached, Timeout = 15000 });

        // ── Slovak: the language this release adds; a Czech copy would slip through here ─────
        await SwitchEditorLanguageAsync(page, "sk");
        (await page.Locator("[data-tm-save]").First.InnerTextAsync()).Trim()
            .Should().Be("Uložiť", "Slovak ships its own table, not a copy of Czech");
        await AssertNoRawKeysAsync(page, "sk");
        await SaveNamedScreenshotAsync(page, "23-editor-sk.png");
    }

    private async Task<IPage> OpenEditorAsync()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 15000 });
        return page;
    }

    /// <summary>
    /// Switches language through the demo's real switcher (the base helper clicks it and waits for the
    /// forced reload), then waits for the editor to render again in the new language.
    /// </summary>
    private async Task SwitchEditorLanguageAsync(IPage page, string language)
    {
        await SwitchLanguageAsync(page, language);
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await page.WaitForFunctionAsync(
            "lang => document.documentElement.lang?.toLowerCase().startsWith(lang) === true"
            + " || !!document.querySelector('[data-testid=\"language-switcher-' + lang + '\"][aria-pressed=\"true\"]')",
            language, new() { Timeout = 20000 });
    }

    /// <summary>Fails when any visible text of the editor region looks like an unlocalized resource key.</summary>
    private static async Task AssertNoRawKeysAsync(IPage page, string language)
    {
        var text = await page.EvaluateAsync<string>(
            """
            () => {
                const root = document.querySelector('[data-tm-email-editor]');
                return root ? root.innerText : '';
            }
            """);

        text.Should().NotBeNullOrWhiteSpace("the editor rendered no text at all ({0})", language);
        RawKeyShape.Matches(text).Select(match => match.Value)
            .Should().BeEmpty("every visible editor string must be translated, not a raw key ({0})", language);
    }
}
