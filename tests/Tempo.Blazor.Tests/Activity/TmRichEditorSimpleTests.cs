using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

public class TmRichEditorSimpleTests : LocalizationTestBase
{
    [Fact]
    public void RichEditorSimple_RendersContainer()
    {
        var cut = Render<TmRichEditorSimple>();

        cut.FindAll(".tm-rich-editor-simple").Should().NotBeEmpty();
    }

    [Fact]
    public void RichEditorSimple_RendersContentEditable()
    {
        var cut = Render<TmRichEditorSimple>();

        // Contenteditable element should exist
        var editor = cut.Find(".tm-rte-editor-content");
        editor.Should().NotBeNull();
        // When enabled, contenteditable attribute is present (may be empty or "true")
        editor.HasAttribute("contenteditable").Should().BeTrue();
    }

    [Fact]
    public void RichEditorSimple_RendersToolbar()
    {
        var cut = Render<TmRichEditorSimple>();

        cut.FindAll(".tm-rte-toolbar").Should().NotBeEmpty();
    }

    [Fact]
    public void RichEditorSimple_Placeholder_Displayed()
    {
        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.Placeholder, "Write something..."));

        var editor = cut.Find(".tm-rte-editor-content");
        editor.GetAttribute("data-placeholder").Should().Be("Write something...");
    }

    [Fact]
    public void RichEditorSimple_Disabled_State()
    {
        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.IsDisabled, true));

        var editor = cut.Find(".tm-rte-editor-content");
        // When disabled, contenteditable="false" or attribute is removed
        var value = editor.GetAttribute("contenteditable");
        (value == null || value == "false").Should().BeTrue();
    }

    [Fact]
    public void RichEditorSimple_MaxLength_ShowsCounter()
    {
        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.MaxLength, 200));

        cut.FindAll(".tm-rte-char-count").Should().NotBeEmpty();
    }

    [Fact]
    public async Task MentionPicker_Open_ArrowDown_WiresComboboxToEditorOwnedList()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        provider.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<TmUser>
            {
                new() { Id = "u1", UserName = "alice", DisplayName = "Alice" },
                new() { Id = "u2", UserName = "alex", DisplayName = "Alex" }
            });

        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.SupportsMentions, true)
            .Add(c => c.MentionProvider, provider));

        var surface = cut.Find(".tm-rte-editor-content");
        await surface.KeyDownAsync(new KeyboardEventArgs { Key = "@" });

        var listId = surface.GetAttribute("aria-controls");
        listId.Should().Be(surface.Id + "-mentions");
        surface.GetAttribute("role").Should().Be("combobox");
        surface.GetAttribute("aria-expanded").Should().Be("true");
        surface.GetAttribute("aria-autocomplete").Should().Be("list");
        surface.GetAttribute("aria-activedescendant").Should().Be(listId + "-option-0");

        var list = cut.Find("#" + listId);
        list.GetAttribute("role").Should().Be("listbox");
        list.GetAttribute("aria-label").Should().Be("People");
        var options = list.QuerySelectorAll("[role='option']");
        options.Should().HaveCount(2);
        options[0].GetAttribute("aria-selected").Should().Be("true");
        options[1].GetAttribute("aria-selected").Should().Be("false");

        await surface.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        surface.GetAttribute("aria-activedescendant").Should().Be(listId + "-option-1");
        options = cut.Find("#" + listId).QuerySelectorAll("[role='option']");
        options[0].GetAttribute("aria-selected").Should().Be("false");
        options[1].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public async Task MentionPicker_EmptyQuery_StaysOpen()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        provider.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<TmUser> { new() { Id = "u1", UserName = "alice", DisplayName = "Alice" } });

        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.SupportsMentions, true)
            .Add(c => c.MentionProvider, provider));

        await cut.Find(".tm-rte-editor-content").KeyDownAsync(new KeyboardEventArgs { Key = "@" });

        cut.FindAll(".tm-rte-mention-dropdown").Should().ContainSingle();
        cut.Find(".tm-rte-mention-dropdown").GetAttribute("role").Should().Be("listbox");
    }

    [Fact]
    public async Task MentionPicker_NoResults_IsDisabledOption()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        provider.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<TmUser>());

        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.SupportsMentions, true)
            .Add(c => c.MentionProvider, provider));

        await cut.InvokeAsync(() => cut.Instance.TriggerMentionSearchForTest("zzz"));

        var empty = cut.Find(".tm-rte-mention-empty");
        empty.GetAttribute("role").Should().Be("option");
        empty.GetAttribute("aria-disabled").Should().Be("true");
        empty.Id.Should().NotBeNullOrEmpty();
        cut.Find(".tm-rte-editor-content").GetAttribute("aria-activedescendant").Should().Be(empty.Id);
    }
}
