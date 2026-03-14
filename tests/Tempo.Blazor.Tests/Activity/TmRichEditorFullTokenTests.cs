using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

public class TmRichEditorFullTokenTests : LocalizationTestBase
{
    private class TestTokenProvider : ITokenDataProvider
    {
        private readonly List<TestToken> _tokens = new()
        {
            new TestToken { Key = "user.name", DisplayName = "User Name", Description = "Full name", Category = "User" },
            new TestToken { Key = "user.email", DisplayName = "User Email", Description = "Email address", Category = "User" },
            new TestToken { Key = "system.date", DisplayName = "Current Date", Description = "Today's date", Category = "System" },
        };

        public bool SupportsCreation { get; set; } = true;

        public void Refresh() { }

        public Task<IEnumerable<IToken>> SearchTokensAsync(string query, CancellationToken ct = default)
        {
            IEnumerable<IToken> result = string.IsNullOrEmpty(query)
                ? _tokens
                : _tokens.Where(t =>
                    t.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(result);
        }
    }

    private class TestToken : IToken
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
    }

    [Fact]
    public void RichEditorFull_SupportsTokens_Default_False()
    {
        var cut = RenderComponent<TmRichEditorFull>();

        cut.FindAll(".tm-rte-btn-token").Should().BeEmpty();
    }

    [Fact]
    public void RichEditorFull_SupportsTokens_ShowsButton()
    {
        var cut = RenderComponent<TmRichEditorFull>(p => p
            .Add(c => c.SupportsTokens, true)
            .Add(c => c.TokenProvider, new TestTokenProvider()));

        cut.FindAll(".tm-rte-btn-token").Should().NotBeEmpty();
    }

    [Fact]
    public void RichEditorFull_TokenDropdown_NotVisibleByDefault()
    {
        var cut = RenderComponent<TmRichEditorFull>(p => p
            .Add(c => c.SupportsTokens, true)
            .Add(c => c.TokenProvider, new TestTokenProvider()));

        cut.FindAll(".tm-rte-token-dropdown").Should().BeEmpty();
    }

    [Fact]
    public async Task RichEditorFull_TriggerTokenSearch_ShowsDropdown()
    {
        var cut = RenderComponent<TmRichEditorFull>(p => p
            .Add(c => c.SupportsTokens, true)
            .Add(c => c.TokenProvider, new TestTokenProvider()));

        await cut.InvokeAsync(() => cut.Instance.TriggerTokenSearchForTest(""));

        cut.FindAll(".tm-rte-token-dropdown").Should().NotBeEmpty();
        cut.FindAll(".tm-rte-token-item").Should().HaveCount(3);
    }

    [Fact]
    public async Task RichEditorFull_TriggerTokenSearch_FiltersResults()
    {
        var cut = RenderComponent<TmRichEditorFull>(p => p
            .Add(c => c.SupportsTokens, true)
            .Add(c => c.TokenProvider, new TestTokenProvider()));

        await cut.InvokeAsync(() => cut.Instance.TriggerTokenSearchForTest("email"));

        cut.FindAll(".tm-rte-token-item").Should().HaveCount(1);
    }

    [Fact]
    public async Task RichEditorFull_TokenSearch_ShowsCreateOption()
    {
        var cut = RenderComponent<TmRichEditorFull>(p => p
            .Add(c => c.SupportsTokens, true)
            .Add(c => c.TokenProvider, new TestTokenProvider()));

        await cut.InvokeAsync(() => cut.Instance.TriggerTokenSearchForTest(""));

        cut.FindAll(".tm-rte-token-create").Should().NotBeEmpty();
    }

    [Fact]
    public async Task RichEditorFull_TokenSearch_NoProvider_NoDropdown()
    {
        var cut = RenderComponent<TmRichEditorFull>(p => p
            .Add(c => c.SupportsTokens, true));

        await cut.InvokeAsync(() => cut.Instance.TriggerTokenSearchForTest("test"));

        cut.FindAll(".tm-rte-token-dropdown").Should().BeEmpty();
    }

    [Fact]
    public void RichEditorFull_NoTokenSupport_NoTokenButton()
    {
        var cut = RenderComponent<TmRichEditorFull>(p => p
            .Add(c => c.SupportsTokens, false));

        cut.FindAll(".tm-rte-btn-token").Should().BeEmpty();
    }
}
