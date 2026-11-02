using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>
/// TDD tests for <c>TmUserMenu</c> — the account menu (plan 5d396592, Fáze 2). The menu is an APG
/// menu button rendered over the core <c>TmOverlayPanel</c>: the panel owns <c>role="menu"</c>,
/// which is what attaches the shared <c>tm-menu-nav.js</c> engine (arrows, Home/End, typeahead,
/// roving tabindex) and what hands focus back to the trigger on every dismissal. These tests pin
/// the markup contract, the entry semantics, the add-on sections (profile completion, theme
/// toggle, extra slot), the mobile sheet and the focus rules — the keyboard engine itself is
/// covered by <c>tm-menu-nav.test.mjs</c> and the E2E lane.
/// </summary>
public class TmUserMenuTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";
    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";
    private const string MenuNavModulePath = "./_content/Tempo.Blazor/js/tm-menu-nav.js";

    private sealed record FakeUser(string DisplayName, string? Mail = "jane@example.com") : IUserInfo
    {
        public string Id => "u-1";
        public string? AvatarSrc => null;
        public string? Email => Mail;
    }

    private static IUserInfo Jane => new FakeUser("Jane Doe");

    /// <summary>Registers the overlay.js / tm-menu-nav.js module mocks the panel attaches.</summary>
    private BunitJSModuleInterop SetupMenuModules()
    {
        var overlay = JSInterop.SetupModule(OverlayModulePath);
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();
        var nav = JSInterop.SetupModule(MenuNavModulePath);
        nav.SetupVoid("attach", _ => true).SetVoidResult();
        nav.Setup<bool>("focusFirst", _ => true).SetResult(true);
        nav.Setup<bool>("focusLast", _ => true).SetResult(true);
        return nav;
    }

    private int FocusCalls() => JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation);

    private IRenderedComponent<TmUserMenu> RenderMenu(
        Action<ComponentParameterCollectionBuilder<TmUserMenu>>? configure = null)
    {
        var cut = Render<TmUserMenu>(p =>
        {
            p.Add(x => x.User, Jane);
            configure?.Invoke(p);
        });
        return cut;
    }

    private IRenderedComponent<TmUserMenu> RenderOpenMenu(
        Action<ComponentParameterCollectionBuilder<TmUserMenu>>? configure = null)
    {
        var cut = RenderMenu(configure);
        cut.Find(".tm-user-menu__trigger").Click();
        return cut;
    }

    // ── Trigger ────────────────────────────────────────────────────────────────

    [Fact]
    public void Trigger_Renders_The_Avatar_And_The_Display_Name()
    {
        var cut = RenderMenu();

        var trigger = cut.Find(".tm-user-menu__trigger");
        trigger.TagName.Should().Be("BUTTON", "a native button is activated by Enter/Space exactly once");
        trigger.QuerySelector(".tm-avatar").Should().NotBeNull();
        cut.Find(".tm-user-menu__trigger-name").TextContent.Trim().Should().Be("Jane Doe");
    }

    [Fact]
    public void Trigger_Advertises_The_Menu_Popup_And_Its_State()
    {
        var cut = RenderMenu();

        var trigger = cut.Find(".tm-user-menu__trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.GetAttribute("aria-label").Should().Be("Jane Doe — user menu",
            "the visible display name appears in the accessible name (WCAG 2.5.3 Label in Name)");
    }

    [Fact]
    public void Trigger_Label_Keeps_The_Visible_Name_Inside_It()
    {
        var cut = RenderMenu();

        var label = cut.Find(".tm-user-menu__trigger").GetAttribute("aria-label");
        var visibleName = cut.Find(".tm-user-menu__trigger-name").TextContent.Trim();

        label.Should().Contain(visibleName,
            "a speech-input user says the text they see — the accessible name has to contain it");
        label.Should().Contain("user menu", "and it still says what the control opens");
    }

    [Fact]
    public void Trigger_Name_Can_Be_Hidden_For_An_Avatar_Only_Trigger()
    {
        var cut = RenderMenu(p => p.Add(x => x.ShowUserName, false));

        cut.FindAll(".tm-user-menu__trigger-name").Should().BeEmpty();
        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-label").Should().Be("User menu",
            "the accessible name never depends on the visual affordance");
    }

    [Fact]
    public void Trigger_Host_Label_Wins_Over_The_Localized_Default()
    {
        var cut = RenderMenu(p => p.Add(x => x.AriaLabel, "Signed-in account"));

        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-label").Should().Be("Signed-in account");
    }

    [Fact]
    public void Trigger_Works_Without_A_User_So_A_Host_Can_Render_Items_Only()
    {
        var cut = Render<TmUserMenu>(p => p
            .Add(x => x.Items, new[] { new UserMenuItem("help", "Help") }));

        cut.FindAll(".tm-avatar").Should().BeEmpty();
        cut.FindAll(".tm-user-menu__trigger-name").Should().BeEmpty();
        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-label").Should().Be("User menu");
    }

    // ── Menu surface ───────────────────────────────────────────────────────────

    [Fact]
    public void Menu_Opens_As_A_Role_Menu_Named_By_The_Localized_Label()
    {
        var cut = RenderOpenMenu();

        var menu = cut.Find("[role='menu']");
        menu.GetAttribute("aria-label").Should().Be("User menu");
        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Trigger_AriaControls_Points_At_The_Open_Panel()
    {
        var cut = RenderOpenMenu();

        var controls = cut.Find(".tm-user-menu__trigger").GetAttribute("aria-controls");
        controls.Should().NotBeNullOrEmpty();
        cut.Find("[role='menu']").GetAttribute("id").Should().Be(controls);
    }

    [Fact]
    public void Menu_Body_Is_Presentational_So_The_Menu_Only_Owns_Menu_Roles()
    {
        var cut = RenderOpenMenu();

        // A role="menu" may only own menuitem / menuitemcheckbox / menuitemradio / separator /
        // group children (axe aria-required-children); the layout wrapper must not appear as one.
        var body = cut.Find(".tm-user-menu__body");
        body.GetAttribute("role").Should().Be("none");
        string[] allowedChildren = ["menuitem", "menuitemcheckbox", "menuitemradio", "separator", "group"];
        body.Children
            .Where(child => !allowedChildren.Contains(child.GetAttribute("role") ?? string.Empty))
            .Should().BeEmpty();
    }

    [Fact]
    public void Menu_Height_Is_Constrained_So_A_Long_Account_Menu_Scrolls()
    {
        var cut = RenderOpenMenu();

        cut.FindComponent<TmOverlayPanel>().Instance.ConstrainHeight.Should().BeTrue(
            "the panel caps its height at the room on the resolved side and scrolls the list");
    }

    [Fact]
    public void Renders_The_User_Card_With_Name_And_Email()
    {
        var cut = RenderOpenMenu();

        var card = cut.FindAll("[role='group']")
            .Single(group => group.GetAttribute("aria-label") == "Signed-in account");
        card.TextContent.Should().Contain("Jane Doe");
        card.TextContent.Should().Contain("jane@example.com");
        card.QuerySelector(".tm-avatar").Should().NotBeNull();
    }

    [Fact]
    public void User_Card_Is_Skipped_Without_A_User()
    {
        var cut = Render<TmUserMenu>(p => p.Add(x => x.Items, new[] { new UserMenuItem("help", "Help") }));
        cut.Find(".tm-user-menu__trigger").Click();

        cut.FindAll("[role='group']")
            .Where(group => group.GetAttribute("aria-label") == "Signed-in account")
            .Should().BeEmpty();
    }

    [Fact]
    public void Entries_Render_As_MenuItems_With_A_Roving_Tab_Stop()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("help", "Help"),
            new UserMenuItem("billing", "Billing"),
        }));

        var items = cut.FindAll("[role='menuitem']");
        items.Should().HaveCount(2);
        foreach (var item in items)
        {
            item.GetAttribute("tabindex").Should().Be("-1",
                "the shared menu engine moves the single tab stop with the focus ring");
        }
    }

    [Fact]
    public void Separator_Entries_Render_As_Separators_And_Are_Not_Menu_Items()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("help", "Help"),
            UserMenuItem.CreateSeparator("grp"),
            new UserMenuItem("billing", "Billing"),
        }));

        cut.FindAll("[role='separator']").Should().HaveCount(1);
        cut.FindAll("[role='menuitem']").Should().HaveCount(2);
    }

    [Fact]
    public void Danger_Entries_Carry_The_Destructive_Style()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("delete", "Delete account", isDanger: true),
        }));

        cut.Find("[role='menuitem']").ClassList.Should().Contain("tm-user-menu__item--danger");
    }

    // ── Row keys (a duplicate @key is a render exception for the whole host) ──

    [Fact]
    public void Id_less_Host_Entries_Render_Without_A_Duplicate_Key_Exception()
    {
        // UserMenuItem.Id defaults to string.Empty, so a host that never sets it has two entries with
        // the same value. All rows are SIBLINGS under .tm-user-menu__body: an @key collision there
        // throws "more than one sibling has the same @key value" and takes the host shell down, so the
        // key carries the position as well.
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem { Label = "A" },
            new UserMenuItem { Label = "B" },
        }));

        var items = cut.FindAll("[role='menuitem']");
        items.Should().HaveCount(2);
        items.Select(item => item.TextContent.Trim()).Should().Equal(new[] { "A", "B" });
    }

    [Fact]
    public void Id_less_Host_Separators_Render_Without_A_Duplicate_Key_Exception()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("a", "A"),
            new UserMenuItem { Separator = true },
            new UserMenuItem("b", "B"),
            new UserMenuItem { Separator = true },
            new UserMenuItem("c", "C"),
        }));

        cut.FindAll("[role='separator']").Should().HaveCount(2,
            "two gaps between three groups — both separators are keyed by position, since neither carries an Id");
        cut.FindAll("[role='menuitem']").Should().HaveCount(3);
    }

    [Fact]
    public void A_Numeric_Host_Separator_Id_Does_Not_Collide_With_A_Built_In_Section_Key()
    {
        // The built-in sections are separated by their own key namespace (section-sep-{n}); a host
        // separator whose Id is a plain number used to be able to produce the very same key.
        var cut = RenderOpenMenu(p => p
            .Add(x => x.ProfileHref, "/profile")
            .Add(x => x.Items, new[]
            {
                new UserMenuItem("docs", "Documentation"),
                UserMenuItem.CreateSeparator("1"),
                new UserMenuItem("billing", "Billing"),
            }));

        cut.FindAll("[role='separator']").Should().HaveCount(2,
            "one built-in separator above the host section, one inside it");
        cut.Find("[data-testid='user-menu-item-billing']").TextContent.Trim().Should().Be("Billing");
    }

    [Fact]
    public void Host_Entry_Test_Ids_Stay_Derived_From_The_Id_Alone()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("help", "Help"),
            new UserMenuItem("billing", "Billing"),
        }));

        cut.FindAll("[data-testid='user-menu-item-help']").Should().HaveCount(1,
            "the position is part of the @key, never of the test id a host selector depends on");
        cut.FindAll("[data-testid='user-menu-item-billing']").Should().HaveCount(1);
    }

    [Fact]
    public void Href_Entries_Render_As_Links_And_Command_Entries_As_Buttons()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("docs", "Documentation", href: "/docs"),
            new UserMenuItem("help", "Help"),
        }));

        var link = cut.Find("[data-testid='user-menu-item-docs']");
        link.TagName.Should().Be("A");
        link.GetAttribute("href").Should().Be("/docs");

        cut.Find("[data-testid='user-menu-item-help']").TagName.Should().Be("BUTTON");
    }

    [Fact]
    public void Href_Wins_Over_OnClick_On_An_Entry_That_Has_Both()
    {
        var clicked = 0;
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("docs", "Documentation", href: "/docs", onClick: () => { clicked++; return Task.CompletedTask; }),
        }));

        cut.Find("[data-testid='user-menu-item-docs']").Click();

        clicked.Should().Be(0, "the model contract says OnClick is ignored when Href is set — the entry navigates");
    }

    [Fact]
    public void Icon_Entries_Render_Their_Icon()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[]
        {
            new UserMenuItem("help", "Help", icon: "settings"),
        }));

        cut.Find("[data-testid='user-menu-item-help'] svg").Should().NotBeNull();
    }

    // ── Item activation ────────────────────────────────────────────────────────

    [Fact]
    public void Item_Command_Closes_The_Menu_Then_Raises_OnItemSelected_After_OnClick()
    {
        var calls = new List<string>();
        var selected = new List<UserMenuItem>();
        var item = new UserMenuItem("help", "Help", onClick: () => { calls.Add("OnClick"); return Task.CompletedTask; });

        var cut = RenderOpenMenu(p => p
            .Add(x => x.Items, new[] { item })
            .Add(x => x.OnItemSelected, EventCallback.Factory.Create<UserMenuItem>(this, i =>
            {
                calls.Add("OnItemSelected");
                selected.Add(i);
            })));

        cut.Find("[data-testid='user-menu-item-help']").Click();

        calls.Should().Equal(new[] { "OnClick", "OnItemSelected" },
            "the host callback runs after the awaited command so it can render the result");
        selected.Should().ContainSingle().Which.Should().BeSameAs(item);
        cut.FindAll("[role='menu']").Should().BeEmpty("choosing an entry closes the menu");
        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Item_Link_Closes_The_Menu_And_Raises_OnItemSelected()
    {
        UserMenuItem? selected = null;
        var cut = RenderOpenMenu(p => p
            .Add(x => x.Items, new[] { new UserMenuItem("docs", "Documentation", href: "/docs") })
            .Add(x => x.OnItemSelected, EventCallback.Factory.Create<UserMenuItem>(this, i => selected = i)));

        cut.Find("[data-testid='user-menu-item-docs']").Click();

        selected.Should().NotBeNull();
        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void Choosing_An_Entry_Returns_Focus_To_The_Trigger()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.Items, new[] { new UserMenuItem("help", "Help") }));

        cut.Find("[data-testid='user-menu-item-help']").Click();

        cut.WaitForAssertion(() => FocusCalls().Should().Be(1));
    }

    // ── Built-in entries ───────────────────────────────────────────────────────

    [Fact]
    public void Sign_Out_Renders_Only_When_The_Host_Binds_A_Handler()
    {
        var without = RenderOpenMenu();
        without.FindAll("[data-testid='user-menu-sign-out']").Should().BeEmpty();

        var with = RenderOpenMenu(p => p.Add(x => x.OnLogout, EventCallback.Factory.Create(this, () => { })));
        with.Find("[data-testid='user-menu-sign-out']").TextContent.Trim().Should().Be("Sign out");
    }

    [Fact]
    public void Sign_Out_Closes_The_Menu_And_Raises_OnLogout()
    {
        var loggedOut = 0;
        var cut = RenderOpenMenu(p => p
            .Add(x => x.OnLogout, EventCallback.Factory.Create(this, () => loggedOut++)));

        cut.Find("[data-testid='user-menu-sign-out']").Click();

        loggedOut.Should().Be(1);
        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void Profile_And_Settings_Entries_Render_From_Their_Hrefs()
    {
        var cut = RenderOpenMenu(p => p
            .Add(x => x.ProfileHref, "/profile")
            .Add(x => x.SettingsHref, "/settings"));

        var profile = cut.Find("[data-testid='user-menu-profile']");
        profile.TagName.Should().Be("A");
        profile.GetAttribute("href").Should().Be("/profile");
        profile.TextContent.Trim().Should().Be("Profile");

        cut.Find("[data-testid='user-menu-settings']").GetAttribute("href").Should().Be("/settings");
        cut.Find("[data-testid='user-menu-settings']").TextContent.Trim().Should().Be("Settings");
    }

    [Fact]
    public void Profile_And_Settings_Are_Skipped_Without_An_Href()
    {
        var cut = RenderOpenMenu();

        cut.FindAll("[data-testid='user-menu-profile']").Should().BeEmpty();
        cut.FindAll("[data-testid='user-menu-settings']").Should().BeEmpty();
    }

    // ── Profile completion ─────────────────────────────────────────────────────

    [Fact]
    public void Profile_Completion_Renders_A_Progress_Bar_With_The_Localized_Text()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.ProfileCompletion, 40));

        var bar = cut.Find("[role='progressbar']");
        bar.GetAttribute("aria-valuenow").Should().Be("40");
        bar.GetAttribute("aria-valuemax").Should().Be("100");
        bar.GetAttribute("aria-label").Should().Be("Profile completion");

        cut.Find(".tm-user-menu__completion-text").TextContent.Trim().Should().Be("Profile 40% complete");
    }

    [Fact]
    public void Profile_Completion_Is_Skipped_When_Not_Set()
    {
        var cut = RenderOpenMenu();

        cut.FindAll("[role='progressbar']").Should().BeEmpty();
        cut.FindAll(".tm-user-menu__completion-text").Should().BeEmpty();
    }

    [Theory]
    [InlineData(140, "100")]
    [InlineData(-5, "0")]
    [InlineData(0, "0")]
    public void Profile_Completion_Is_Clamped_To_The_Percentage_Range(int value, string expected)
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.ProfileCompletion, value));

        cut.Find("[role='progressbar']").GetAttribute("aria-valuenow").Should().Be(expected);
    }

    [Fact]
    public void Profile_Completion_Action_Renders_As_A_Menu_Item_Link()
    {
        var cut = RenderOpenMenu(p => p
            .Add(x => x.ProfileCompletion, 40)
            .Add(x => x.ProfileCompletionHref, "/profile/edit"));

        var action = cut.Find("[data-testid='user-menu-complete-profile']");
        action.GetAttribute("role").Should().Be("menuitem",
            "a control inside a menu that is not a menu item is unreachable in menu interaction mode");
        action.TagName.Should().Be("A");
        action.GetAttribute("href").Should().Be("/profile/edit");
        action.TextContent.Trim().Should().Be("Complete your profile");
    }

    [Fact]
    public void Profile_Completion_Action_Raises_The_Callback_And_Closes()
    {
        var raised = 0;
        var cut = RenderOpenMenu(p => p
            .Add(x => x.ProfileCompletion, 40)
            .Add(x => x.OnProfileCompletion, EventCallback.Factory.Create(this, () => raised++)));

        cut.Find("[data-testid='user-menu-complete-profile']").Click();

        raised.Should().Be(1);
        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void Profile_Completion_Link_Wins_Over_The_Callback_When_Both_Are_Set()
    {
        var raised = 0;
        var cut = RenderOpenMenu(p => p
            .Add(x => x.ProfileCompletion, 40)
            .Add(x => x.ProfileCompletionHref, "/profile/edit")
            .Add(x => x.OnProfileCompletion, EventCallback.Factory.Create(this, () => raised++)));

        cut.Find("[data-testid='user-menu-complete-profile']").Click();

        raised.Should().Be(0, "one action per activation: the link navigates, the command callback does not run");
    }

    [Fact]
    public void Profile_Completion_Action_Is_Skipped_At_One_Hundred_Percent()
    {
        var cut = RenderOpenMenu(p => p
            .Add(x => x.ProfileCompletion, 100)
            .Add(x => x.ProfileCompletionHref, "/profile/edit"));

        cut.Find("[role='progressbar']").GetAttribute("aria-valuenow").Should().Be("100");
        cut.FindAll("[data-testid='user-menu-complete-profile']").Should().BeEmpty(
            "there is nothing left to complete");
    }

    // ── Theme toggle ───────────────────────────────────────────────────────────

    [Fact]
    public void Theme_Toggle_Is_Opt_In()
    {
        Services.AddSingleton(new ThemeService());
        var cut = RenderOpenMenu();

        cut.FindAll("[data-testid='user-menu-theme']").Should().BeEmpty();
    }

    [Fact]
    public void Theme_Toggle_Is_A_Menu_Item_Checkbox_That_Follows_The_ThemeService()
    {
        var theme = new ThemeService();
        Services.AddSingleton(theme);
        var cut = RenderOpenMenu(p => p.Add(x => x.ShowThemeToggle, true));

        var toggle = cut.Find("[data-testid='user-menu-theme']");
        toggle.GetAttribute("role").Should().Be("menuitemcheckbox");
        toggle.GetAttribute("aria-checked").Should().Be("false");
        toggle.TextContent.Trim().Should().Be("Dark theme");

        toggle.Click();

        theme.IsDark.Should().BeTrue();
        cut.Find("[data-testid='user-menu-theme']").GetAttribute("aria-checked").Should().Be("true");
        cut.FindAll("[role='menu']").Should().HaveCount(1,
            "a checkbox entry reports a state change instead of dismissing the menu");
    }

    [Fact]
    public void Theme_Toggle_Reflects_A_Theme_Changed_Elsewhere()
    {
        var theme = new ThemeService();
        Services.AddSingleton(theme);
        var cut = RenderOpenMenu(p => p.Add(x => x.ShowThemeToggle, true));

        cut.InvokeAsync(() => theme.Toggle());

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='user-menu-theme']").GetAttribute("aria-checked").Should().Be("true"));
    }

    [Fact]
    public void Theme_Toggle_Is_Hidden_When_No_ThemeService_Is_Registered()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.ShowThemeToggle, true));

        cut.FindAll("[data-testid='user-menu-theme']").Should().BeEmpty(
            "without the service there is no theme to report or switch");
    }

    [Fact]
    public void Theme_Entry_Keeps_Its_Icon_When_The_Theme_Flips()
    {
        var theme = new ThemeService();
        Services.AddSingleton(theme);
        var cut = RenderOpenMenu(p => p.Add(x => x.ShowThemeToggle, true));

        var iconMarkup = cut.Find("[data-testid='user-menu-theme'] svg").InnerHtml;
        cut.Find(".tm-user-menu__item-check").Should().NotBeNull(
            "the trailing checkmark is the state signal on its own");

        cut.InvokeAsync(() => theme.Toggle());
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='user-menu-theme']").GetAttribute("aria-checked").Should().Be("true"));

        cut.Find("[data-testid='user-menu-theme'] svg").InnerHtml.Should().Be(iconMarkup,
            "the label still says \"Dark theme\"; a sun icon next to it is a second, contradicting signal");
    }

    // ── Extra slot ─────────────────────────────────────────────────────────────

    [Fact]
    public void Extra_Content_Renders_Inside_A_Named_Group()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.ExtraContent,
            (RenderFragment)(b => b.AddMarkupContent(0, "<button class=\"probe-language\">EN</button>"))));

        cut.Find(".probe-language").Should().NotBeNull();
        var group = cut.Find(".tm-user-menu__extra");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be("Account options");
    }

    // ── Dismissal and focus ────────────────────────────────────────────────────

    [Fact]
    public async Task Escape_Dismissal_Closes_The_Menu_And_Leaves_Focus_Restoration_To_The_Overlay()
    {
        var cut = RenderOpenMenu();

        await cut.InvokeAsync(() => cut.FindComponent<TmOverlayPanel>().Instance.NotifyDismissedAsync("escape"));

        cut.FindAll("[role='menu']").Should().BeEmpty();
        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-expanded").Should().Be("false");
        FocusCalls().Should().Be(0,
            "overlay.js already refocused the anchor — a second Blazor focus would fight it");
    }

    [Fact]
    public async Task Outside_Pointer_Dismissal_Closes_The_Menu()
    {
        var cut = RenderOpenMenu();

        await cut.InvokeAsync(() => cut.FindComponent<TmOverlayPanel>().Instance.NotifyDismissedAsync("outside"));

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void Clicking_The_Trigger_Again_Closes_The_Menu()
    {
        var cut = RenderOpenMenu();

        cut.Find(".tm-user-menu__trigger").Click();

        cut.FindAll("[role='menu']").Should().BeEmpty();
        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    // ── Keyboard entry ─────────────────────────────────────────────────────────

    [Fact]
    public void ArrowDown_On_The_Trigger_Opens_The_Menu_And_Focuses_Its_First_Entry()
    {
        var nav = SetupMenuModules();
        var cut = RenderMenu();

        cut.Find(".tm-user-menu__trigger").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.FindAll("[role='menu']").Should().HaveCount(1);
        cut.WaitForAssertion(() => nav.Invocations["focusFirst"].Should().ContainSingle());
    }

    [Fact]
    public void ArrowUp_On_The_Trigger_Opens_The_Menu_And_Focuses_Its_Last_Entry()
    {
        var nav = SetupMenuModules();
        var cut = RenderMenu();

        cut.Find(".tm-user-menu__trigger").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        cut.FindAll("[role='menu']").Should().HaveCount(1);
        cut.WaitForAssertion(() => nav.Invocations["focusLast"].Should().ContainSingle());
    }

    [Fact]
    public void ArrowDown_On_An_Already_Open_Menu_Moves_The_Focus_To_The_First_Entry()
    {
        // The same "first or last" hand-off runs on the opening (OnOpened) and on the arrow of an
        // already-open menu; both go through one helper, and both have to reach the engine.
        var nav = SetupMenuModules();
        var cut = RenderMenu();
        cut.Find(".tm-user-menu__trigger").Click();
        cut.WaitForAssertion(() => nav.Invocations["focusFirst"].Should().ContainSingle());
        var before = nav.Invocations["focusFirst"].Count;

        cut.Find(".tm-user-menu__trigger").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => nav.Invocations["focusFirst"].Should().HaveCount(before + 1));
    }

    [Fact]
    public void Enter_And_Space_Are_Not_Emulated_On_The_Native_Button()
    {
        var cut = RenderMenu();

        // A native <button> turns Enter/Space into a click itself; emulating them here would fire
        // the toggle twice (docs/keyboard-activation-convention.md).
        cut.Find(".tm-user-menu__trigger").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    // ── Mobile sheet ───────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_Presentation_Defaults_To_Auto()
    {
        var cut = RenderMenu();

        cut.Instance.MobilePresentation.Should().Be(PanelPresentation.Auto,
            "the account menu is a content-height sheet on a mobile viewport");
    }

    [Fact]
    public void Forced_Sheet_Presentation_Renders_The_Same_Menu_Inside_A_Sheet()
    {
        var cut = RenderOpenMenu(p => p.Add(x => x.MobilePresentation, PanelPresentation.Sheet));

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Find(".tm-overlay-panel-sheet__content[role='menu']").Should().NotBeNull(
            "the menu role survives the popover → sheet switch (axe aria-required-parent)");
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty("the sheet replaces the popover, it does not stack");
    }

    [Fact]
    public void Forced_Sheet_Presentation_Does_Not_Restore_Focus_From_Blazor()
    {
        var cut = RenderOpenMenu(p => p
            .Add(x => x.MobilePresentation, PanelPresentation.Sheet)
            .Add(x => x.Items, new[] { new UserMenuItem("help", "Help") }));

        cut.Find("[data-testid='user-menu-item-help']").Click();

        FocusCalls().Should().Be(0,
            "the drawer owns focus restoration through its RestoreFocusTarget, so a second focus would race it");
    }

    // ── Localization ───────────────────────────────────────────────────────────

    [Fact]
    public void Czech_Localization_Translates_Every_String_The_Menu_Renders()
    {
        UseCzechLocalization();
        var cut = RenderOpenMenu(p => p
            .Add(x => x.ProfileCompletion, 40)
            .Add(x => x.ProfileCompletionHref, "/profil")
            .Add(x => x.ProfileHref, "/profil")
            .Add(x => x.OnLogout, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-user-menu__trigger").GetAttribute("aria-label").Should().Be("Jane Doe — uživatelské menu");
        cut.Find("[data-testid='user-menu-sign-out']").TextContent.Trim().Should().Be("Odhlásit se");
        cut.Find(".tm-user-menu__completion-text").TextContent.Trim().Should().Be("Profil je vyplněný na 40 %");
        cut.Find("[data-testid='user-menu-profile']").TextContent.Trim().Should().Be("Profil");
    }
}
