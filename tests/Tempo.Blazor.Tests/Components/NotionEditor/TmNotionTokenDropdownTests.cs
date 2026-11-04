using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// TKN-01..05: TmNotionTokenDropdown — renders list, keyboard nav, Enter selects, Escape closes,
/// backdrop click closes.
/// TDD: tests written alongside implementation.
/// </summary>
public class TmNotionTokenDropdownTests : LocalizationTestBase
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static IToken MakeToken(string key, string displayName, string? colorClass = null) =>
        new TestToken { Key = key, DisplayName = displayName, ColorClass = colorClass };

    private static ITokenDataProvider ProviderWith(params IToken[] tokens)
    {
        var p = Substitute.For<ITokenDataProvider>();
        p.SearchTokensAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult<IEnumerable<IToken>>(tokens));
        return p;
    }

    private static NotionEditorContext BuildContext(ITokenDataProvider? tokenProvider = null)
        => new()
        {
            DataProvider  = Substitute.For<INotionDataProvider>(),
            BlockService = Substitute.For<INotionEditorBlockService>(),
            TokenProvider = tokenProvider,
        };

    // ── TKN-01: Visible=false renders nothing ─────────────────────────────────

    [Fact]
    public void TokenDropdown_WhenNotVisible_RendersNothing()
    {
        var ctx = BuildContext(ProviderWith());
        var cut = Render<TmNotionTokenDropdown>(p => p
            .Add(x => x.Visible, false)
            .Add(x => x.Top, 100)
            .Add(x => x.Left, 200)
            .AddCascadingValue(ctx));

        cut.FindAll(".tm-notion-token-dropdown").Should().BeEmpty();
    }

    // ── TKN-02: Visible=true renders item list ────────────────────────────────

    [Fact]
    public async Task TokenDropdown_WhenVisible_RendersItems()
    {
        var tokens = new[]
        {
            MakeToken("user.email", "User Email"),
            MakeToken("company.name", "Company Name"),
        };
        var ctx = BuildContext(ProviderWith(tokens));

        var cut = Render<TmNotionTokenDropdown>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 100)
            .Add(x => x.Left, 200)
            .AddCascadingValue(ctx));

        await cut.InvokeAsync(() => Task.CompletedTask);

        var items = cut.FindAll(".tm-notion-token-dropdown__item");
        items.Count.Should().Be(2);
        items[0].TextContent.Should().Contain("User Email");
        items[1].TextContent.Should().Contain("Company Name");
    }

    // ── TKN-03: Enter fires OnItemSelected with correct args ──────────────────

    [Fact]
    public async Task TokenDropdown_Enter_FiresOnItemSelectedWithCorrectArgs()
    {
        const string ExpectedKey         = "user.email";
        const string ExpectedDisplayName = "User Email";
        const string ExpectedColorClass  = "token-primary";

        var tokens = new[] { MakeToken(ExpectedKey, ExpectedDisplayName, ExpectedColorClass) };
        var ctx = BuildContext(ProviderWith(tokens));

        (string Key, string DisplayName, string? ColorClass) selected = default;

        var cut = Render<TmNotionTokenDropdown>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 100)
            .Add(x => x.Left, 200)
            .Add(x => x.OnItemSelected,
                EventCallback.Factory.Create<(string, string, string?)>(
                    this, args => selected = args))
            .AddCascadingValue(ctx));

        await cut.InvokeAsync(() => Task.CompletedTask);

        var input = cut.Find(".tm-notion-token-dropdown__input");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        selected.Key.Should().Be(ExpectedKey);
        selected.DisplayName.Should().Be(ExpectedDisplayName);
        selected.ColorClass.Should().Be(ExpectedColorClass);
    }

    // ── TKN-04: Escape fires OnClosed ────────────────────────────────────────

    [Fact]
    public async Task TokenDropdown_Escape_FiresOnClosed()
    {
        var ctx = BuildContext(ProviderWith());
        var closedFired = false;

        var cut = Render<TmNotionTokenDropdown>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 100)
            .Add(x => x.Left, 200)
            .Add(x => x.OnClosed,
                EventCallback.Factory.Create(this, () => closedFired = true))
            .AddCascadingValue(ctx));

        await cut.InvokeAsync(() => Task.CompletedTask);

        var input = cut.Find(".tm-notion-token-dropdown__input");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        closedFired.Should().BeTrue();
    }

    // ── TKN-05: Backdrop click fires OnClosed ─────────────────────────────────

    [Fact]
    public async Task TokenDropdown_BackdropClick_FiresOnClosed()
    {
        var ctx = BuildContext(ProviderWith());
        var closedFired = false;

        var cut = Render<TmNotionTokenDropdown>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.Top, 100)
            .Add(x => x.Left, 200)
            .Add(x => x.OnClosed,
                EventCallback.Factory.Create(this, () => closedFired = true))
            .AddCascadingValue(ctx));

        await cut.InvokeAsync(() => Task.CompletedTask);

        // The backdrop dismissal is the core overlay's OnDismissed → the shared floating menu's
        // OnClose → the dropdown's OnClosed.
        var menu = cut.FindComponent<TmNotionFloatingMenu>();
        await cut.InvokeAsync(() => menu.Instance.GetType()
            .GetMethod("OnDismissed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(menu.Instance, ["backdrop"]));

        closedFired.Should().BeTrue();
    }

    // ── Stub ─────────────────────────────────────────────────────────────────

    private sealed class TestToken : IToken
    {
        public string  Key         { get; init; } = string.Empty;
        public string  DisplayName { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? Category    { get; init; }
        public string? Icon        { get; init; }
        public string? ColorClass  { get; init; }
        public string? TypeLabel   { get; init; }
    }
}
