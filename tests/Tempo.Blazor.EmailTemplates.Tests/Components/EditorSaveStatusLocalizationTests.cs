using Microsoft.Extensions.Time.Testing;
using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7 — the save-status strings come from the package's own embedded JSON tables (en/cs/sk/fr), not from
/// a key fallback: "Unsaved changes", "Saving…", "Saved just now", "Save failed" and "Conflict" are real
/// words in the current UI culture.
/// </summary>
public class EditorSaveStatusLocalizationTests : BunitContext
{
    public EditorSaveStatusLocalizationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        // No ITmEmailLocalizer override: the package registers its real JSON-backed localizer.
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    [Theory]
    [InlineData("en", "Unsaved changes", "Saving…", "Saved just now", "Save failed", "Version conflict")]
    [InlineData("cs", "Neuložené změny", "Ukládám…", "Uloženo právě teď", "Uložení selhalo", "Konflikt verzí")]
    [InlineData("sk", "Neuložené zmeny", "Ukladám…", "Uložené práve teraz", "Uloženie zlyhalo", "Konflikt verzií")]
    [InlineData("fr", "Modifications non enregistrées", "Enregistrement…", "Enregistré à l'instant",
        "L'enregistrement a échoué", "Conflit de version")]
    public void TheBadge_ShowsEveryStateInTheCurrentCulture(
        string culture,
        string dirty,
        string saving,
        string savedJustNow,
        string failed,
        string conflict)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            var gate = new TaskCompletionSource<EmailSaveResult>();
            var answers = new Queue<Func<Task<EmailSaveResult>>>();
            answers.Enqueue(() => gate.Task);
            answers.Enqueue(() => Task.FromResult(EmailSaveResult.Failed()));
            answers.Enqueue(() => Task.FromResult(EmailSaveResult.Conflict()));
            answers.Enqueue(() => Task.FromResult(EmailSaveResult.Saved()));

            var cut = Render<TmEmailTemplateEditor>(p => p
                .Add(c => c.Document, new EmailTemplateDocument())
                .Add(c => c.OnSave, (EmailSaveRequest _) => answers.Count > 0
                    ? answers.Dequeue()()
                    : Task.FromResult(EmailSaveResult.Saved())));

            cut.Find("[data-tm-block=\"text\"]").Click();
            BadgeText(cut).Should().Be(dirty);
            cut.Find("[data-tm-save]").Click();
            BadgeText(cut).Should().Be(saving);

            gate.SetResult(EmailSaveResult.Saved());
            cut.WaitForAssertion(() => BadgeText(cut).Should().Be(savedJustNow));

            cut.Find("[data-tm-block=\"button\"]").Click();
            cut.Find("[data-tm-save]").Click();
            cut.WaitForAssertion(() => BadgeText(cut).Should().Be(failed));

            cut.Find("[data-tm-block=\"divider\"]").Click();
            cut.Find("[data-tm-save]").Click();
            cut.WaitForAssertion(() => BadgeText(cut).Should().Be(conflict));

            cut.Find("[data-tm-block=\"spacer\"]").Click();
            cut.Find("[data-tm-save]").Click();
            cut.WaitForAssertion(() => BadgeText(cut).Should().Be(savedJustNow));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static string BadgeText(IRenderedComponent<TmEmailTemplateEditor> cut)
        => cut.Find("[data-tm-save-badge]").TextContent.Trim();

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}

/// <summary>
/// Fáze 7 — "Saved {relative time}" must use the CORE <c>ITmRelativeTimeFormatter</c> rather than a private
/// copy of the plural rules: the phrase ("před 2 dny", "il y a 2 jours") is the language's own, and the
/// editor's own pattern ("Uloženo {0}") carries the verb.
/// </summary>
public class EditorSavedRelativeTimeTests : BunitContext
{
    public EditorSavedRelativeTimeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    [Fact]
    public void TheSavedBadge_SpeaksTheCulturesOwnRelativePhrase()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("cs");
            var time = new FakeTimeProvider();

            // The real core localizer (Tm_Time_*) plus the core formatter over the editor's clock.
            Services.AddTempoBlazor();
            Services.AddSingleton<ITmRelativeTimeFormatter>(
                sp => new TmRelativeTimeFormatter(sp.GetRequiredService<ITmLocalizer>(), time));

            var cut = Render<TmEmailTemplateEditor>(p => p
                .Add(c => c.Document, new EmailTemplateDocument())
                .Add(c => c.TimeProvider, time)
                .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

            cut.Find("[data-tm-block=\"text\"]").Click();
            cut.Find("[data-tm-save]").Click();
            cut.Find("[data-tm-save-badge]").TextContent.Should().Contain("Uloženo právě teď");

            time.Advance(TimeSpan.FromDays(2));

            cut.Find("[data-tm-save-badge]").TextContent.Trim()
                .Should().Be("Uloženo před 2 dny", "the plural form comes from the core formatter");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
