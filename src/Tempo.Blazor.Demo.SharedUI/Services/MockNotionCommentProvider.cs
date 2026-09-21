using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.NotionEditor.Helpers;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// In-memory scoped comment provider for the demo. Pre-seeded with threads on
/// the demo Notion pages. All mutation methods update in-memory state only.
/// </summary>
public class MockNotionCommentProvider :
    ITmCommentProvider,
    ITmCommentReactionProvider,
    ITmCommentReadTrackingProvider,
    ITmCommentSubscriptionProvider
{
    private readonly Dictionary<string, TmCommentThread> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _idx = new(StringComparer.Ordinal);

    private static readonly Guid Page1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Page2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly CommentNotificationOrchestrator? _orchestrator;

    public MockNotionCommentProvider(ITmNotificationService? notificationService = null)
    {
        _orchestrator = notificationService is not null
            ? new CommentNotificationOrchestrator(notificationService)
            : null;
        Seed();
    }

    public TmCommentProviderCapabilities Capabilities =>
        TmCommentProviderCapabilities.Read
        | TmCommentProviderCapabilities.CreateThread
        | TmCommentProviderCapabilities.Reply
        | TmCommentProviderCapabilities.EditEntry
        | TmCommentProviderCapabilities.Delete
        | TmCommentProviderCapabilities.Resolve
        | TmCommentProviderCapabilities.Reactions
        | TmCommentProviderCapabilities.ReadTracking
        | TmCommentProviderCapabilities.Subscriptions
        | TmCommentProviderCapabilities.RichText;

    private void Seed()
    {
        var pageRef = TmEntityRef.Create("notion-page", Page1Id.ToString("D"));

        var c1e1 = MakeEntry("alice", "Alice Johnson", "https://i.pravatar.cc/150?u=alice",
            "Great overview! Should we add a keyboard shortcut cheat sheet?",
            DateTimeOffset.UtcNow.AddDays(-3));
        var c1e2 = MakeEntry("bob", "Bob Smith", "https://i.pravatar.cc/150?u=bob",
            "Good idea - added a <strong>Keyboard Shortcuts</strong> callout below.",
            DateTimeOffset.UtcNow.AddDays(-2));
        c1e1.Reactions.Add(new TmCommentReaction { Value = "👍", UserIds = ["bob", "charlie"] });
        c1e1.Reactions.Add(new TmCommentReaction { Value = "🔥", UserIds = ["diana"] });

        var c1 = NewThread(pageRef, c1e1, c1e2);
        c1.Status = TmCommentThreadStatus.Resolved;
        c1.ResolvedAt = DateTimeOffset.UtcNow.AddDays(-2).AddHours(1);
        c1.ResolvedBy = new TmUserRef { Id = "bob", DisplayName = "Bob Smith" };
        Register(c1);

        var c2e1 = MakeEntry("charlie", "Charlie Brown", null,
            "Can we add a <em>board view</em> database demo in a separate sub-page?",
            DateTimeOffset.UtcNow.AddHours(-6));
        c2e1.Reactions.Add(new TmCommentReaction { Value = "👍", UserIds = ["bob"] });
        Register(NewThread(pageRef, c2e1));

        var page2Ref = TmEntityRef.Create("notion-page", Page2Id.ToString("D"));
        Register(NewThread(page2Ref,
            MakeEntry("diana", "Diana Prince", "https://i.pravatar.cc/150?u=diana",
                "Should the Q2 items be moved into a separate database for tracking?",
                DateTimeOffset.UtcNow.AddHours(-3))));
    }

    public Task<IReadOnlyList<TmCommentThread>> GetForEntityAsync(
        TmEntityRef entityRef,
        CancellationToken cancellationToken = default)
    {
        var key = entityRef.Normalize().ToQualifiedKey();
        var result = _idx.TryGetValue(key, out var ids)
            ? ids.Where(_byId.ContainsKey).Select(id => _byId[id]).ToList()
            : [];

        return Task.FromResult<IReadOnlyList<TmCommentThread>>(result);
    }

    public Task<TmCommentThread> CreateThreadAsync(
        TmCommentThread thread,
        CancellationToken cancellationToken = default)
    {
        NormalizeThread(thread);
        Register(thread);
        return Task.FromResult(thread);
    }

    public async Task<TmCommentEntry> ReplyAsync(
        string threadId,
        TmCommentEntry entry,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        NormalizeEntry(entry, thread.Id);
        thread.Entries.Add(entry);
        thread.UpdatedAt = DateTimeOffset.UtcNow;

        if (!thread.SubscribedUserIds.Contains(entry.Author.Id))
            thread.SubscribedUserIds.Add(entry.Author.Id);

        if (_orchestrator is not null)
            await _orchestrator.OnNewReplyAsync(thread, entry, cancellationToken);

        return entry;
    }

    public Task<TmCommentEntry> UpdateEntryAsync(
        string threadId,
        string entryId,
        TmCommentEntry entry,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        var existing = thread.Entries.FirstOrDefault(item => item.Id == entryId)
            ?? throw new KeyNotFoundException(entryId);

        existing.Body = entry.Body;
        existing.BodyFormat = entry.BodyFormat;
        existing.EditedAt = entry.EditedAt ?? DateTimeOffset.UtcNow;
        existing.Mentions = entry.Mentions;
        existing.Metadata = entry.Metadata;
        thread.UpdatedAt = DateTimeOffset.UtcNow;

        return Task.FromResult(existing);
    }

    public Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (_byId.Remove(threadId, out var thread))
        {
            var key = thread.EntityRef.Normalize().ToQualifiedKey();
            if (_idx.TryGetValue(key, out var ids))
                ids.Remove(threadId);
        }

        return Task.CompletedTask;
    }

    public Task DeleteEntryAsync(
        string threadId,
        string entryId,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        thread.Entries.RemoveAll(entry => entry.Id == entryId);
        thread.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public async Task<TmCommentThread> ResolveAsync(
        string threadId,
        TmUserRef? resolvedBy = null,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        thread.Status = TmCommentThreadStatus.Resolved;
        thread.ResolvedAt = DateTimeOffset.UtcNow;
        thread.ResolvedBy = resolvedBy ?? new TmUserRef { Id = "demo", DisplayName = "Demo User" };
        thread.UpdatedAt = DateTimeOffset.UtcNow;

        if (_orchestrator is not null)
            await _orchestrator.OnThreadResolvedAsync(thread, "demo", "Demo User", cancellationToken);

        return thread;
    }

    public Task<TmCommentThread> ReopenAsync(
        string threadId,
        TmUserRef? reopenedBy = null,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        thread.Status = TmCommentThreadStatus.Open;
        thread.ResolvedAt = null;
        thread.ResolvedBy = null;
        thread.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(thread);
    }

    public Task<IReadOnlyList<TmCommentReaction>> GetReactionsAsync(
        string entryId,
        CancellationToken cancellationToken = default)
    {
        var entry = FindEntry(entryId);
        return Task.FromResult<IReadOnlyList<TmCommentReaction>>(entry?.Reactions ?? []);
    }

    public async Task AddReactionAsync(
        string entryId,
        string value,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var (thread, entry) = RequireEntry(entryId);
        var reaction = entry.Reactions.FirstOrDefault(item => item.Value == value);
        if (reaction is null)
        {
            reaction = new TmCommentReaction { Value = value };
            entry.Reactions.Add(reaction);
        }

        if (!reaction.UserIds.Contains(userId))
            reaction.UserIds.Add(userId);

        thread.UpdatedAt = DateTimeOffset.UtcNow;

        if (_orchestrator is not null)
            await _orchestrator.OnReactionAsync(entry, value, userId, "Demo User", thread.Id, thread.EntityRef.EntityId, cancellationToken);
    }

    public Task RemoveReactionAsync(
        string entryId,
        string value,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var (thread, entry) = RequireEntry(entryId);
        var reaction = entry.Reactions.FirstOrDefault(item => item.Value == value);
        if (reaction is not null)
        {
            reaction.UserIds.Remove(userId);
            if (reaction.UserIds.Count == 0)
                entry.Reactions.Remove(reaction);
        }

        thread.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task MarkThreadAsReadAsync(
        string threadId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        if (!thread.ReadByUserIds.Contains(userId))
            thread.ReadByUserIds.Add(userId);
        return Task.CompletedTask;
    }

    public Task MarkThreadAsUnreadAsync(
        string threadId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        thread.ReadByUserIds.Remove(userId);
        return Task.CompletedTask;
    }

    public Task MarkAllForEntityAsReadAsync(
        TmEntityRef entityRef,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var key = entityRef.Normalize().ToQualifiedKey();
        if (_idx.TryGetValue(key, out var ids))
        {
            foreach (var id in ids)
            {
                if (_byId.TryGetValue(id, out var thread) && !thread.ReadByUserIds.Contains(userId))
                    thread.ReadByUserIds.Add(userId);
            }
        }

        return Task.CompletedTask;
    }

    public Task SubscribeAsync(
        string threadId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        if (!thread.SubscribedUserIds.Contains(userId))
            thread.SubscribedUserIds.Add(userId);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(
        string threadId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var thread = Require(threadId);
        thread.SubscribedUserIds.Remove(userId);
        return Task.CompletedTask;
    }

    private static TmCommentThread NewThread(TmEntityRef entityRef, params TmCommentEntry[] entries)
    {
        var thread = new TmCommentThread
        {
            EntityRef = entityRef,
            Anchor = TmCommentAnchor.None(),
            Visibility = TmCommentVisibility.Internal,
            CreatedAt = entries.Length > 0 ? entries.Min(entry => entry.CreatedAt) : DateTimeOffset.UtcNow,
            UpdatedAt = entries.Length > 0 ? entries.Max(entry => entry.CreatedAt) : DateTimeOffset.UtcNow,
            Entries = entries.ToList()
        };

        NormalizeThread(thread);
        return thread;
    }

    private static TmCommentEntry MakeEntry(
        string userId,
        string name,
        string? avatar,
        string html,
        DateTimeOffset at,
        string? parentEntryId = null)
    {
        return new TmCommentEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentEntryId = parentEntryId,
            Author = new TmUserRef
            {
                Id = userId,
                DisplayName = name,
                AvatarUrl = avatar
            },
            Body = html,
            BodyFormat = TmCommentBodyFormat.Html,
            CreatedAt = at,
            CanEdit = false,
            CanDelete = false
        };
    }

    private void Register(TmCommentThread thread)
    {
        _byId[thread.Id] = thread;
        var key = thread.EntityRef.Normalize().ToQualifiedKey();
        if (!_idx.TryGetValue(key, out var ids))
        {
            ids = [];
            _idx[key] = ids;
        }

        if (!ids.Contains(thread.Id))
            ids.Add(thread.Id);
    }

    private TmCommentThread Require(string id)
        => _byId.TryGetValue(id, out var thread) ? thread : throw new KeyNotFoundException(id);

    private TmCommentEntry? FindEntry(string entryId)
        => _byId.Values.SelectMany(thread => thread.Entries).FirstOrDefault(entry => entry.Id == entryId);

    private (TmCommentThread Thread, TmCommentEntry Entry) RequireEntry(string entryId)
    {
        foreach (var thread in _byId.Values)
        {
            var entry = thread.Entries.FirstOrDefault(item => item.Id == entryId);
            if (entry is not null)
                return (thread, entry);
        }

        throw new KeyNotFoundException(entryId);
    }

    private static void NormalizeThread(TmCommentThread thread)
    {
        if (string.IsNullOrWhiteSpace(thread.Id))
            thread.Id = Guid.NewGuid().ToString("N");

        thread.EntityRef = thread.EntityRef.Normalize();
        thread.CreatedAt = thread.CreatedAt == default ? DateTimeOffset.UtcNow : thread.CreatedAt;
        thread.UpdatedAt ??= thread.CreatedAt;

        foreach (var entry in thread.Entries)
            NormalizeEntry(entry, thread.Id);

        // Auto-subscribe each entry's author so a commenter follows their own thread
        // (matches Notion semantics: posting a comment subscribes you to replies).
        // New threads created via AddBlockCommentAsync/AddTextAnchorCommentAsync went
        // through here without this, leaving the author unwatched.
        foreach (var entry in thread.Entries)
            if (!string.IsNullOrWhiteSpace(entry.Author.Id) && !thread.SubscribedUserIds.Contains(entry.Author.Id))
                thread.SubscribedUserIds.Add(entry.Author.Id);
    }

    private static void NormalizeEntry(TmCommentEntry entry, string threadId)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
            entry.Id = Guid.NewGuid().ToString("N");
        entry.ThreadId = threadId;
        entry.CreatedAt = entry.CreatedAt == default ? DateTimeOffset.UtcNow : entry.CreatedAt;
        if (string.IsNullOrWhiteSpace(entry.Author.Id))
        {
            entry.Author.Id = "demo";
            entry.Author.DisplayName = "Demo User";
        }
    }
}
