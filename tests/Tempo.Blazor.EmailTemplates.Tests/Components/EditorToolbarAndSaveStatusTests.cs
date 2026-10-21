using Microsoft.Extensions.Time.Testing;
using Microsoft.AspNetCore.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7 — grouped toolbar, host slots and the save-status badge (§3.2/§3.3). The bar is grouped by
/// task (history | review | file | help), the host gets <c>ToolbarStart</c>/<c>ToolbarEnd</c> for its own
/// actions, <c>ShowSaveButton</c>/<c>ShowSaveStatus</c> can switch the save chrome off, and the badge
/// announces every state through a polite live region — including "Saved &lt;relative time&gt;" which is
/// refreshed from <see cref="TimeProvider"/>.
/// </summary>
public class EditorToolbarAndSaveStatusTests : EmailEditorTestBase
{
    [Fact]
    public void TheToolbar_IsGroupedByTask_InTheDocumentedOrder()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());

        var groups = cut.FindAll("[data-tm-email-editor] .tm-email-editor__toolbar [data-tm-group]")
            .Select(e => e.GetAttribute("data-tm-group"))
            .ToList();

        groups.Should().Equal("history", "review", "file", "help");
        // Fáze 8: the groups are core TmToolbarGroups; the core bar spaces them (and moves the
        // low-priority ones into the "More" menu when the row runs out of room) instead of hairline
        // separators the editor drew itself.
        cut.FindAll(".tm-email-editor__toolbar .tm-toolbar-group").Count.Should().BeGreaterThanOrEqualTo(4);
        cut.FindAll("[data-tm-save-chrome]").Should().ContainSingle("the primary action closes the row");
    }

    [Fact]
    public void TheHistoryButtons_CarryATextLabel_AndAnAriaLabel()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());

        foreach (var selector in new[] { "[data-tm-undo]", "[data-tm-redo]" })
        {
            var button = cut.Find(selector);
            button.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace(
                "below the toolbar's own label breakpoint only the icon is visible, so the accessible name "
                + "must not depend on the label");
            cut.FindAll($"{selector} .tm-toolbar-btn-text").Should().ContainSingle(
                "the action is spelled out and the core toolbar decides when the label hides");
        }
    }

    [Fact]
    public void TheExportButton_IsCalledExport_AndKeepsItsTestHook()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());

        cut.FindAll("[data-tm-mjml-btn]").Should().ContainSingle("hosts and E2E select the export by this attribute");
        cut.Find("[data-tm-mjml-btn]").TextContent.Should().Contain("EmailEditor_Toolbar_Export");
    }

    [Fact]
    public void ToolbarStartAndToolbarEnd_HostTheHostActions()
    {
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved()))
            .Add(c => c.ToolbarStart, (RenderFragment)(b => b.AddMarkupContent(0, "<span data-tm-host-start>Back</span>")))
            .Add(c => c.ToolbarEnd, (RenderFragment)(b => b.AddMarkupContent(0, "<span data-tm-host-end>Send test</span>"))));

        cut.FindAll("[data-tm-toolbar-start] [data-tm-host-start]").Should().ContainSingle();
        cut.FindAll("[data-tm-toolbar-end] [data-tm-host-end]").Should().ContainSingle();

        var markup = cut.Markup;
        markup.IndexOf("data-tm-toolbar-start", StringComparison.Ordinal)
            .Should().BeLessThan(markup.IndexOf("data-tm-group=\"history\"", StringComparison.Ordinal),
                "host actions lead the bar");
        markup.IndexOf("data-tm-toolbar-end", StringComparison.Ordinal)
            .Should().BeLessThan(markup.IndexOf("data-tm-save=\"true\"", StringComparison.Ordinal),
                "the trailing host actions come before the save chrome");
    }

    [Fact]
    public void ShowSaveButtonAndShowSaveStatus_SwitchTheSaveChromeOff()
    {
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.ShowSaveButton, false)
            .Add(c => c.ShowSaveStatus, false)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.FindAll("[data-tm-save]").Should().BeEmpty();
        cut.FindAll("[data-tm-save-badge]").Should().BeEmpty();
    }

    /// <summary>
    /// The badge is a visual pill, not a live region any more: a live region inside the toolbar that changes
    /// on every edit would narrate the whole editing session. ONE polite announcer states a confirmed
    /// manual/shortcut/programmatic save; a background auto-save, "Saving…" and the minute tick stay silent.
    /// </summary>
    [Fact]
    public void TheBadge_IsNotALiveRegion_AndTheAnnouncerSpeaksOnlyAConfirmedSave()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());
        cut.Find("[data-tm-block=\"text\"]").Click();

        var badge = cut.Find("[data-tm-save-badge]");
        badge.HasAttribute("aria-live").Should().BeFalse("the announcer owns the live region now");
        badge.HasAttribute("role").Should().BeFalse();

        var announcer = cut.Find("[data-tm-save-announcer]");
        announcer.GetAttribute("role").Should().Be("status");
        announcer.GetAttribute("aria-live").Should().Be("polite");
        cut.Find("[data-tm-save-announcer]").TextContent.Should().BeEmpty("an edit is not an outcome");

        cut.Find("[data-tm-save]").Click();
        cut.Find("[data-tm-save-announcer]").TextContent.Should().Be("EmailEditor_SaveStatus_Saved");
    }

    [Fact]
    public void ASuccessfulAutoSave_DoesNotTouchTheAnnouncer()
    {
        var time = new FakeTimeProvider();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => BadgeState(cut).Should().Be("saved"));

        cut.Find("[data-tm-save-announcer]").TextContent.Should().BeEmpty(
            "a background save must not narrate the editing session");

        // The minute tick re-renders the relative time and must not speak either.
        time.Advance(TimeSpan.FromMinutes(5));
        cut.Find("[data-tm-save-announcer]").TextContent.Should().BeEmpty();
    }

    [Fact]
    public void TheBadge_FollowsEveryState_FromDirtyToSavingToSaved()
    {
        var gate = new TaskCompletionSource<EmailSaveResult>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest _) => gate.Task));

        BadgeState(cut).Should().BeNull("a pristine, never-saved document has no save state to report");

        cut.Find("[data-tm-block=\"text\"]").Click();
        BadgeState(cut).Should().Be("dirty");
        BadgeText(cut).Should().Contain("EmailEditor_SaveStatus_Dirty");

        cut.Find("[data-tm-save]").Click();
        BadgeState(cut).Should().Be("saving");

        gate.SetResult(EmailSaveResult.Saved());
        cut.WaitForAssertion(() => BadgeState(cut).Should().Be("saved"));
        BadgeText(cut).Should().Contain("EmailEditor_SaveStatus_SavedJustNow");
    }

    [Fact]
    public void TheSavedBadge_ShowsARelativeTime_RefreshedFromTimeProvider()
    {
        var time = new FakeTimeProvider();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.TimeProvider, time)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-save]").Click();
        BadgeText(cut).Should().Contain("EmailEditor_SaveStatus_SavedJustNow");

        time.Advance(TimeSpan.FromMinutes(5));

        cut.WaitForAssertion(() => BadgeText(cut).Should().Contain("EmailEditor_SaveStatus_SavedAgo"));
        BadgeText(cut).Should().Contain("5");
    }

    /// <summary>
    /// The shortcut help is the editor's discoverability contract: every editing chord, the navigation
    /// keys as two separate items (not one chord) and Ctrl/⌘+S for saving.
    /// </summary>
    [Fact]
    public void TheShortcutHelp_ListsEveryEditorShortcut()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());
        cut.Find("[data-tm-help-btn]").Click();

        var categories = cut.FindAll(".tm-keyboard-shortcuts-category-title").Select(e => e.TextContent).ToList();
        categories.Should().Equal(
            "EmailEditor_Shortcut_Category_Edit",
            "EmailEditor_Shortcut_Category_Navigate",
            "EmailEditor_Shortcut_Category_File");

        var items = cut.FindAll(".tm-keyboard-shortcuts-item")
            .Select(item => string.Join("+", item.QuerySelectorAll("kbd").Select(k => k.TextContent)))
            .ToList();
        items.Should().Contain("Ctrl/⌘+Z");
        items.Should().Contain("Ctrl/⌘+Y");
        items.Should().Contain("Ctrl/⌘+Shift+Z");
        items.Should().Contain("Ctrl/⌘+C");
        items.Should().Contain("Ctrl/⌘+X");
        items.Should().Contain("Ctrl/⌘+V");
        items.Should().Contain("Ctrl/⌘+D");
        items.Should().Contain("Ctrl/⌘+S");
        items.Should().Contain("Delete");
        items.Should().Contain("↑");
        items.Should().Contain("↓");
        items.Should().Contain("Esc");
        items.Should().NotContain(item => item.Contains("Arrow Up", StringComparison.Ordinal),
            "the old single chord entry is what this phase replaces");
    }

    [Fact]
    public void CtrlS_SavesTheDocument()
    {
        var triggers = new List<EmailSaveTrigger>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "s", CtrlKey = true });

        triggers.Should().ContainSingle();
        triggers[0].Should().Be(EmailSaveTrigger.Shortcut);
        cut.Instance.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void CmdS_SavesOnMacToo()
    {
        var triggers = new List<EmailSaveTrigger>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "s", MetaKey = true });

        triggers.Should().ContainSingle();
        triggers[0].Should().Be(EmailSaveTrigger.Shortcut);
    }

    /// <summary>
    /// A failed or conflicted save needs more room than a pill gives: the host's reason is a sentence and the
    /// user needs a next step. The alert sits under the command bar, outside the save chrome, and carries the
    /// host message, a localized hint and a retry action that runs a MANUAL save.
    /// </summary>
    [Fact]
    public void Failed_RendersAnAlertWithTheHostMessageHintAndRetry()
    {
        var triggers = new List<EmailSaveTrigger>();
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.OnSave, (EmailSaveRequest r) =>
            {
                triggers.Add(r.Trigger);
                return Task.FromResult(triggers.Count == 1
                    ? EmailSaveResult.Failed("The server rejected the template.")
                    : EmailSaveResult.Saved());
            }));

        cut.Find("[data-tm-save]").Click();

        var alert = cut.Find("[data-tm-save-alert]");
        alert.GetAttribute("data-tm-save-alert").Should().Be("failed");
        alert.GetAttribute("role").Should().Be("alert", "a failed save is what TmAlert announces");
        alert.TextContent.Should().Contain("The server rejected the template.", "the host's own reason");
        alert.TextContent.Should().Contain("EmailEditor_SaveStatus_FailedHint", "the user needs a next step");

        // The chrome badge keeps the short state label; the full sentence is the alert's job.
        cut.Find("[data-tm-save-badge] .tm-email-editor__save-label").TextContent
            .Should().Be("EmailEditor_SaveStatus_Failed");

        cut.Find("[data-tm-save-retry]").Click();
        triggers.Should().HaveCount(2);
        triggers[^1].Should().Be(EmailSaveTrigger.Manual, "the retry is the user's explicit save");
        cut.FindAll("[data-tm-save-alert]").Should().BeEmpty("the retry succeeded, so the alert is gone");
    }

    [Fact]
    public void Conflict_RendersAWarningAlertWithItsOwnHint()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Conflict("Revision 12 is newer."));

        cut.Find("[data-tm-save]").Click();

        var alert = cut.Find("[data-tm-save-alert]");
        alert.GetAttribute("data-tm-save-alert").Should().Be("conflict");
        alert.TextContent.Should().Contain("Revision 12 is newer.");
        alert.TextContent.Should().Contain("EmailEditor_SaveStatus_ConflictHint");
        alert.ClassList.Should().Contain("tm-alert--warning", "a conflict is a warning, not an error");
    }

    /// <summary>
    /// A read-only editor is a mode, not a dimmed editor: the badge says "Read-only", the Save button is not
    /// rendered (there is nothing it could do) and unsaved changes stay as a secondary fact without any CTA.
    /// </summary>
    [Fact]
    public void TheReadOnlyEditor_ShowsAReadOnlyBadge_AndNoSaveChrome()
    {
        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, DocWithTextBlock(out _))
            .Add(c => c.ReadOnly, true)
            .Add(c => c.OnSave, (EmailSaveRequest _) => Task.FromResult(EmailSaveResult.Saved())));

        var badge = cut.Find("[data-tm-readonly-badge]");
        badge.TextContent.Should().Contain("EmailEditor_ReadOnly_Badge");
        cut.FindAll("[data-tm-save]").Should().BeEmpty("a read-only editor offers nothing to save");
        cut.FindAll("[data-tm-save-badge]").Should().BeEmpty("the read-only badge replaces the save status");
        cut.FindAll("[data-tm-save-retry]").Should().BeEmpty("no call to action may invite an edit it cannot make");
    }

    /// <summary>
    /// The keyboard contract has to be programmatically discoverable: the buttons advertise their chords
    /// through <c>aria-keyshortcuts</c> (the platform syntax, space separated, so both Ctrl and ⌘ work) and
    /// carry a tooltip — icon-only buttons below 1200 px would otherwise have no visible affordance at all.
    /// </summary>
    [Fact]
    public void TheActions_AdvertiseTheirShortcutsAndTitles()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());

        cut.Find("[data-tm-save]").GetAttribute("aria-keyshortcuts").Should().Be("Control+S Meta+S");
        cut.Find("[data-tm-save]").GetAttribute("title").Should().Be("EmailEditor_Toolbar_SaveTitle",
            "the tooltip spells the chord out for a pointer user");
        cut.Find("[data-tm-undo]").GetAttribute("aria-keyshortcuts").Should().Be("Control+Z Meta+Z");
        cut.Find("[data-tm-redo]").GetAttribute("aria-keyshortcuts").Should()
            .Be("Control+Y Control+Shift+Z Meta+Shift+Z");
        cut.Find("[data-tm-undo]").GetAttribute("title").Should().Be("EmailEditor_Toolbar_Undo",
            "icon-only buttons take their tooltip from the same localized accessible name");
        cut.Find("[data-tm-redo]").GetAttribute("title").Should().Be("EmailEditor_Toolbar_Redo");
        cut.Find("[data-tm-help-btn]").GetAttribute("title").Should().Be("EmailEditor_Toolbar_Help");
    }

    /// <summary>
    /// The top-level contract a later responsive phase must keep: the bar is addressable by the stable
    /// data attributes, and the badge never loses its tooltip (a truncated pill has to explain itself).
    /// </summary>
    [Fact]
    public void TheBar_IsAddressableByStableHooks_AndTheBadgeAlwaysCarriesATitle()
    {
        var cut = RenderEditor(DocWithTextBlock(out _), EmailSaveResult.Saved());
        cut.Find("[data-tm-block=\"text\"]").Click();

        foreach (var hook in new[] { "[data-tm-save-chrome]", "[data-tm-save-badge]", "[data-tm-save]", "[data-tm-group=\"history\"]", "[data-tm-group=\"file\"]" })
        {
            cut.FindAll(hook).Should().ContainSingle($"hosts and E2E select the bar through {hook}");
        }

        cut.Find("[data-tm-save-badge]").GetAttribute("title").Should().Be("EmailEditor_SaveStatus_Dirty");
    }
}
