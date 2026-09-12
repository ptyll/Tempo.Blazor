using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Helpers;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Keyboard-isolation regression tests for the comment panels whose entry rows
/// are non-native focusables (<c>&lt;div tabindex="0"&gt;</c>) that legitimately
/// map Enter to "start inline reply". The native controls nested inside an
/// entry — action buttons, reaction buttons, edit/reply textareas — now stop
/// their keydowns from reaching that handler. Without the boundary, Enter on
/// the Edit button both opened the editor (native click) and started a reply
/// (bubbled keydown) for a single key press.
/// </summary>
public sealed class TmNotionCommentPanelKeyboardTests : LocalizationTestBase
{
    private const string BlockId  = "block-1";
    private const string ThreadId = "thread-1";

    public TmNotionCommentPanelKeyboardTests()
    {
        Services.AddSingleton<ITmNotificationService, NoOpNotificationService>();
        Services.AddScoped<CommentNotificationOrchestrator>();
    }

    // ── TmNotionTextCommentPanel ────────────────────────────────────────────

    [Fact]
    public void TextPanel_EnterOnEntryEditButton_OpensEdit_WithoutStartingReply()
    {
        var cut = RenderTextPanel();
        var edit = FindAction(cut, "tm-ntcp__entry-action", "Edit");

        var act = () => edit.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the actions group stops keydowns before the entry's Enter->reply handler");
        cut.FindAll(".tm-ntcp__inline-reply").Should().BeEmpty();

        // The native click half of the sequence still does the button's own job.
        FindAction(cut, "tm-ntcp__entry-action", "Edit").Click();
        cut.FindAll(".tm-ntcp__edit-wrap").Should().ContainSingle();
        cut.FindAll(".tm-ntcp__inline-reply").Should().BeEmpty();
    }

    [Fact]
    public void TextPanel_EnterOnEntryDeleteButton_Confirms_WithoutStartingReply()
    {
        var cut = RenderTextPanel();
        var delete = FindAction(cut, "tm-ntcp__entry-action", "Delete");

        var act = () => delete.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>();

        FindAction(cut, "tm-ntcp__entry-action", "Delete").Click();
        cut.FindAll(".tm-dialog").Should().ContainSingle("the delete click shows its confirm dialog");
        cut.FindAll(".tm-ntcp__inline-reply").Should().BeEmpty();
    }

    [Fact]
    public void TextPanel_EnterInEditTextarea_DoesNotStartReply_EscapeCancelsEdit()
    {
        var cut = RenderTextPanel();
        FindAction(cut, "tm-ntcp__entry-action", "Edit").Click();
        cut.FindAll(".tm-ntcp__edit-wrap").Should().ContainSingle();

        // Enter inside the edit textarea inserts a newline natively; the edit
        // wrap stops the keydown so the entry must not start a reply.
        cut.Find("textarea.tm-ntcp__edit-input")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-ntcp__edit-wrap").Should().ContainSingle();
        cut.FindAll(".tm-ntcp__inline-reply").Should().BeEmpty();

        // Escape->cancel previously relied on bubbling to the entry handler;
        // the textarea now handles it locally.
        cut.Find("textarea.tm-ntcp__edit-input")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-ntcp__edit-wrap").Should().BeEmpty();
    }

    [Fact]
    public void TextPanel_EnterOnEntryItself_StillStartsInlineReply()
    {
        // Legitimate emulation kept: the entry row is a non-native focusable.
        var cut = RenderTextPanel();
        cut.Find(".tm-ntcp__entry").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-ntcp__inline-reply").Should().ContainSingle();
    }

    // ── TmNotionBlockCommentPanel ───────────────────────────────────────────

    [Fact]
    public void BlockPanel_EnterOnEntryEditButton_OpensEdit_WithoutStartingReply()
    {
        var cut = RenderBlockPanel();
        var edit = FindAction(cut, "tm-nbcp__entry-action", "Edit");

        var act = () => edit.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the actions group stops keydowns before the entry's Enter->reply handler");
        cut.FindAll(".tm-nbcp__inline-reply").Should().BeEmpty();

        FindAction(cut, "tm-nbcp__entry-action", "Edit").Click();
        cut.FindAll(".tm-nbcp__edit-wrap").Should().ContainSingle();
        cut.FindAll(".tm-nbcp__inline-reply").Should().BeEmpty();
    }

    [Fact]
    public void BlockPanel_EnterOnEntryDeleteButton_Confirms_WithoutStartingReply()
    {
        var cut = RenderBlockPanel();
        var delete = FindAction(cut, "tm-nbcp__entry-action", "Delete");

        var act = () => delete.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>();

        FindAction(cut, "tm-nbcp__entry-action", "Delete").Click();
        cut.FindAll(".tm-dialog").Should().ContainSingle();
        cut.FindAll(".tm-nbcp__inline-reply").Should().BeEmpty();
    }

    [Fact]
    public void BlockPanel_EnterInEditTextarea_DoesNotStartReply_EscapeCancelsEdit()
    {
        var cut = RenderBlockPanel();
        FindAction(cut, "tm-nbcp__entry-action", "Edit").Click();
        cut.FindAll(".tm-nbcp__edit-wrap").Should().ContainSingle();

        cut.Find("textarea.tm-nbcp__edit-input")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-nbcp__edit-wrap").Should().ContainSingle();
        cut.FindAll(".tm-nbcp__inline-reply").Should().BeEmpty();

        cut.Find("textarea.tm-nbcp__edit-input")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-nbcp__edit-wrap").Should().BeEmpty();
    }

