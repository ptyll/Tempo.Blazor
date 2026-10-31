using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Recipient role editor (Signing redesign phase 7, S1-2): roles render as COLLAPSIBLE cards — the
/// header carries the order badge (not a read-only number input), the role pill in the role colour
/// with contrast-safe text, a contact preview, the reorder arrows (kept as the a11y alternative to
/// drag &amp; drop), the remove action and the chevron toggle with <c>aria-expanded</c>. The body
/// edits through core TmTextInput/TmSelect components: the role name stays editable, the palette
/// swatches (+ "Custom…") emit RolesChanged, and the three invite selectors survive (Q-S6 adds NO
/// advanced invitation section).
/// </summary>
public class TmRecipientRoleEditorTests : LocalizationTestBase
{
    [Fact]
    public void RoleEditor_CardToggle_AriaExpanded()
    {
        // The contact preview is a submission-recipients feature (template roles carry only the pill).
        var cut = Render<TmRecipientRoleEditor>(parameters => parameters
            .Add(p => p.Roles, CreateRoles())
            .Add(p => p.Mode, TmRecipientRoleEditorMode.SubmissionRecipients));

        var toggles = cut.FindAll(".tm-recipient-role-editor__toggle");
        toggles.Should().HaveCount(2);
        toggles[0].GetAttribute("aria-expanded").Should().Be("true", "every card starts expanded");
        toggles[1].GetAttribute("aria-expanded").Should().Be("true");

        toggles[1].Click();

        toggles = cut.FindAll(".tm-recipient-role-editor__toggle");
        toggles[1].GetAttribute("aria-expanded").Should().Be("false", "the toggle collapses the card");
        cut.FindAll("[data-role-uuid='role-b'] .tm-recipient-role-editor__card-body").Should().BeEmpty(
            "a collapsed card keeps only its header in the DOM");
        // The collapsed header still previews the contact.
        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__preview").TextContent
            .Should().Contain("Rene Reviewer");

        toggles[1].Click();

        toggles = cut.FindAll(".tm-recipient-role-editor__toggle");
        toggles[1].GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__card-body").Should().NotBeNull();
    }

