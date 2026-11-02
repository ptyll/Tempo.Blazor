using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>TDD tests for TmTopBar.</summary>
public class TmTopBarTests : LocalizationTestBase
{
    private sealed record FakeUser(string DisplayName) : IUserInfo
    {
        public string Id => "u-1";
        public string? AvatarSrc => null;
        public string? Email => "jane@example.com";
    }

    private static IUserInfo User => new FakeUser("Jane Doe");

    // ── Base markup (regression) ────────────────────────────────────────────────

    [Fact]
    public void Renders_A_Header_With_The_Brand_Title()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.BrandTitle, "Tempo"));

        var header = cut.Find("header");
        header.ClassList.Should().Contain("tm-topbar");
        cut.Find(".tm-topbar-brand-title").TextContent.Trim().Should().Be("Tempo");
    }

    [Fact]
    public void Renders_The_Action_Slot()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.ActionsContent, builder => builder.AddMarkupContent(0, "<span class=\"probe-action\">bell</span>")));

        cut.FindAll(".probe-action").Should().HaveCount(1);
    }

    // ── Command palette trigger ────────────────────────────────────────────────

    [Fact]
    public void Command_Palette_Trigger_Is_Hidden_Without_A_Handler()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.BrandTitle, "Tempo"));

        cut.FindAll(".tm-topbar-search-trigger").Should().BeEmpty();
    }

    [Fact]
    public void Command_Palette_Trigger_Carries_The_Localized_Label_And_Its_Key_Shortcut()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.OnCommandPalette, EventCallback.Factory.Create(this, () => { })));

        var trigger = cut.Find(".tm-topbar-search-trigger");
        trigger.GetAttribute("aria-label").Should().Be("Open command palette");
        trigger.GetAttribute("aria-keyshortcuts").Should().Contain("Control+K",
            "the shortcut is advertised to assistive technology, not only printed");
        trigger.GetAttribute("aria-keyshortcuts").Should().Contain("Meta+K");
    }

    [Fact]
    public void Command_Palette_Shortcut_Text_Defaults_To_The_Parameter_Platform_Label()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.OnCommandPalette, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-search-hint").TextContent.Trim().Should().Be("Ctrl+K");
    }

    [Fact]
    public void Command_Palette_Shortcut_Text_Comes_From_The_Parameter()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.CommandPaletteShortcut, "⌘K")
            .Add(c => c.OnCommandPalette, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-search-hint").TextContent.Trim().Should().Be("⌘K");
    }

    [Fact]
    public void Command_Palette_Shortcut_Text_Can_Be_Hidden()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.CommandPaletteShortcut, string.Empty)
            .Add(c => c.OnCommandPalette, EventCallback.Factory.Create(this, () => { })));

        cut.FindAll(".tm-topbar-search-hint").Should().BeEmpty();
        cut.Find(".tm-topbar-search-trigger").GetAttribute("aria-label").Should().Be("Open command palette");
    }

    [Fact]
    public void Command_Palette_Advertises_The_Host_Key_Shortcuts()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.CommandPaletteShortcut, "Ctrl+P")
            .Add(c => c.CommandPaletteKeyShortcuts, "Control+P")
            .Add(c => c.OnCommandPalette, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-search-trigger").GetAttribute("aria-keyshortcuts").Should().Be("Control+P",
            "a host that rebinds the chord must be able to make the announcement match its hint");
        cut.Find(".tm-topbar-search-hint").TextContent.Trim().Should().Be("Ctrl+P");
    }

    [Fact]
    public void Command_Palette_Omits_Aria_KeyShortcuts_When_The_Host_Passes_None()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.CommandPaletteKeyShortcuts, null)
            .Add(c => c.OnCommandPalette, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-search-trigger").GetAttribute("aria-keyshortcuts").Should().BeNull(
            "announcing a chord nobody claims is worse than announcing none");
    }

    [Fact]
    public void Command_Palette_Trigger_Fires_OnCommandPalette()
    {
        var fired = false;
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.OnCommandPalette, EventCallback.Factory.Create(this, () => fired = true)));

        cut.Find(".tm-topbar-search-trigger").Click();

        fired.Should().BeTrue();
    }

    // ── Menu button (host drawer toggle) ───────────────────────────────────────

    [Fact]
    public void Menu_Button_Is_Absent_By_Default()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.BrandTitle, "Tempo"));

        cut.FindAll(".tm-topbar-menu-btn").Should().BeEmpty("the menu button is opt-in");
    }

    [Fact]
    public void ShowMenuButton_Renders_A_Localized_Disclosure_Button()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.ShowMenuButton, true));

        var button = cut.Find(".tm-topbar-menu-btn");
        button.GetAttribute("type").Should().Be("button");
        button.GetAttribute("aria-label").Should().Be("Navigation menu");
        button.GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Menu_Button_Reports_Its_Expanded_State_And_Controls()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.ShowMenuButton, true)
            .Add(c => c.MenuExpanded, true)
            .Add(c => c.MenuControlsId, "app-drawer"));

        var button = cut.Find(".tm-topbar-menu-btn");
        button.GetAttribute("aria-expanded").Should().Be("true");
        button.GetAttribute("aria-controls").Should().Be("app-drawer");
    }

    [Fact]
    public void Menu_Button_Omits_AriaControls_Without_A_Target()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.ShowMenuButton, true));

        cut.Find(".tm-topbar-menu-btn").GetAttribute("aria-controls").Should().BeNull();
    }

    [Fact]
    public void Menu_Button_Fires_OnMenuToggle()
    {
        var fired = 0;
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.ShowMenuButton, true)
            .Add(c => c.OnMenuToggle, EventCallback.Factory.Create(this, () => fired++)));

        cut.Find(".tm-topbar-menu-btn").Click();

        fired.Should().Be(1);
    }

    [Fact]
    public void Menu_Button_Is_Translated()
    {
        UseCzechLocalization();

        var cut = Render<TmTopBar>(p => p.Add(c => c.ShowMenuButton, true));

        cut.Find(".tm-topbar-menu-btn").GetAttribute("aria-label").Should().Be("Nabídka navigace");
    }

    [Fact]
    public void MenuButtonId_Is_Opt_In_And_Gives_The_Opener_A_Stable_Id()
    {
        // A frame whose drawer hands the focus back to this button needs to name it by id — the opener
        // is rendered here, not by the frame.
        var plain = Render<TmTopBar>(p => p.Add(c => c.ShowMenuButton, true));
        plain.Find(".tm-topbar-menu-btn").GetAttribute("id").Should().BeNull("no id without the parameter, so every existing host keeps its markup");

        var named = Render<TmTopBar>(p => p
            .Add(c => c.ShowMenuButton, true)
            .Add(c => c.MenuButtonId, "app-menu-button"));
        named.Find(".tm-topbar-menu-btn").GetAttribute("id").Should().Be("app-menu-button");
    }

    // ── User menu ──────────────────────────────────────────────────────────────

    [Fact]
    public void User_Renders_An_Avatar_And_The_Display_Name()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.User, User));

        cut.FindAll(".tm-topbar-user-button").Should().HaveCount(1);
        cut.Find(".tm-topbar-user-name").TextContent.Trim().Should().Be("Jane Doe");
    }

    [Fact]
    public void User_Button_Advertises_The_Menu_And_Its_State()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.User, User));

        var button = cut.Find(".tm-topbar-user-button");
        button.GetAttribute("aria-haspopup").Should().Be("menu");
        button.GetAttribute("aria-expanded").Should().Be("false");
        button.GetAttribute("aria-label").Should().Be("User menu");

        button.Click();

        cut.Find(".tm-topbar-user-button").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void User_Menu_Is_A_Menu_With_MenuItems()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));
        cut.Find(".tm-topbar-user-button").Click();

        var menu = cut.Find(".tm-topbar-user-menu");
        menu.GetAttribute("role").Should().Be("menu");
        menu.GetAttribute("aria-label").Should().Be("User menu");

        var signOut = cut.Find(".tm-topbar-user-menu-logout");
        signOut.GetAttribute("role").Should().Be("menuitem");
        signOut.TextContent.Trim().Should().Be("Sign out");
    }

    [Fact]
    public void User_Menu_Item_Labels_Are_Not_Hardcoded_By_The_Component()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("profile", "Your profile", "user", "/profile"),
                new("settings", "Account settings", "settings", "/settings")
            }));
        cut.Find(".tm-topbar-user-button").Click();

        var labels = cut.FindAll(".tm-topbar-user-menu-item").Select(i => i.TextContent.Trim());
        labels.Should().Equal(new[] { "Your profile", "Account settings" });
    }

    [Fact]
    public void User_Menu_Renders_A_Href_Item_As_A_Link_And_A_Command_As_A_Button()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("profile", "Your profile", "user", "/profile"),
                new("theme", "Toggle theme", "moon")
            }));
        cut.Find(".tm-topbar-user-button").Click();

        var items = cut.FindAll(".tm-topbar-user-menu-item");
        items[0].TagName.Should().Be("A");
        items[0].GetAttribute("href").Should().Be("/profile");
        items[0].GetAttribute("role").Should().Be("menuitem");
        items[1].TagName.Should().Be("BUTTON");
        items[1].GetAttribute("role").Should().Be("menuitem");
    }

    [Fact]
    public void User_Menu_Command_Raises_Its_Callback_And_Closes_The_Menu()
    {
        var clicks = 0;
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("theme", "Toggle theme", onClick: () => { clicks++; return Task.CompletedTask; })
            }));
        cut.Find(".tm-topbar-user-button").Click();

        cut.Find(".tm-topbar-user-menu-item").Click();

        clicks.Should().Be(1);
        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty("choosing an entry dismisses the menu");
    }

    [Fact]
    public void User_Menu_Link_Closes_The_Menu()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("profile", "Your profile", "user", "/profile")
            }));
        cut.Find(".tm-topbar-user-button").Click();

        cut.Find(".tm-topbar-user-menu-item").Click();

        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty();
    }

    [Fact]
    public void User_Menu_Renders_A_Separator_And_A_Danger_Entry()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("profile", "Your profile", "user", "/profile"),
                UserMenuItem.CreateSeparator("sep-1"),
                new("delete", "Delete account", "trash", onClick: () => Task.CompletedTask, isDanger: true)
            }));
        cut.Find(".tm-topbar-user-button").Click();

        cut.FindAll(".tm-topbar-user-menu-separator").Should().HaveCount(1);
        cut.FindAll(".tm-topbar-user-menu-separator")[0].GetAttribute("role").Should().Be("separator");
        cut.FindAll(".tm-topbar-user-menu-separator")[0].GetAttribute("aria-hidden").Should().BeNull(
            "role=separator already removes it from the accessibility tree; aria-hidden contradicts the role");

        var danger = cut.FindAll(".tm-topbar-user-menu-item").Last();
        danger.ClassList.Should().Contain("tm-topbar-user-menu-item-danger",
            "the destructive style is a visual cue only — the label stays self-explanatory");
    }

    [Fact]
    public void SignOut_Is_Localized_And_Raises_OnLogout()
    {
        var loggedOut = false;
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => loggedOut = true)));
        cut.Find(".tm-topbar-user-button").Click();

        cut.Find(".tm-topbar-user-menu-logout").Click();

        loggedOut.Should().BeTrue();
        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty();
    }

    [Fact]
    public void SignOut_Is_Translated()
    {
        UseCzechLocalization();

        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));
        cut.Find(".tm-topbar-user-button").Click();

        cut.Find(".tm-topbar-user-menu-logout").TextContent.Trim().Should().Be("Odhlásit se");
    }

    [Fact]
    public void SignOut_Is_Separated_From_The_Host_Items()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { }))
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("profile", "Your profile", "user", "/profile")
            }));
        cut.Find(".tm-topbar-user-button").Click();

        cut.FindAll(".tm-topbar-user-menu-separator").Should().HaveCount(1,
            "a sign-out glued to a navigation entry reads as the same group");
    }

    [Fact]
    public void No_Logout_Handler_And_No_Items_Render_No_SignOut()
    {
        var cut = Render<TmTopBar>(p => p.Add(c => c.User, User));
        cut.Find(".tm-topbar-user-button").Click();

        cut.FindAll(".tm-topbar-user-menu-logout").Should().BeEmpty();
    }

    [Fact]
    public void User_Menu_Item_Id_Name_The_Item_For_Tests()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("profile", "Your profile", "user", "/profile")
            }));
        cut.Find(".tm-topbar-user-button").Click();

        cut.Find(".tm-topbar-user-menu-item").GetAttribute("data-testid").Should().Be("topbar-user-menu-item-profile");
    }

    // ── Keyboard: Escape, Tab out and the APG menu button pattern ───────────────

    [Fact]
    public void A_Click_Outside_Closes_The_User_Menu_And_Returns_Focus_To_The_Trigger()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));
        var triggerId = ReferenceId(cut.Find(".tm-topbar-user-button"));

        cut.Find(".tm-topbar-user-button").Click();
        cut.Find(".tm-topbar-user-backdrop").Click();

        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty();
        cut.Find(".tm-topbar-user-button").GetAttribute("aria-expanded").Should().Be("false");
        ShouldHaveFocusedLast(triggerId,
            "a backdrop click destroys the focused entry, so the trigger has to take the focus back");
    }

    [Fact]
    public void Escape_Closes_The_User_Menu_And_Returns_Focus_To_The_Trigger()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));
        var triggerId = ReferenceId(cut.Find(".tm-topbar-user-button"));
        cut.Find(".tm-topbar-user-button").Click();
        cut.FindAll(".tm-topbar-user-menu").Should().HaveCount(1);

        // Escape is container-level by design and bubbles from whichever entry holds the focus.
        cut.FindAll(".tm-topbar-user-menu-item")[0].KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty();
        cut.Find(".tm-topbar-user-button").GetAttribute("aria-expanded").Should().Be("false");
        ShouldHaveFocusedLast(triggerId,
            "Escape must hand focus back to the button that opened the menu");
    }

    [Fact]
    public void Tab_From_Any_Entry_Closes_The_Menu_And_Focuses_The_Trigger()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));
        var triggerId = ReferenceId(cut.Find(".tm-topbar-user-button"));
        cut.Find(".tm-topbar-user-button").Click();

        // The entries are not tab stops (tabindex=-1), so Tab leaves the menu from ANY of them.
        cut.FindAll(".tm-topbar-user-menu-item")[0].KeyDown(new KeyboardEventArgs { Key = "Tab" });

        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty(
            "a menu left open after focus moved on is a visual lie");
        ShouldHaveFocusedLast(triggerId,
            "closing removes the focused entry, so the trigger must own the focus before the browser moves on");
    }

    [Fact]
    public void ShiftTab_From_Any_Entry_Closes_The_Menu_And_Focuses_The_Trigger()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));
        var triggerId = ReferenceId(cut.Find(".tm-topbar-user-button"));
        cut.Find(".tm-topbar-user-button").Click();

        cut.FindAll(".tm-topbar-user-menu-item")[0].KeyDown(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty();
        ShouldHaveFocusedLast(triggerId);
    }

    [Fact]
    public void Opening_The_User_Menu_Moves_Focus_Into_The_First_Entry()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-user-button").Click();

        ShouldHaveFocusedLast(ReferenceId(cut.FindAll(".tm-topbar-user-menu-item")[0]),
            "a role=menu that never receives the focus is announced and then unusable");
    }

    [Fact]
    public void Every_Entry_Is_A_Negative_Tab_Stop()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));
        cut.Find(".tm-topbar-user-button").Click();

        cut.FindAll(".tm-topbar-user-menu-item")
            .Should().OnlyContain(item => item.GetAttribute("tabindex") == "-1",
                "focus inside an APG menu is owned by the arrow keys, not by the page's tab order");
    }

    [Fact]
    public void ArrowDown_On_The_Trigger_Opens_The_Menu_And_Focuses_The_First_Entry()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-user-button").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.FindAll(".tm-topbar-user-menu").Should().HaveCount(1);
        ShouldHaveFocusedLast(ReferenceId(cut.FindAll(".tm-topbar-user-menu-item")[0]));
    }

    [Fact]
    public void ArrowUp_On_The_Trigger_Opens_The_Menu_And_Focuses_The_Last_Entry()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-user-button").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        cut.FindAll(".tm-topbar-user-menu").Should().HaveCount(1);
        ShouldHaveFocusedLast(ReferenceId(cut.FindAll(".tm-topbar-user-menu-item").Last()));
    }

    [Fact]
    public void ArrowDown_On_An_Entry_Moves_To_The_Next_And_Wraps_At_The_End()
    {
        var cut = Render<TmTopBar>(EntryMenuParameters());
        cut.Find(".tm-topbar-user-button").Click();
        var ids = ItemReferenceIds(cut);
        ids.Should().HaveCount(4);

        cut.FindAll(".tm-topbar-user-menu-item")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        ShouldHaveFocusedLast(ids[1]);

        cut.FindAll(".tm-topbar-user-menu-item").Last().KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        ShouldHaveFocusedLast(ids[0], "the arrows wrap around the menu");
    }

    [Fact]
    public void ArrowUp_On_An_Entry_Moves_To_The_Previous_And_Wraps_At_The_Start()
    {
        var cut = Render<TmTopBar>(EntryMenuParameters());
        cut.Find(".tm-topbar-user-button").Click();
        var ids = ItemReferenceIds(cut);

        cut.FindAll(".tm-topbar-user-menu-item")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        ShouldHaveFocusedLast(ids[0]);

        cut.FindAll(".tm-topbar-user-menu-item")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        ShouldHaveFocusedLast(ids[^1], "the arrows wrap around the menu");
    }

    [Fact]
    public void Home_And_End_Jump_To_The_First_And_Last_Entry()
    {
        var cut = Render<TmTopBar>(EntryMenuParameters());
        cut.Find(".tm-topbar-user-button").Click();
        var ids = ItemReferenceIds(cut);

        cut.FindAll(".tm-topbar-user-menu-item")[2].KeyDown(new KeyboardEventArgs { Key = "Home" });
        ShouldHaveFocusedLast(ids[0]);

        cut.FindAll(".tm-topbar-user-menu-item")[0].KeyDown(new KeyboardEventArgs { Key = "End" });
        ShouldHaveFocusedLast(ids[^1]);
    }

    [Fact]
    public void Reopening_The_Menu_Focuses_The_First_Entry_Again()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-user-button").Click();
        cut.FindAll(".tm-topbar-user-menu-item")[0].KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty();

        cut.Find(".tm-topbar-user-button").Click();

        ShouldHaveFocusedLast(ReferenceId(cut.FindAll(".tm-topbar-user-menu-item")[0]),
            "the focus-on-open contract has to survive a close/reopen cycle, not only the first open");
    }

    [Fact]
    public void Enter_On_The_Trigger_Is_Not_Emulated()
    {
        var cut = Render<TmTopBar>(p => p
            .Add(c => c.User, User)
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { })));

        cut.Find(".tm-topbar-user-button").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.FindAll(".tm-topbar-user-menu").Should().BeEmpty(
            "Enter on a native button is the browser's own activation, never an emulated one");
        FocusCalls().Should().BeEmpty();
    }

    /// <summary>Two host entries, a separator and the built-in sign out, so the menu has four stops.</summary>
    private Action<ComponentParameterCollectionBuilder<TmTopBar>> EntryMenuParameters()
        => parameters => parameters
            .Add(c => c.User, User)
            .Add(c => c.UserMenuItems, new List<UserMenuItem>
            {
                new("profile", "Your profile", "user", "/profile"),
                new("settings", "Account settings", "settings", "/settings"),
                UserMenuItem.CreateSeparator("sep-1"),
                new("theme", "Toggle theme", "moon", onClick: () => Task.CompletedTask)
            })
            .Add(c => c.OnLogout, EventCallback.Factory.Create(this, () => { }));

    /// <summary>
    /// The element reference id the renderer stamped on <paramref name="element"/>. It is only present
    /// on the render that inserted the element — a later re-render clears the serialized attribute
    /// (the reference itself is unchanged), so the fixture reads the ids before the interaction.
    /// </summary>
    private static string? ReferenceId(IElement element) => element.GetAttribute("blazor:elementreference");

    private static List<string?> ItemReferenceIds(IRenderedComponent<TmTopBar> cut)
        => cut.FindAll(".tm-topbar-user-menu-item").Select(ReferenceId).ToList();

    private List<JSRuntimeInvocation> FocusCalls()
        => JSInterop.Invocations.Where(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus").ToList();

    /// <summary>
    /// Asserts the LAST focus move went to the element that carried <paramref name="expectedReferenceId"/>.
    /// One fact produces several focus calls (opening + every arrow), so the single-invocation helper
    /// cannot be used here.
    /// </summary>
    private void ShouldHaveFocusedLast(string? expectedReferenceId, string? because = null)
    {
        expectedReferenceId.Should().NotBeNullOrEmpty("the fixture reads the target's reference id before the interaction");
        var focusCalls = FocusCalls();
        focusCalls.Should().NotBeEmpty(because ?? "the interaction must move the focus");
        var target = focusCalls[^1].Arguments[0].Should().BeOfType<ElementReference>().Subject;
        target.Id.Should().Be(expectedReferenceId);
    }
}
