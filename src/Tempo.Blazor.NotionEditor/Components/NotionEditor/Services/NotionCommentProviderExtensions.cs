using System.Net;
using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Components.NotionEditor.Services;

internal static class NotionCommentProviderExtensions
{
    private const string PageEntityType = "notion-page";
    private const string BlockEntityType = "notion-block";

    public static TmEntityRef PageEntity(string pageId)
        => TmEntityRef.Create(PageEntityType, pageId);

    public static TmEntityRef BlockEntity(string blockId)
        => TmEntityRef.Create(BlockEntityType, blockId);

    public static async Task<IReadOnlyList<TmCommentThread>> GetPageCommentsAsync(
        this ITmCommentProvider provider,
        string pageId)
    {
        return await provider.GetForEntityAsync(PageEntity(pageId));
    }

    public static async Task<IReadOnlyList<TmCommentThread>> GetBlockCommentsAsync(
        this ITmCommentProvider provider,
        string blockId)
    {
        return await provider.GetForEntityAsync(BlockEntity(blockId));
    }

    /// <summary>
    /// Creates a page comment authored by <paramref name="currentUserId"/>. A null or blank id is stored
    /// as an empty author id — never the historical <c>"demo"</c> stand-in.
    /// </summary>
    public static async Task<TmCommentThread> AddPageCommentAsync(
        this ITmCommentProvider provider,
        string pageId,
        string htmlContent,
        string? currentUserId)
    {
        return await provider.CreateThreadAsync(new TmCommentThread
        {
            EntityRef = PageEntity(pageId),
            Anchor = TmCommentAnchor.None(),
            Visibility = TmCommentVisibility.Internal,
            Entries = [CreateEntry(string.Empty, htmlContent, currentUserId: currentUserId)]
        });
    }

    /// <summary>
    /// Creates a block comment authored by <paramref name="currentUserId"/>. A null or blank id is stored
    /// as an empty author id — never the historical <c>"demo"</c> stand-in.
    /// </summary>
    public static async Task<TmCommentThread> AddBlockCommentAsync(
        this ITmCommentProvider provider,
        string blockId,
        string htmlContent,
        string? currentUserId)
    {
        return await provider.CreateThreadAsync(new TmCommentThread
        {
            EntityRef = BlockEntity(blockId),
            Anchor = TmCommentAnchor.Block(blockId),
            Visibility = TmCommentVisibility.Internal,
            Entries = [CreateEntry(string.Empty, htmlContent, currentUserId: currentUserId)]
        });
    }

    public static async Task<TmCommentThread> AddTextAnchorCommentAsync(
        this ITmCommentProvider provider,
        string blockId,
        int startOffset,
        int endOffset,
        string highlightedText,
        string htmlContent,
        string commentId,
        string? currentUserId)
    {
        var thread = new TmCommentThread
        {
            Id = string.IsNullOrWhiteSpace(commentId) ? Guid.NewGuid().ToString("N") : commentId,
            EntityRef = BlockEntity(blockId),
            Anchor = TmCommentAnchor.TextRange(blockId, startOffset, endOffset, highlightedText),
            Visibility = TmCommentVisibility.Internal
        };
        thread.Entries.Add(CreateEntry(thread.Id, htmlContent, currentUserId: currentUserId));
        return await provider.CreateThreadAsync(thread);
    }

    public static async Task<int> GetUnresolvedCommentsCountAsync(
        this ITmCommentProvider provider,
        string pageId)
    {
        var comments = await provider.GetPageCommentsAsync(pageId);
        return comments.Count(comment => comment.Status != TmCommentThreadStatus.Resolved);
    }

    public static async Task<TmCommentEntry> ReplyToCommentAsync(
        this ITmCommentProvider provider,
        string threadId,
        string htmlContent,
        string? parentEntryId,
        string? currentUserId)
    {
        return await provider.ReplyAsync(threadId, CreateEntry(threadId, htmlContent, parentEntryId, currentUserId));
    }

    public static async Task<TmCommentEntry> EditCommentAsync(
        this ITmCommentProvider provider,
        string threadId,
        string entryId,
        string htmlContent,
        string? currentUserId)
    {
        var userId = currentUserId?.Trim() ?? string.Empty;
        return await provider.UpdateEntryAsync(threadId, entryId, new TmCommentEntry
        {
            Id = entryId,
            ThreadId = threadId,
            Body = htmlContent,
            BodyFormat = TmCommentBodyFormat.Html,
            EditedAt = DateTimeOffset.UtcNow,
            Author = new TmUserRef { Id = userId, DisplayName = userId }
        });
    }

