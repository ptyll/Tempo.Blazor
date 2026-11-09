using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Models;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Localization;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase-1 comment extraction: the shared thread, composer and formatter. These pin the contract
/// the three panels now share, including the selectors the draft-guard and E2E already use.
/// </summary>
public class TmNotionCommentSharedTests : LocalizationTestBase
{
    [Fact]
    public void BlockComment_UsesTheSharedFormatter()
    {
        UseCzechLocalization();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<ITmRelativeTimeFormatter>(
            sp => new TmRelativeTimeFormatter(sp.GetRequiredService<ITmLocalizer>(), clock));

        var created = clock.GetUtcNow().AddMinutes(-5);
        var cut = Render<TmNotionCommentThreadView>(p => p
            .Add(x => x.ClassPrefix, "tm-nbcp")
            .Add(x => x.Nodes, Tree(created))
            .Add(x => x.FormatTime, (Func<DateTimeOffset, string>)(dt =>
                Services.GetRequiredService<ITmRelativeTimeFormatter>().Format(dt)))
            .Add(x => x.AbsoluteTime, (Func<DateTimeOffset, string>)(dt =>
                Services.GetRequiredService<ITmRelativeTimeFormatter>().Absolute(dt))));

        var time = cut.Find("time");
        time.TextContent.Should().Contain("před 5 min");
        time.GetAttribute("datetime").Should().StartWith("2026-06-15T11:55:00");
        time.GetAttribute("title").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ThreadView_RendersReplyTree_AndHidesEditDeleteWithoutPermission()
    {
        var cut = Render<TmNotionCommentThreadView>(p => p
            .Add(x => x.ClassPrefix, "tm-nbcp")
            .Add(x => x.Nodes, Tree(DateTimeOffset.UtcNow, canEdit: false, canDelete: false))
            .Add(x => x.LabelPrefix, "TmNotionBlockComment"));

        cut.FindAll("[data-testid=notion-comment-entry]").Should().ContainSingle();
        cut.FindAll("[data-testid=notion-comment-reply]").Should().ContainSingle();
        cut.FindAll("[data-testid=notion-comment-edit]").Should().BeEmpty();
        cut.FindAll("[data-testid=notion-comment-delete]").Should().BeEmpty();
        cut.FindAll("[data-testid=notion-comment-entry-menu]").Should().BeEmpty();
    }

    [Fact]
    public void ThreadView_MenuOpensEditAndDelete_OnlyWhenAllowed()
    {
        var cut = Render<TmNotionCommentThreadView>(p => p
            .Add(x => x.ClassPrefix, "tm-nbcp")
            .Add(x => x.Nodes, Tree(DateTimeOffset.UtcNow, canEdit: true, canDelete: true))
            .Add(x => x.CanEdit, (Func<TmCommentEntry, bool>)(e => e.CanEdit))
            .Add(x => x.CanDelete, (Func<TmCommentEntry, bool>)(e => e.CanDelete))
            .Add(x => x.LabelPrefix, "TmNotionBlockComment"));

        cut.Find("[data-testid=notion-comment-entry-menu] .tm-dropdown-trigger")
            .GetAttribute("aria-label").Should().NotBeNullOrEmpty();

        cut.Find("[data-testid=notion-comment-entry-menu] .tm-dropdown-trigger").Click();
        cut.FindAll("[data-testid=notion-comment-edit-menu]").Should().ContainSingle();
        cut.FindAll("[data-testid=notion-comment-delete-menu]").Should().ContainSingle();
    }

    [Fact]
    public void Composer_CtrlEnterSubmits_EmptyIsDisabled_ShowsInitials()
    {
        string? submitted = null;
        var cut = Render<TmNotionCommentComposer>(p => p
            .Add(x => x.ClassPrefix, "tm-nbcp")
            .Add(x => x.ShowAvatar, true)
            .Add(x => x.AuthorName, "Ada Lovelace")
            .Add(x => x.SubmitText, "Send")
            .Add(x => x.OnSubmit, (string text) => submitted = text));

        cut.Find("[data-testid=notion-comment-composer-submit]").HasAttribute("disabled").Should().BeTrue();
        cut.Markup.Should().Contain("AL");

        var input = cut.Find("textarea.tm-nbcp__reply-input");
        input.Input("hello");
        cut.Find("[data-testid=notion-comment-composer-submit]").HasAttribute("disabled").Should().BeFalse();

        input.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        submitted.Should().Be("hello");
    }

    [Fact]
    public void TextPanel_Input_IsTmCommentMentionInput()
    {
        var cut = Render<TmNotionTextCommentPanel>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.BlockId, "block-1")
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = default!,
                BlockService = default!,
                CommentProvider = new EmptyCommentProvider()
            }));

        cut.FindAll(".tm-ntcp__reply .tm-comment-mention-input").Should().ContainSingle();
        cut.Find("textarea.tm-ntcp__reply-input").Should().NotBeNull();
    }

    private static List<CommentThreadNode> Tree(DateTimeOffset created, bool canEdit = false, bool canDelete = false)
    {
        var entry = new TmCommentEntry
        {
            Id = "entry-1",
            Author = new TmUserRef { Id = "ada", DisplayName = "Ada" },
            Body = "hello",
            CreatedAt = created,
            CanEdit = canEdit,
            CanDelete = canDelete
        };
        return CommentThreadHelper.BuildTree([entry]).ToList();
    }

    private sealed class EmptyCommentProvider : ITmCommentProvider
    {
        public TmCommentProviderCapabilities Capabilities => TmCommentProviderCapabilities.Read;

        public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
            TmEntityRef entityRef, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmCommentThread>>([]);

        public Task<TmCommentThread> CreateThreadAsync(TmCommentThread thread, CancellationToken cancellationToken = default)
            => Task.FromResult(thread);

        public Task<TmCommentEntry> ReplyAsync(string threadId, TmCommentEntry entry, CancellationToken cancellationToken = default)
            => Task.FromResult(entry);

        public Task<TmCommentEntry> UpdateEntryAsync(string threadId, string entryId, TmCommentEntry entry, CancellationToken cancellationToken = default)
            => Task.FromResult(entry);

        public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteEntryAsync(string threadId, string entryId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<TmCommentThread> ResolveAsync(string threadId, TmUserRef? resolvedBy = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new TmCommentThread { Id = threadId });

        public Task<TmCommentThread> ReopenAsync(string threadId, TmUserRef? reopenedBy = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new TmCommentThread { Id = threadId });
    }
}
