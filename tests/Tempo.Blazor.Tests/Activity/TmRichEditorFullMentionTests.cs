using Bunit;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

public class TmRichEditorFullMentionTests : LocalizationTestBase
{
    [Fact]
    public async Task MentionPicker_StillRendersList()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        provider.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<TmUser> { new() { Id = "u1", UserName = "alice", DisplayName = "Alice" } });

        var cut = Render<TmRichEditorFull>(p => p
            .Add(c => c.SupportsMentions, true)
            .Add(c => c.MentionProvider, provider));

        await cut.InvokeAsync(() => cut.Instance.TriggerMentionSearchForTest(""));

        var list = cut.Find(".tm-rte-mention-dropdown");
        list.GetAttribute("role").Should().Be("listbox");
        list.QuerySelectorAll("[role='option']").Should().ContainSingle();
        list.QuerySelector("[role='option']")!.GetAttribute("aria-selected").Should().Be("true");
    }
}
