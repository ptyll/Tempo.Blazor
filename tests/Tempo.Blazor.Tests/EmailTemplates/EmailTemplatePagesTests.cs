using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.EmailTemplates;

public class EmailTemplatePagesTests : BunitContext
{
    private readonly IEmailTemplateApiClient _api = Substitute.For<IEmailTemplateApiClient>();

    public EmailTemplatePagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
        Services.AddSingleton(_api);
    }

    private static string ContentJsonWith(string textContent)
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = textContent });
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return EmailTemplateSerializer.Serialize(doc);
    }

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    // ── E10.2 list page ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ListPage_RendersTemplateCards()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Welcome", Subject = "Hi" },
            new() { Id = Guid.NewGuid(), Name = "Newsletter", Subject = "News" },
        });

        var cut = Render<EmailTemplatesPage>();

        cut.FindAll("[data-tm-template-card]").Should().HaveCount(2);
    }

    [Fact]
    public async Task ListPage_NewTemplate_CreatesAndNavigatesToEditor()
    {
        var newId = Guid.NewGuid();
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>());
        _api.CreateAsync(Arg.Any<CreateEmailTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmailTemplateDetailDto { Id = newId, Name = "Untitled template" });

        var cut = Render<EmailTemplatesPage>();
        await cut.Find("[data-tm-new-template]").ClickAsync(new());

        await _api.Received(1).CreateAsync(Arg.Any<CreateEmailTemplateRequest>(), Arg.Any<CancellationToken>());
        Nav.Uri.Should().EndWith($"/email-templates/edit/{newId}");
    }

    [Fact]
    public async Task ListPage_Delete_ConfirmCallsApi()
    {
        var id = Guid.NewGuid();
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = id, Name = "Doomed", Subject = "x" },
        });
        _api.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var cut = Render<EmailTemplatesPage>();
        cut.Find("[data-tm-delete]").Click();
        await cut.Find("[data-tm-confirm-delete]").ClickAsync(new());

        await _api.Received(1).DeleteAsync(id, Arg.Any<CancellationToken>());
    }

    // ── Fáze 11 list page (redesign) ─────────────────────────────────────────────────────────

    [Fact]
    public void ListPage_Search_FiltersBy_NameAndSubject_AndShowsNoMatchState()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Welcome email", Subject = "Welcome {{ first_name }}!" },
            new() { Id = Guid.NewGuid(), Name = "Newsletter", Subject = "{{ newsletter_title }}" },
            new() { Id = Guid.NewGuid(), Name = "Order confirmation", Subject = "Order confirmed" },
        });

        var cut = Render<EmailTemplatesPage>();
        cut.FindAll("[data-tm-template-card]").Should().HaveCount(3);

        // By NAME.
        cut.Find("[data-tm-list-search] input").Input("welcome");
        cut.FindAll("[data-tm-template-card]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Welcome email");

        // By SUBJECT: "confirmed" is in a subject only, never in a name.
        cut.Find("[data-tm-list-search] input").Input("confirmed");
        cut.FindAll("[data-tm-template-card]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Order confirmation");

        // No match: a dedicated state that names the term, not the "no templates yet" empty library.
        cut.Find("[data-tm-list-search] input").Input("zzz-nothing");
        cut.FindAll("[data-tm-template-card]").Should().BeEmpty();
        cut.FindAll("[data-tm-list-no-match]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("zzz-nothing");
    }

    [Fact]
    public void ListPage_Card_ShowsALanguageBadge_WithTheHumanName()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Ceska sablona", Subject = "Ahoj", Language = "cs" },
            new() { Id = Guid.NewGuid(), Name = "Neznama", Subject = "Hi", Language = "xx" },
        });

        var cut = Render<EmailTemplatesPage>();

        var badges = cut.FindAll("[data-tm-card-language]");
        badges.Should().HaveCount(2);
        badges[0].TextContent.Trim().Should().Be("TmDemo_EmailTemplates_Language_Cs",
            "the badge carries the localized language name from DemoResources, never the ISO code");
        badges[0].TextContent.Trim().Should().NotBe("cs");
        badges[1].TextContent.Trim().Should().Be("XX", "an unknown code degrades to the upper-cased code");
    }

    [Fact]
    public void ListPage_CardMenu_OffersTheSecondaryActions_AndDeleteAsksForConfirmation()
    {
        var id = Guid.NewGuid();
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = id, Name = "Doomed", Subject = "x" },
        });

        var cut = Render<EmailTemplatesPage>();

        // The phone menu is collapsed until its trigger is pressed, and then it carries the three
        // secondary actions (the same handlers the visible buttons use).
        cut.FindAll("[data-tm-card-menu-panel]").Should().BeEmpty("the menu is closed at load");
        cut.Find("[data-tm-card-menu]").Click();
        cut.Find("[data-tm-card-menu-send]").Should().NotBeNull();
        cut.Find("[data-tm-card-menu-duplicate]").Should().NotBeNull();
        cut.Find("[data-tm-card-menu-delete]").Click();
        cut.Find("[data-tm-confirm-delete]").Should().NotBeNull("Delete still asks before it deletes");
    }

    [Fact]
    public void ListPage_DeleteDialog_ShowsADestructiveIcon_AndDeleteIsADangerGhost()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Doomed", Subject = "x" },
        });

        var cut = Render<EmailTemplatesPage>();
        cut.Find("[data-tm-delete]").Click();

        cut.FindAll("[data-tm-delete-dialog-icon]").Should().ContainSingle(
            "a destructive confirmation is recognisable before the text is read");
        cut.Find("[data-tm-delete]").ClassList.Should().Contain("tm-demo-email-templates__delete",
            "the card's Delete is a ghost shape with danger ink, not a second filled button");
    }

    // ── E10.3 editor page ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EditorPage_LoadsAndRendersEditor()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi"),
        });

        var cut = Render<EmailTemplateEditorPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-email-editor-page]").Should().ContainSingle();
        cut.FindAll("[data-tm-email-editor]").Should().ContainSingle();
    }

    [Fact]
    public void EditorPage_UnknownTemplate_ShowsNotFound()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns((EmailTemplateDetailDto?)null);

        var cut = Render<EmailTemplateEditorPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-email-editor]").Should().BeEmpty();
        cut.Markup.Should().Contain("TmDemo_EmailTemplates_Editor_NotFound", "the empty state is localized copy, not shipped English")
            .And.NotContain("Template not found");
    }

    // ── E10.4 / E10.5 send page ─────────────────────────────────────────────────────────────

    [Fact]
    public void SendPage_BuildsFieldsFromVariables()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi {{ first_name }}"),
        });

        var cut = Render<EmailTemplateSendPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-var=\"first_name\"]").Should().ContainSingle();
    }

    [Fact]
    public async Task SendPage_Submit_CallsSendApi()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi {{ first_name }}"),
        });
        _api.SendAsync(id, Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendEmailResult(true, 202, Array.Empty<string>()));

        var cut = Render<EmailTemplateSendPage>(p => p.Add(c => c.Id, id));
        cut.Find("[data-tm-to]").Change("a@example.com");
        await cut.Find("[data-tm-send-submit]").ClickAsync(new());

        await _api.Received(1).SendAsync(id, Arg.Is<SendEmailRequest>(r => r.To.Contains("a@example.com")), Arg.Any<CancellationToken>());
        cut.FindAll("[data-tm-send-success]").Should().ContainSingle();
    }

    [Fact]
    public void SendPage_GroupsTheForm_AndLabelsVariablesWithHostDescriptions()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi {{ first_name }}"),
            SampleDataJson = "{\"first_name\":\"Jane\"}",
        });

        var cut = Render<EmailTemplateSendPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-send-section='recipients']").Should().ContainSingle();
        cut.FindAll("[data-tm-send-section='variables']").Should().ContainSingle();
        cut.Find("[data-tm-var-label]").GetAttribute("data-tm-var-label")
            .Should().Be("Recipient's first name", "the label is the host's description, not the token path");
        cut.Markup.Should().NotContain("first_name</label>", "the raw path never becomes the field label");
    }

    [Fact]
    public void SendPage_Preview_HidesTheSampleEditorAndTheViewToggle()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi {{ first_name }}"),
        });

        var cut = Render<EmailTemplateSendPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-preview-edit-data]").Should().BeEmpty(
            "the send page IS the sample data editor: a second one in the preview would be ambiguous");
        cut.FindAll("[data-tm-preview-view-group]").Should().BeEmpty(
            "the send page shows the message preview, not an HTML/Text switch");
    }

    // ── Fáze 11 visual review (ChangesRequested) ────────────────────────────────────────────

    [Fact]
    public void ListPage_Search_IgnoresDiacritics()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Šablona pro zákazníky", Subject = "Dobrý den" },
            new() { Id = Guid.NewGuid(), Name = "Welcome email", Subject = "Hi" },
        });

        var cut = Render<EmailTemplatesPage>();

        // A Czech user types without diacritics: "sablona" must find "Šablona" (the code said
        // "diacritics-insensitive" in a comment while comparing with OrdinalIgnoreCase).
        cut.Find("[data-tm-list-search] input").Input("sablona");
        cut.FindAll("[data-tm-template-card]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Šablona pro zákazníky");

        cut.Find("[data-tm-list-search] input").Input("zakazniky");
        cut.FindAll("[data-tm-template-card]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Šablona pro zákazníky");

        // The SUBJECT is folded the same way, and the case is still ignored.
        cut.Find("[data-tm-list-search] input").Input("DOBRY");
        cut.FindAll("[data-tm-template-card]").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Dobrý den");
    }

    [Fact]
    public void ListPage_CardMenu_ClosesOnEscape_AndOnAClickOutside()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Doomed", Subject = "x" },
        });

        var cut = Render<EmailTemplatesPage>();

        cut.Find("[data-tm-card-menu]").Click();
        cut.FindAll("[data-tm-card-menu-panel]").Should().ContainSingle();

        // Escape closes the menu a keyboard user opened (focus restore is asserted in E2E — bUnit cannot
        // observe document.activeElement).
        cut.Find("[data-tm-card-menu-panel]").KeyDown(Key.Escape);
        cut.FindAll("[data-tm-card-menu-panel]").Should().BeEmpty("Escape dismisses the open menu");
        cut.Find("[data-tm-card-menu]").GetAttribute("aria-expanded").Should().Be("false");

        // A click OUTSIDE the menu (the backdrop) closes it too — a menu that only its own items can close
        // leaves the user stuck.
        cut.Find("[data-tm-card-menu]").Click();
        cut.FindAll("[data-tm-card-menu-panel]").Should().ContainSingle();
        cut.Find("[data-tm-card-menu-backdrop]").Click();
        cut.FindAll("[data-tm-card-menu-panel]").Should().BeEmpty("a click outside dismisses the menu");
    }

    [Fact]
    public void ListPage_DeleteDialog_LeavesThePhonePresentationToTheViewport()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Doomed", Subject = "x" },
        });

        var cut = Render<EmailTemplatesPage>();

        cut.FindComponent<TmModal>().Instance
            .MobilePresentation.Should().Be(MobilePresentation.Auto,
                "Auto is a sheet on a phone AND a dialog on a desktop; forcing Sheet turned the 1440 px "
                + "confirmation into a bottom sheet the reference does not draw");
    }

    [Fact]
    public void ListPage_Texts_ComeFromTheDemoResources()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Welcome", Subject = "Hi" },
        });

        var cut = Render<EmailTemplatesPage>();

        // EchoTmLocalizer echoes the KEY, so a rendered key is the proof that the copy comes from
        // DemoResources (rule: no hardcoded user-facing demo text).
        cut.Find(".tm-demo-email-templates__subtitle").TextContent.Trim()
            .Should().Be("TmDemo_EmailTemplates_Subtitle");
        cut.Find("[data-tm-new-template]").TextContent.Trim().Should().Be("TmDemo_EmailTemplates_New");
        cut.Find("[data-tm-list-search] input").GetAttribute("placeholder")
            .Should().Be("TmDemo_EmailTemplates_Search");
        cut.Find("[data-tm-delete]").TextContent.Trim().Should().Be("TmDemo_EmailTemplates_Delete");
    }

    [Fact]
    public void EditorPage_GuardTexts_ComeFromTheDemoResources()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi"),
        });

        var cut = Render<EmailTemplateEditorPage>(p => p.Add(c => c.Id, id));

        var guard = cut.FindComponent<TmNavigationGuard>().Instance;
        guard.ConfirmTitle.Should().Be("TmDemo_EmailTemplates_Guard_Title");
        guard.ConfirmMessage.Should().Be("TmDemo_EmailTemplates_Guard_Message");
        guard.SaveAndLeaveText.Should().Be("TmDemo_EmailTemplates_Guard_SaveAndLeave");
        guard.ConfirmLeaveText.Should().Be("TmDemo_EmailTemplates_Guard_Leave");
        guard.CancelText.Should().Be("TmDemo_EmailTemplates_Guard_Stay");
    }

    [Fact]
    public void EditorPage_ChromeTexts_ComeFromTheDemoResources()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi"),
        });

        var cut = Render<EmailTemplateEditorPage>(p => p.Add(c => c.Id, id));
        var bar = cut.Find("[data-tm-demo-bar]");

        // EchoTmLocalizer echoes the key: every switch-bar label, every <select> option and the host status
        // line must be a DemoResources key, never shipped English (the page used to hardcode all of them).
        bar.QuerySelector("[data-tm-back]")!.TextContent.Trim().Should().Be("TmDemo_EmailTemplates_Editor_Back");
        bar.QuerySelector("[data-tm-demo-readonly]")!.OuterHtml.Should().Contain("TmDemo_EmailTemplates_Editor_ReadOnly");
        bar.QuerySelectorAll("label.tm-demo-email-editor-page__simulate > span").Select(s => s.TextContent.Trim())
            .Should().Equal(
                "TmDemo_EmailTemplates_Editor_HostSaveResult",
                "TmDemo_EmailTemplates_Editor_ItemEditing",
                "TmDemo_EmailTemplates_Editor_CanvasImages",
                "TmDemo_EmailTemplates_Editor_Layout",
                "TmDemo_EmailTemplates_Editor_MobilePanels",
                "TmDemo_EmailTemplates_Editor_ImportLimit");

        // The option VALUES are the stable contract the E2E drives; only their text is copy.
        bar.QuerySelectorAll("[data-tm-demo-simulate] option")
            .Select(o => (o.GetAttribute("value"), o.TextContent.Trim()))
            .Should().Equal(
                ("real", "TmDemo_EmailTemplates_Editor_Simulate_Real"),
                ("failed", "TmDemo_EmailTemplates_Editor_Simulate_Failed"),
                ("failed-long", "TmDemo_EmailTemplates_Editor_Simulate_FailedLong"),
                ("conflict", "TmDemo_EmailTemplates_Editor_Simulate_Conflict"));
        bar.QuerySelectorAll("[data-tm-demo-layout] option").Select(o => o.GetAttribute("value"))
            .Should().Equal("Auto", "Desktop", "Tablet", "Mobile");
        bar.QuerySelectorAll("[data-tm-demo-layout] option").Select(o => o.TextContent.Trim())
            .Should().OnlyContain(text => text.StartsWith("TmDemo_EmailTemplates_Editor_Layout_"));
        bar.QuerySelectorAll("[data-tm-demo-import-limit] option").Select(o => o.TextContent.Trim())
            .Should().OnlyContain(text => text.StartsWith("TmDemo_EmailTemplates_Editor_ImportLimit_"));

        bar.QuerySelector("[data-tm-demo-dirty]")!.TextContent.Trim()
            .Should().Be("TmDemo_EmailTemplates_Editor_HostDirty_No");
        bar.QuerySelector("[data-tm-demo-resolved-layout]")!.TextContent.Trim()
            .Should().StartWith("TmDemo_EmailTemplates_Editor_ResolvedLayout");
        bar.QuerySelector("[data-tm-demo-save-background]")!.TextContent.Trim()
            .Should().Be("TmDemo_EmailTemplates_Editor_SaveBackground");
    }

    [Fact]
    public void SendPage_DisabledSend_SaysWhy()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi {{ first_name }}"),
        });

        var cut = Render<EmailTemplateSendPage>(p => p.Add(c => c.Id, id));

        cut.Find("[data-tm-send-hint]").TextContent.Trim()
            .Should().Be("TmDemo_EmailTemplates_Send_RecipientRequired",
                "a disabled primary action explains what is missing");

        cut.Find("[data-tm-to]").Change("a@example.com");

        cut.FindAll("[data-tm-send-hint]").Should().BeEmpty("the reason is gone once a recipient is typed");
    }

    /// <summary>Echoes the key (plus its format arguments), so a rendered key proves the copy is localized.</summary>
    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "|" + string.Join("|", arguments);
    }
}