    public static async Task DeleteCommentAsync(this ITmCommentProvider provider, string threadId)
    {
        await provider.DeleteThreadAsync(threadId);
    }

    public static async Task DeleteCommentEntryAsync(
        this ITmCommentProvider provider,
        string threadId,
        string entryId)
    {
        await provider.DeleteEntryAsync(threadId, entryId);
    }

    public static async Task<TmCommentThread> ResolveCommentAsync(
        this ITmCommentProvider provider,
        string threadId,
        string? currentUserId)
    {
        var userId = currentUserId?.Trim() ?? string.Empty;
        return await provider.ResolveAsync(threadId, new TmUserRef { Id = userId, DisplayName = userId });
    }

    public static async Task<TmCommentThread> UnresolveCommentAsync(
        this ITmCommentProvider provider,
        string threadId,
        string? currentUserId)
    {
        var userId = currentUserId?.Trim() ?? string.Empty;
        return await provider.ReopenAsync(threadId, new TmUserRef { Id = userId, DisplayName = userId });
    }

    public static async Task MarkThreadAsReadAsync(
        this ITmCommentProvider provider,
        string threadId,
        string userId)
    {
        if (provider is ITmCommentReadTrackingProvider readTracking)
            await readTracking.MarkThreadAsReadAsync(threadId, userId);
    }

    public static async Task MarkThreadAsUnreadAsync(
        this ITmCommentProvider provider,
        string threadId,
        string userId)
    {
        if (provider is ITmCommentReadTrackingProvider readTracking)
            await readTracking.MarkThreadAsUnreadAsync(threadId, userId);
    }

    public static async Task MarkAllPageThreadsAsReadAsync(
        this ITmCommentProvider provider,
        string pageId,
        string userId)
    {
        if (provider is ITmCommentReadTrackingProvider readTracking)
            await readTracking.MarkAllForEntityAsReadAsync(PageEntity(pageId), userId);
    }

    public static async Task MarkAllBlockThreadsAsReadAsync(
        this ITmCommentProvider provider,
        string blockId,
        string userId)
    {
        if (provider is ITmCommentReadTrackingProvider readTracking)
            await readTracking.MarkAllForEntityAsReadAsync(BlockEntity(blockId), userId);
    }

    public static async Task SubscribeToThreadAsync(
        this ITmCommentProvider provider,
        string threadId,
        string userId)
    {
        if (provider is ITmCommentSubscriptionProvider subscriptions)
            await subscriptions.SubscribeAsync(threadId, userId);
    }

    public static async Task UnsubscribeFromThreadAsync(
        this ITmCommentProvider provider,
        string threadId,
        string userId)
    {
        if (provider is ITmCommentSubscriptionProvider subscriptions)
            await subscriptions.UnsubscribeAsync(threadId, userId);
    }

    public static bool IsResolved(this TmCommentThread comment)
        => comment.Status == TmCommentThreadStatus.Resolved;

    public static DateTimeOffset? LastActivityAt(this TmCommentThread comment)
        => comment.UpdatedAt
        ?? comment.Entries
            .OrderByDescending(entry => entry.EditedAt ?? entry.CreatedAt)
            .FirstOrDefault()?.CreatedAt;

    public static string HtmlContent(this TmCommentEntry entry)
        => entry.BodyFormat == TmCommentBodyFormat.Html
            ? entry.Body
            : WebUtility.HtmlEncode(entry.Body).Replace("\n", "<br/>", StringComparison.Ordinal);

    public static string AuthorDisplayName(this TmCommentEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Author.DisplayName)
            ? entry.Author.DisplayName
            : entry.Author.Id;

    public static string? AuthorAvatarUrl(this TmCommentEntry entry)
        => entry.Author.AvatarUrl;

    private static TmCommentEntry CreateEntry(
        string threadId,
        string htmlContent,
        string? parentEntryId = null,
        string? currentUserId = null)
    {
        var userId = currentUserId?.Trim() ?? string.Empty;
        return new TmCommentEntry
        {
            ThreadId = threadId,
            ParentEntryId = string.IsNullOrWhiteSpace(parentEntryId) ? null : parentEntryId,
            Author = new TmUserRef { Id = userId, DisplayName = userId },
            Body = htmlContent,
            BodyFormat = TmCommentBodyFormat.Html,
            CreatedAt = DateTimeOffset.UtcNow,
            CanEdit = true,
            CanDelete = true
        };
    }
}