    [Fact]
    public void BlockPanel_EnterOnEntryItself_StillStartsInlineReply()
    {
        var cut = RenderBlockPanel();
        cut.Find(".tm-nbcp__entry").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-nbcp__inline-reply").Should().ContainSingle();
    }

    // ── Hosts ───────────────────────────────────────────────────────────────

    private IRenderedComponent<TextPanelHost> RenderTextPanel()
        => Render<TextPanelHost>(p => p
            .Add(h => h.Context, CreateContext())
            .Add(h => h.CommentId, ThreadId)
            .Add(h => h.BlockId, BlockId));

    private IRenderedComponent<BlockPanelHost> RenderBlockPanel()
        => Render<BlockPanelHost>(p => p
            .Add(h => h.Context, CreateContext())
            .Add(h => h.BlockId, BlockId));

    private static NotionEditorContext CreateContext()
        => new()
        {
            DataProvider    = default!,
            BlockService    = default!,
            CommentProvider = new FakeBlockCommentProvider(BlockId, ThreadId)
        };

    private static IElement FindAction<T>(IRenderedComponent<T> cut, string actionClass, string titlePart)
        where T : Microsoft.AspNetCore.Components.IComponent
        => cut.FindAll($".{actionClass}")
              .First(e => e.GetAttribute("title")?.Contains(titlePart) == true);

    public sealed class TextPanelHost : ComponentBase
    {
        [Parameter] public NotionEditorContext Context { get; set; } = default!;
        [Parameter] public string CommentId { get; set; } = string.Empty;
        [Parameter] public string BlockId { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", Context);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<TmNotionTextCommentPanel>(0);
                child.AddAttribute(1, "Visible", true);
                child.AddAttribute(2, "CommentId", CommentId);
                child.AddAttribute(3, "BlockId", BlockId);
                child.AddAttribute(4, "HighlightedText", "highlighted");
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    public sealed class BlockPanelHost : ComponentBase
    {
        [Parameter] public NotionEditorContext Context { get; set; } = default!;
        [Parameter] public string BlockId { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<NotionEditorContext>>(0);
            builder.AddAttribute(1, "Value", Context);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<TmNotionBlockCommentPanel>(0);
                child.AddAttribute(1, "Visible", true);
                child.AddAttribute(2, "BlockId", BlockId);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class FakeBlockCommentProvider : ITmCommentProvider, ITmCommentReactionProvider
    {
        private readonly Dictionary<string, TmCommentThread> _threads = [];

        public FakeBlockCommentProvider(string blockId, string threadId)
        {
            var thread = new TmCommentThread
            {
                Id = threadId,
                EntityRef = TmEntityRef.Create("notion-block", blockId),
                Visibility = TmCommentVisibility.Internal
            };
            thread.Entries.Add(new TmCommentEntry
            {
                Id = "entry-1",
                ThreadId = threadId,
                Author = new TmUserRef { Id = "demo", DisplayName = "Demo User" },
                Body = "first entry",
                CanEdit = true,
                CanDelete = true
            });
            _threads[threadId] = thread;
        }

        public TmCommentProviderCapabilities Capabilities =>
            TmCommentProviderCapabilities.Read
            | TmCommentProviderCapabilities.CreateThread
            | TmCommentProviderCapabilities.Reply
            | TmCommentProviderCapabilities.Delete
            | TmCommentProviderCapabilities.Resolve
            | TmCommentProviderCapabilities.Reactions;

        public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
            TmEntityRef entityRef,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmCommentThread>>(
                _threads.Values.Where(t => t.EntityRef.EntityId == entityRef.EntityId).ToList());

        public Task<TmCommentThread> CreateThreadAsync(
            TmCommentThread thread,
            CancellationToken cancellationToken = default)
        {
            thread.Id = Guid.NewGuid().ToString("N");
            _threads[thread.Id] = thread;
            return Task.FromResult(thread);
        }

        public Task<TmCommentEntry> ReplyAsync(
            string threadId,
            TmCommentEntry entry,
            CancellationToken cancellationToken = default)
        {
            entry.Id = Guid.NewGuid().ToString("N");
            _threads[threadId].Entries.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<TmCommentEntry> UpdateEntryAsync(
            string threadId,
            string entryId,
            TmCommentEntry entry,
            CancellationToken cancellationToken = default)
        {
            var existing = _threads[threadId].Entries.Single(e => e.Id == entryId);
            existing.Body = entry.Body;
            return Task.FromResult(existing);
        }

        public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
        {
            _threads.Remove(threadId);
            return Task.CompletedTask;
        }

        public Task DeleteEntryAsync(
            string threadId,
            string entryId,
            CancellationToken cancellationToken = default)
        {
            _threads[threadId].Entries.RemoveAll(e => e.Id == entryId);
            return Task.CompletedTask;
        }

        public Task<TmCommentThread> ResolveAsync(
            string threadId,
            TmUserRef? resolvedBy = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_threads[threadId]);

        public Task<TmCommentThread> ReopenAsync(
            string threadId,
            TmUserRef? reopenedBy = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_threads[threadId]);

        public Task<IReadOnlyList<TmCommentReaction>> GetReactionsAsync(
            string entryId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmCommentReaction>>([]);

        public Task AddReactionAsync(
            string entryId,
            string value,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveReactionAsync(
            string entryId,
            string value,
            string userId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
