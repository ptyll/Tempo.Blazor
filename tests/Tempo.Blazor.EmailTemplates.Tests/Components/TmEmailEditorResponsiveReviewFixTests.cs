using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 8 review (ChangesRequested) — the editor's responsive chrome: the phone's Tabs presentation after an
/// insert, the one-way <c>ActiveMobilePanel</c>, the layout-vs-user collapse contract (U1/U2/D27), the compact
/// header (document title, pinned Save), the "⋮" overflow contents and the tablet's inspector rail.
/// </summary>
public class TmEmailEditorResponsiveReviewFixTests : EmailEditorTestBase
{
    private IRenderedComponent<TmEmailTemplateEditor> RenderWith(
        EmailTemplateDocument? document = null,
        TmLayoutMode layoutMode = TmLayoutMode.Mobile,
        MobilePanelPresentation presentation = MobilePanelPresentation.Sheet,
        EditorShellPanel activeMobilePanel = EditorShellPanel.None,
        bool hasActiveMobilePanelCallback = false,
        Action<EditorShellPanel>? activeMobilePanelChanged = null,
        Action<bool>? toolboxCollapsedChanged = null,
        RenderFragment? toolbarStart = null,
        RenderFragment? toolbarEnd = null)
        => Render<TmEmailTemplateEditor>(p =>
        {
            p.Add(c => c.Document, document ?? DocWithTextBlock(out _));
            p.Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved()));
            p.Add(c => c.LayoutMode, layoutMode);
            p.Add(c => c.MobilePanelPresentation, presentation);
            p.Add(c => c.ActiveMobilePanel, activeMobilePanel);
            if (hasActiveMobilePanelCallback || activeMobilePanelChanged is not null)
            {
                p.Add(c => c.ActiveMobilePanelChanged,
                    EventCallback.Factory.Create<EditorShellPanel>(
                        this, panel => activeMobilePanelChanged?.Invoke(panel)));
            }
            if (toolboxCollapsedChanged is not null)
            {
                p.Add(c => c.ToolboxCollapsedChanged,
                    EventCallback.Factory.Create<bool>(this, collapsed => toolboxCollapsedChanged(collapsed)));
            }
            if (toolbarStart is not null) p.Add(c => c.ToolbarStart, toolbarStart);
            if (toolbarEnd is not null) p.Add(c => c.ToolbarEnd, toolbarEnd);
        });

    // ── Item 2: an insert in the Tabs presentation shows the canvas ─────────────────────────

    [Fact]
    public void Adding_a_block_in_the_Tabs_presentation_switches_to_the_canvas_tab()
    {
        // Uncontrolled host (no ActiveMobilePanelChanged binding): the editor's own shadow decides.
        var cut = RenderWith(presentation: MobilePanelPresentation.Tabs,
            activeMobilePanel: EditorShellPanel.Left);

        cut.Find("[data-tm-block='text']").Click();

        cut.FindComponent<TmEditorShell>().Instance.ActiveMobilePanel
            .Should().Be(EditorShellPanel.None,
                "the Tabs presentation has no sheet to close: an insert has to reveal the CANVAS tab, "
                + "otherwise the new block lands on a canvas the user cannot see");
    }

    [Fact]
    public void Adding_a_section_in_the_Tabs_presentation_switches_to_the_canvas_tab()
    {
        var seen = new List<EditorShellPanel>();
        var cut = RenderWith(presentation: MobilePanelPresentation.Tabs,
            activeMobilePanel: EditorShellPanel.Left,
            activeMobilePanelChanged: seen.Add);

        cut.Find("[data-tm-preset]").Click();

        seen.Should().Contain(EditorShellPanel.None,
            "an added section lives on the canvas, which the Tabs presentation must reveal");
    }

    // ── Item 5: a one-way ActiveMobilePanel is adopted ──────────────────────────────────────

    [Fact]
    public void A_one_way_ActiveMobilePanel_parameter_moves_the_sheet_when_the_host_changes_it()
    {
        // The host drives the tab ONE WAY (no ActiveMobilePanelChanged binding).
        var cut = RenderWith(presentation: MobilePanelPresentation.Tabs,
            activeMobilePanel: EditorShellPanel.Left);
        cut.FindComponent<TmEditorShell>().Instance.ActiveMobilePanel
            .Should().Be(EditorShellPanel.Left);

        // The user picks the Properties tab: the editor records it in its own shadow.
        cut.FindAll(".tm-editor-shell__tabs--region .tm-editor-shell__tab")[2].Click();
        cut.FindComponent<TmEditorShell>().Instance.ActiveMobilePanel
            .Should().Be(EditorShellPanel.Right);

        // Now the HOST moves the tab. Without adopting a changed parameter the editor's shadow (Right)
        // keeps winning and the host's value is silently ignored.
        cut.Render(p => p.Add(c => c.ActiveMobilePanel, EditorShellPanel.None));

        cut.FindComponent<TmEditorShell>().Instance.ActiveMobilePanel
            .Should().Be(EditorShellPanel.None,
                "a host-supplied parameter change has to win over the editor's own shadow");
    }

    // ── U1/U2/D27: the tablet auto-rail is a layout state ──────────────────────────────────

    [Fact]
    public void A_tablet_auto_rail_does_not_raise_ToolboxCollapsedChanged_and_is_not_adopted()
    {
        var raised = new List<bool>();
        var cut = RenderWith(layoutMode: TmLayoutMode.Tablet, toolboxCollapsedChanged: raised.Add);

        // Both panels are open, so the shell's one-expanded-panel rule rails the palette on its own.
        cut.FindAll("[data-region='rail-left']").Should().ContainSingle(
            "a tablet expands at most one panel (F6-TABLET-DOCKED-PANELS)");
        raised.Should().BeEmpty(
            "the tablet auto-rail is a LAYOUT state: it must not be reported as a collapsed preference (U1/D27)");
        cut.FindComponent<TmEditorShell>().Instance.CollapsedPanels.HasFlag(EditorShellPanel.Left)
            .Should().BeFalse("a layout-imposed rail is not adopted into the host parameter (U1/D27)");
    }

    [Fact]
    public void A_user_collapse_of_the_palette_raises_ToolboxCollapsedChanged()
    {
        var raised = new List<bool>();
        var cut = RenderWith(layoutMode: TmLayoutMode.Desktop, toolboxCollapsedChanged: raised.Add);

        cut.Find(".tm-editor-shell__panel-toggle--left").Click();

        raised.Should().ContainSingle("a USER collapse is a preference and does report (U1)");
        raised[0].Should().BeTrue();
    }

    [Fact]
    public void The_tablets_inspector_rail_expands_the_properties_as_a_user_action()
    {
        var raised = new List<bool>();
        var cut = RenderWith(layoutMode: TmLayoutMode.Tablet, toolboxCollapsedChanged: raised.Add);

        // The inspector needs its own way back once the palette wins the tablet (which is what happens
        // when the user expands the palette from its rail — a USER action, hence raised below).
        cut.Find("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").Click();
        raised.Clear();

        var rail = cut.Find("[data-tm-properties-rail]");
        rail.QuerySelector("[data-tm-expand-properties]").Should().NotBeNull(
            "a railed inspector needs its own way back, not only the palette's rail");
        rail.QuerySelector("[data-tm-expand-properties]")!
            .GetAttribute("aria-label").Should().Be("EmailEditor_Panel_Properties");

        cut.Find("[data-tm-expand-properties]").Click();

        raised.Should().ContainSingle(
            "expanding the inspector rails the palette, which is a USER collapse of the palette (U1)");
        raised[0].Should().BeTrue();
    }

    // ── Items 11/13/14: the compact header and its overflow ───────────────────────────────

    [Fact]
    public void The_overflow_trigger_has_an_accessible_name()
    {
        var cut = RenderWith();

        var trigger = cut.Find("[data-tm-mobile-overflow] .tm-popover__trigger, [data-tm-mobile-overflow] button");
        trigger.QuerySelector(".tm-sr-only").Should().NotBeNull(
            "an icon-only trigger is announced as \"button\" without a visually hidden name (item 11)");
        trigger.QuerySelector(".tm-sr-only")!.TextContent.Should().Be("EmailEditor_Mobile_MoreActions");
    }

    [Fact]
    public void The_overflow_offers_the_long_tail_without_a_second_Preview()
    {
        var cut = RenderWith();
        cut.Find("[data-tm-mobile-overflow] .tm-popover__trigger, [data-tm-mobile-overflow] button").Click();

        var panel = cut.Find("[data-tm-mobile-overflow-panel]");
        panel.QuerySelectorAll("[data-tm-import-btn]").Should().ContainSingle();
        panel.QuerySelectorAll("[data-tm-mjml-btn]").Should().ContainSingle();
        panel.QuerySelectorAll("[data-tm-validate-btn]").Should().ContainSingle();
        panel.QuerySelectorAll("[data-tm-help-btn]").Should().ContainSingle();
        panel.QuerySelectorAll("[data-tm-preview-btn]").Should().BeEmpty(
            "Preview is one of the four action-bar commands (D20) — a second path to the same modal "
            + "on one phone screen is noise (item 14)");
    }

    // ── Fáze 11 review: a command from the overflow closes it ─────────────────────────────

    [Fact]
    public void A_command_from_the_overflow_closes_the_overflow()
    {
        var cut = RenderWith();
        cut.Find("[data-tm-mobile-overflow] .tm-popover__trigger, [data-tm-mobile-overflow] button").Click();
        cut.FindAll("[data-tm-mobile-overflow-panel]").Should().ContainSingle();

        cut.Find("[data-tm-mobile-overflow-panel] [data-tm-help-btn]").Click();

        cut.FindAll("[data-tm-mobile-overflow-panel]").Should().BeEmpty(
            "Help opened its own surface: an overflow left open behind it is a second panel the user has "
            + "to dismiss by hand, and its commands stay clickable under the new dialog");
        cut.FindComponent<TmKeyboardShortcutsHelp>().Should().NotBeNull("the command itself still ran");
    }

    [Fact]
    public void A_host_command_from_the_overflow_closes_the_overflow()
    {
        RenderFragment hostAction = builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "type", "button");
            builder.AddAttribute(2, "data-tm-host-action", true);
            builder.AddContent(3, "Host action");
            builder.CloseElement();
        };
        var cut = RenderWith(toolbarEnd: hostAction);
        cut.Find("[data-tm-mobile-overflow] .tm-popover__trigger, [data-tm-mobile-overflow] button").Click();
        cut.FindAll("[data-tm-mobile-overflow-panel]").Should().ContainSingle();

        cut.Find("[data-tm-mobile-overflow-panel] [data-tm-host-action]").Click();

        cut.FindAll("[data-tm-mobile-overflow-panel]").Should().BeEmpty(
            "the host's own fragment lives in the same panel, so its commands close it too");
    }

    [Fact]
    public void The_mobile_header_names_the_document_when_the_host_has_no_ToolbarStart()
    {
        var document = DocWithTextBlock(out _);
        document.Name = "Welcome campaign";

        var cut = RenderWith(document: document);

        cut.Find("[data-tm-mobile-title]").TextContent.Should().Be("Welcome campaign",
            "a phone header with two controls and no template name does not tell the user what they edit");
    }

    [Fact]
    public void The_mobile_header_prefers_the_hosts_ToolbarStart_over_the_document_title()
    {
        var document = DocWithTextBlock(out _);
        document.Name = "Welcome campaign";

        var cut = RenderWith(document: document, toolbarStart: builder =>
            builder.AddMarkupContent(0, "<span data-tm-host-start>Back</span>"));

        cut.FindAll("[data-tm-mobile-title]").Should().BeEmpty(
            "the host's own title wins; two titles on one 390 px header is one too many");
        cut.FindAll("[data-tm-host-start]").Should().ContainSingle();
    }

    // ── Item 10: the save state keeps its label on a phone ────────────────────────────────

    [Fact]
    public async Task The_save_badge_keeps_its_label_below_the_desktop_layout()
    {
        var cut = RenderWith();
        cut.Find("[data-tm-save]").Click();
        await cut.WaitForStateAsync(
            () => cut.FindAll("[data-tm-save-badge]").Count == 1,
            TimeSpan.FromSeconds(5));

        var badge = cut.Find("[data-tm-save-badge]");
        badge.QuerySelector(".tm-email-editor__save-label").Should().NotBeNull(
            "the label IS the state; an icon-only chip cannot say \"saved\" (item 10)");
        badge.QuerySelector(".tm-email-editor__save-label")!.TextContent
            .Should().StartWith("EmailEditor_SaveStatus_");
    }
}