    [Fact]
    public void RoleEditor_NewRoleExpanded()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, _ => { })));

        cut.Find(".tm-recipient-role-editor__add").Click();

        var toggles = cut.FindAll(".tm-recipient-role-editor__toggle");
        toggles.Should().HaveCount(3);
        toggles[2].GetAttribute("aria-expanded").Should().Be("true", "a newly added role opens for editing");
    }

    [Fact]
    public void RoleEditor_OrderBadgeNotInput()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters => parameters.Add(p => p.Roles, CreateRoles()));

        cut.FindAll(".tm-recipient-role-editor__order-input").Should().BeEmpty(
            "the read-only number input is gone");
        var badges = cut.FindAll(".tm-recipient-role-editor__order-badge [aria-hidden='true']");
        badges.Select(badge => badge.TextContent.Trim()).Should().Equal("1", "2");
    }

    [Fact]
    public void RoleEditor_ArrowsStillReorder()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__move-down").Click();

        captured.Should().NotBeNull();
        captured![0].Uuid.Should().Be("role-b");
        captured[0].Order.Should().Be(0);
        captured[1].Uuid.Should().Be("role-a");
        captured[1].Order.Should().Be(1);
    }

    [Fact]
    public void RoleEditor_SwatchEmitsRolesChanged()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__swatch[data-color='#16a34a']").Click();

        captured.Should().NotBeNull();
        captured!.First(role => role.Uuid == "role-a").Color.Should().Be("#16a34a");

        var swatch = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__swatch[data-color='#16a34a']");
        swatch.GetAttribute("aria-pressed").Should().Be("true", "the active swatch is pressed");
    }

    [Fact]
    public void RoleEditor_UsesTmTextInputAndTmSelect()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.Mode, TmRecipientRoleEditorMode.SubmissionRecipients)
                      .Add(p => p.Fields, CreateFields()));

        cut.FindComponents<Tempo.Blazor.Components.Inputs.TmTextInput>().Should().NotBeEmpty(
            "role name and contact fields are TmTextInput components");
        cut.FindComponents<Tempo.Blazor.Components.Inputs.TmSelect<string>>().Should().HaveCount(6,
            "each of the two cards carries the three invite selectors");
    }

    [Fact]
    public void RoleEditor_RoleNameStillEditable()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        var input = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__name");
        input.Change("Signer A");
        input.Blur();

        captured.Should().NotBeNull();
        captured!.First(role => role.Uuid == "role-a").Name.Should().Be("Signer A");
    }

    [Fact]
    public void Render_RendersRoleList()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        cut.FindAll(".tm-recipient-role-editor__card").Should().HaveCount(2);
        cut.Markup.Should().Contain("Approver");
        cut.Markup.Should().Contain("Reviewer");
    }

    [Fact]
    public void Render_EmptyRolesShowsDefaultRole()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, Array.Empty<SigningSubmitterRole>()));

        cut.FindAll(".tm-recipient-role-editor__card").Should().HaveCount(1);
        cut.Find(".tm-recipient-role-editor__name").GetAttribute("value").Should().Be("Signer 1");
    }

    [Fact]
    public void Render_RoleHasColor()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        var card = cut.Find("[data-role-uuid='role-a']");
        // The pill paints the role colour through the runtime local custom property; the active
        // swatch is pressed and the custom colour input carries the serialized hex.
        card.QuerySelector(".tm-recipient-role-editor__role-pill")!.GetAttribute("style")!
            .Should().Contain("--tm-signing-role-color");
        card.QuerySelector(".tm-recipient-role-editor__swatch[data-color='#2563eb']")!
            .GetAttribute("aria-pressed").Should().Be("true");
        card.QuerySelector(".tm-recipient-role-editor__color")!.GetAttribute("value").Should().Be("#2563eb");
    }

    [Fact]
    public void AddRole_CreatesNewRole()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find(".tm-recipient-role-editor__add").Click();

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(3);
        captured.Last().Name.Should().Be("Signer 3");
        captured.Last().Order.Should().Be(2);
    }

    [Fact]
    public void RemoveRole_RemovesRole()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__remove").Click();

        captured.Should().NotBeNull();
        captured!.Select(role => role.Uuid).Should().NotContain("role-b");
    }

    [Fact]
    public void TemplateRoles_DoesNotRenderRecipientInputs()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.Mode, TmRecipientRoleEditorMode.TemplateRoles));

        cut.Find(".tm-recipient-role-editor").ClassList.Should().Contain("tm-recipient-role-editor--template");
        cut.FindAll(".tm-recipient-role-editor__email").Should().BeEmpty();
        cut.FindAll(".tm-recipient-role-editor__full-name").Should().BeEmpty();
        cut.FindAll(".tm-recipient-role-editor__phone").Should().BeEmpty();
    }

    [Fact]
    public void SubmissionRecipients_RendersRecipientInputs()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.Mode, TmRecipientRoleEditorMode.SubmissionRecipients));

        cut.Find(".tm-recipient-role-editor").ClassList.Should().Contain("tm-recipient-role-editor--submission");
        cut.FindAll(".tm-recipient-role-editor__email").Should().HaveCount(2);
        cut.FindAll(".tm-recipient-role-editor__full-name").Should().HaveCount(2);
        cut.FindAll(".tm-recipient-role-editor__phone").Should().HaveCount(2);
    }

    [Fact]
    public void SubmissionRecipients_EmailInputHasEmailType()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.Mode, TmRecipientRoleEditorMode.SubmissionRecipients));

        cut.Find(".tm-recipient-role-editor__email").GetAttribute("type").Should().Be("email");
    }

    [Fact]
    public void SubmissionRecipients_PhoneInputHasTelType()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.Mode, TmRecipientRoleEditorMode.SubmissionRecipients));

        cut.Find(".tm-recipient-role-editor__phone").GetAttribute("type").Should().Be("tel");
    }

    [Fact]
    public void SubmissionRecipients_RequiredEmailMissing_ShowsValidation()
    {
        var roles = CreateRoles();
        roles[0].Email = null;
        roles[0].IsOptional = false;

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, roles)
                      .Add(p => p.Mode, TmRecipientRoleEditorMode.SubmissionRecipients));

        cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__validation")
            .TextContent.Should().Contain("Email is required");
    }

    [Fact]
    public void DragDrop_ReordersRoles()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find("[data-role-uuid='role-b']").DragStart();
        cut.Find("[data-role-uuid='role-a']").Drop();

        captured.Should().NotBeNull();
        captured![0].Uuid.Should().Be("role-b");
        captured[0].Order.Should().Be(0);
        captured[1].Uuid.Should().Be("role-a");
        captured[1].Order.Should().Be(1);
    }

    [Fact]
    public void InviteByRole_UpdatesRole()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__invite-by-role select").Change("role-a");

        captured.Should().NotBeNull();
        captured!.First(role => role.Uuid == "role-b").InviteByRoleUuid.Should().Be("role-a");
    }

    [Fact]
    public void OptionalInviteByRole_UpdatesRole()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__optional-invite-by-role select").Change("role-a");

        captured.Should().NotBeNull();
        captured!.First(role => role.Uuid == "role-b").OptionalInviteByRoleUuid.Should().Be("role-a");
    }

    [Fact]
    public void InviteViaField_UpdatesRole()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.Fields, CreateFields())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__invite-via-field select").Change("email-field");

        captured.Should().NotBeNull();
        captured!.First(role => role.Uuid == "role-b").InviteViaFieldUuid.Should().Be("email-field");
    }

    [Fact]
    public void InviteSelectors_DoNotOfferSelfReference()
    {
        var roles = CreateRoles();
        roles[0].InviteByRoleUuid = "role-a";

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, roles));

        var select = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__invite-by-role select");
        (select.GetAttribute("value") ?? string.Empty).Should().Be(string.Empty);
        select.QuerySelectorAll("option")
            .Select(option => option.GetAttribute("value"))
            .Should()
            .NotContain("role-a");
    }

    // ── Role pill contrast (phase 7 review, MAJOR 1) ────────────────────────────────

    [Fact]
    public void Pill_DefaultPaletteColour_CarriesTheOnToken()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        var pill = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__role-pill");
        var style = pill.GetAttribute("style")!;
        style.Should().Contain("--tm-signing-role-color: var(--tm-signing-role-1)",
            "the paint follows the theme (and a host rebrand)");
        style.Should().Contain("color: var(--tm-signing-role-1-on)",
            "the ink is the per-theme -on token, never a computed hex");
        pill.ClassList.Should().NotContain(match => match.Contains("--on-"),
            "a token-inked pill needs no tone class");
    }

    [Fact]
    public void Pill_CustomLightColour_TakesTheOnLightClassWithoutInlineHex()
    {
        var roles = CreateRoles();
        roles[0].Color = "#fef3c7";

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, roles));

        var pill = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__role-pill");
        pill.GetAttribute("style")!.Should().Contain("--tm-signing-role-color: #fef3c7")
            .And.NotContain("; color:", "a custom paint's fixed ink rides the tone class, not an inline hex");
        pill.ClassList.Should().Contain("tm-recipient-role-editor__role-pill--on-light");
    }

    [Fact]
    public void Pill_CustomDarkColour_TakesTheOnDarkClass()
    {
        var roles = CreateRoles();
        roles[0].Color = "#1f2937";

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, roles));

        var pill = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__role-pill");
        pill.GetAttribute("style")!.Should().NotContain("; color:");
        pill.ClassList.Should().Contain("tm-recipient-role-editor__role-pill--on-dark");
    }

    /// <summary>A host token string where concrete colour data was expected paints nothing custom:
    /// the effective paint is the primary fallback, so the on-primary token carries the ink.</summary>
    [Fact]
    public void Pill_HostTokenColour_RidesTheOnPrimaryToken()
    {
        var roles = CreateRoles();
        roles[0].Color = "var(--tm-signing-role-6)";

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, roles));

        cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__role-pill")
            .GetAttribute("style")!.Should().Contain("color: var(--tm-color-on-primary)");
    }

    // ── Header semantics (phase 7 review, MINOR 6/7/8) ──────────────────────────────

    [Fact]
    public void OrderBadge_CarriesVisuallyHiddenLabel_AndDecorativeNumber()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        var badge = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__order-badge");
        badge.QuerySelector(".tm-sr-only")!.TextContent.Trim().Should().Be("Signing order 1",
            "the semantic label is visually hidden text, not a title tooltip");
        badge.QuerySelector("[aria-hidden='true']")!.TextContent.Trim().Should().Be("1",
            "the visible number is decorative");
    }

    [Fact]
    public void Toggle_AriaControls_OnlyWhileExpanded()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        var toggle = cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__toggle");
        toggle.GetAttribute("aria-controls").Should().NotBeNullOrEmpty("an expanded card owns its body id");

        toggle.Click();

        toggle = cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__toggle");
        toggle.GetAttribute("aria-controls").Should().BeNull(
            "a collapsed card renders no body — aria-controls must not reference a missing id");
    }

    [Fact]
    public void Swatches_CarryLocalizedColourNames()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        var labels = cut.FindAll("[data-role-uuid='role-a'] .tm-recipient-role-editor__swatch")
            .Select(swatch => swatch.GetAttribute("aria-label"))
            .ToArray();
        labels.Should().HaveCount(6);
        labels.Should().Contain("Colour Blue");
        labels.Should().Contain("Colour Green");
        labels.Should().Contain("Colour Red");
        labels.Should().Contain("Colour Purple");
        labels.Should().Contain("Colour Cyan");
        labels.Should().Contain("Colour Amber");
    }

    // ── Deferred commit while typing (phase 7 review, MAJOR 3) ──────────────────────

    [Fact]
    public void NameInput_ClearingKeepsTheFieldEmpty_AndDoesNotNotifyPerKeystroke()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        var input = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__name");
        input.Input(string.Empty);

        // The raw (emptied) text stays on screen while typing — no snap-back to "Signer 1".
        input.GetAttribute("value").Should().BeEmpty();
        captured.Should().BeNull("a keystroke must not raise RolesChanged");
        cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__role-pill").TextContent
            .Should().Be("Approver", "the committed value is untouched while the draft is live");
    }

    [Fact]
    public void NameInput_BlurCommitsAndNormalizes()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles())
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, roles => captured = roles)));

        var input = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__name");
        input.Input(string.Empty);
        input.Blur();

        captured.Should().NotBeNull("the blur is the commit");
        captured!.First(role => role.Uuid == "role-a").Name.Should().Be("Signer 1",
            "the committed empty name normalizes to the default");
        cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__name").GetAttribute("value")
            .Should().Be("Signer 1");
    }

    [Fact]
    public void EmailInput_TypingIsRaw_BlurCommits()
    {
        IReadOnlyList<SigningSubmitterRole>? captured = null;
        var roles = CreateRoles();
        roles[0].Email = null;
        roles[0].IsOptional = true;

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, roles)
                      .Add(p => p.Mode, TmRecipientRoleEditorMode.SubmissionRecipients)
                      .Add(p => p.RolesChanged, EventCallback.Factory.Create<IReadOnlyList<SigningSubmitterRole>>(this, value => captured = value)));

        var input = cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__email");
        input.Input("a");
        captured.Should().BeNull("no notification while typing");

        input.Input("a@b.c");
        input.Blur();

        captured.Should().NotBeNull();
        captured!.First(role => role.Uuid == "role-a").Email.Should().Be("a@b.c");
    }

    // ── Focus hand-off + announcements (phase 7 review, MAJOR 2) ────────────────────

    [Fact]
    public void RemoveMiddleRole_FocusesTheNextCardsToggle()
    {
        var roles = CreateRoles();
        roles.Add(new SigningSubmitterRole { Uuid = "role-c", Name = "Closer", Order = 2 });

        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        var cut = Render<TmRecipientRoleEditor>(parameters => parameters.Add(p => p.Roles, roles));

        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__remove").Click();
        cut.Render(); // the host re-render that follows RolesChanged — the queued focus applies

        // The card that now occupies the removed position is role-c — its toggle takes the focus.
        module.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "focusElement"
            && invocation.Arguments[0].ToString()!.Contains("[data-role-uuid='role-c']")
            && invocation.Arguments[0].ToString()!.Contains(".tm-recipient-role-editor__toggle"));
    }

    [Fact]
    public void MoveRoleDown_FocusesTheMovedCard_AndAnnouncesTheNewPosition()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        cut.Find("[data-role-uuid='role-a'] .tm-recipient-role-editor__move-down").Click();
        cut.Render(); // the host re-render that follows RolesChanged — the queued focus applies

        // role-a moved to the last position — its move-down disabled itself there, so the focus
        // moves to the OPPOSITE (still enabled) button inside the moved card.
        module.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "focusElement"
            && invocation.Arguments[0].ToString()!.Contains("[data-role-uuid='role-a']")
            && invocation.Arguments[0].ToString()!.Contains(".tm-recipient-role-editor__move-up"));

        // The polite live region (always rendered) announces the result.
        cut.Find(".tm-recipient-role-editor .tm-sr-only[aria-live='polite']").TextContent
            .Should().Be("Approver moved to position 2");
    }

    [Fact]
    public void MoveRoleUpIntoTheTopSlot_FocusesTheOppositeButton()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", _ => true).SetResult(true);

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__move-up").Click();
        cut.Render();

        module.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "focusElement"
            && invocation.Arguments[0].ToString()!.Contains("[data-role-uuid='role-b']")
            && invocation.Arguments[0].ToString()!.Contains(".tm-recipient-role-editor__move-down"),
            "the move-up disabled itself at the top slot, so the focus lands on the move-down");
    }

    [Fact]
    public void FocusMiss_FallsBackToTheAddAction()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor.Signing/js/signing-editor.js");
        module.Setup<bool>("focusElement", invocation => invocation.Arguments.OfType<string>().Single().Contains(".tm-recipient-role-editor__add"))
            .SetResult(true);
        module.Setup<bool>("focusElement", invocation => !invocation.Arguments.OfType<string>().Single().Contains(".tm-recipient-role-editor__add"))
            .SetResult(false);

        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        cut.Find("[data-role-uuid='role-b'] .tm-recipient-role-editor__remove").Click();
        cut.Render();

        module.Invocations.Where(invocation => invocation.Identifier == "focusElement")
            .Select(invocation => invocation.Arguments[0].ToString())
            .Should().Contain(selector => selector!.Contains(".tm-recipient-role-editor__add"),
                "a failed primary focus move falls back to the Add action");
    }

    [Fact]
    public void Cards_CarryStableUuidKeys()
    {
        var cut = Render<TmRecipientRoleEditor>(parameters =>
            parameters.Add(p => p.Roles, CreateRoles()));

        cut.FindAll(".tm-recipient-role-editor__card")
            .Should().OnlyContain(card => card.HasAttribute("data-role-uuid"),
                "every card carries the stable uuid the @key and the focus selectors ride on");
    }

    private static List<SigningSubmitterRole> CreateRoles()
    {
        return
        [
            new SigningSubmitterRole
            {
                Uuid = "role-a",
                Name = "Approver",
                Color = "#2563eb",
                Email = "approver@example.test",
                FullName = "Ada Approver",
                Phone = "+420555010101",
                Order = 0
            },
            new SigningSubmitterRole
            {
                Uuid = "role-b",
                Name = "Reviewer",
                Color = "#16a34a",
                Email = "reviewer@example.test",
                FullName = "Rene Reviewer",
                Phone = "+420555010102",
                Order = 1,
                IsOptional = true
            }
        ];
    }

    private static IReadOnlyList<SigningField> CreateFields()
    {
        return
        [
            new SigningField
            {
                Uuid = "email-field",
                Name = "Manager email",
                Type = SigningFieldType.Text
            }
        ];
    }
}
