using Bunit;
using Microsoft.AspNetCore.Components;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Draft-guard surface of <see cref="TmDocumentCommentComposer"/>. The composer holds its text in
/// a private field — no host can observe it through a parameter — so these tests pin the additive
/// surface (OnDraftChanged / HasUnsavedDraft / DiscardDraftAsync).
/// </summary>
public class TmDocumentCommentComposerDraftGuardTests : LocalizationTestBase
{
    [Fact]
    public void Composer_Typing_FiresOnDraftChanged()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentComposer>(p => p
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-input']").Input("New comment");

        states.Should().ContainSingle().Which.Should().Be(new TmCommentDraftState(true, null, true));
        cut.Instance.HasUnsavedDraft.Should().BeTrue();
    }

    [Fact]
    public void Composer_DedupsNotificationsWhileDraftStateUnchanged()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentComposer>(p => p
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-input']").Input("a");
        cut.Find("[data-testid='document-comment-input']").Input("ab");

        states.Should().ContainSingle("value-equality dedup suppresses identical states")
            .Which.HasUnsavedContent.Should().BeTrue();

        cut.Find("[data-testid='document-comment-input']").Input("");
        states.Should().HaveCount(2);
        states[1].Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public void Composer_SeededEdit_OpenedWithoutChanges_IsNotAnUnsavedDraft()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentComposer>(p => p
            .Add(c => c.InitialText, "Original text")
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Instance.HasUnsavedDraft.Should().BeFalse("a freshly seeded edit is not a draft");
        states.Should().BeEmpty();

        cut.Find("[data-testid='document-comment-input']").Input("Original text v2");
        states.Should().ContainSingle().Which.HasUnsavedContent.Should().BeTrue();

        // Reverting to the seeded text clears the flag again — stateless comparison.
        cut.Find("[data-testid='document-comment-input']").Input("Original text");
        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        states[^1].Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public async Task Composer_DiscardDraftAsync_ClearsText_AndFiresClearedState()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentComposer>(p => p
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        cut.Find("[data-testid='document-comment-input']").Input("draft");
        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        cut.Instance.HasUnsavedDraft.Should().BeFalse();
        (cut.Find("[data-testid='document-comment-input']").GetAttribute("value") ?? string.Empty).Should().BeEmpty();
        states.Last().Should().Be(new TmCommentDraftState(true, null, false));
    }

    [Fact]
    public async Task Composer_DiscardDraftAsync_WithNoDraft_IsANoOp_WithoutNotification()
    {
        var states = new List<TmCommentDraftState>();
        var cut = Render<TmDocumentCommentComposer>(p => p
            .Add(c => c.OnDraftChanged, EventCallback.Factory.Create<TmCommentDraftState>(this, s => states.Add(s))));

        await cut.InvokeAsync(() => cut.Instance.DiscardDraftAsync());

        states.Should().BeEmpty("the dedup suppresses a notification when nothing changed");
    }
}
