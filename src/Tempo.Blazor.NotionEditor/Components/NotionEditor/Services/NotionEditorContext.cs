using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Services;

/// <summary>
/// Cascades all NotionEditor provider references to the component tree.
/// Child components receive this via <c>[CascadingParameter] NotionEditorContext Context</c>
/// and do not require individual DI registrations.
/// </summary>
public sealed class NotionEditorContext
{
    public string                      CurrentUserId        { get; init; } = "demo";
    public string?                     CurrentPageId        { get; init; }
    public INotionDataProvider         DataProvider       { get; init; } = default!;
    public INotionEditorBlockService   BlockService      { get; init; } = default!;
    public NotionEditorAggregateSession? AggregateSession { get; init; }
    public INotionSearchProvider?      SearchProvider        { get; init; }
    public INotionDatabaseProvider?    DatabaseProvider      { get; init; }
    public ITmCommentProvider?         CommentProvider       { get; init; }
    public INotionVersionProvider?     HistoryProvider       { get; init; }
    public INotionCollaborationProvider? CollaborationProvider { get; init; }
    public ITmPeopleProvider?          MentionProvider       { get; init; }
    public INotionAIProvider?          AIProvider            { get; init; }
    public ITmWorkItemProvider?        WorkItemSource        { get; init; }
    public TmWorkItemProviderRegistry? WorkItemProviders     { get; init; }
    public INotionReactionProvider?    ReactionProvider      { get; init; }
    public INotionAnalyticsProvider?   AnalyticsProvider     { get; init; }
    public INotionBlogProvider?        BlogProvider          { get; init; }
    public INotionWatchProvider?       WatchProvider         { get; init; }
    public INotionSpaceProvider?       SpaceProvider         { get; init; }
    public INotionPagePropertiesProvider? PagePropertiesProvider { get; init; }
    public INotionTemplateProvider?      TemplateProvider         { get; init; }
    public ISmartLinkProvider?           SmartLinkProvider        { get; init; }
    public ITmAuthorizationProvider?      AuthorizationProvider    { get; init; }
    public INotionPermissionProvider?    PermissionProvider       { get; init; }
    public INotionPublicShareProvider?   PublicShareProvider      { get; init; }
    public ITmActivityProvider?          AuditProvider            { get; init; }
    public IReadOnlyList<string>         CurrentUserGroupIds      { get; init; } = [];
    public INotionBookmarkProvider?      BookmarkProvider         { get; init; }
    public ITmFileProvider?              FileProvider             { get; init; }
    public INotionImportExportProvider?  ImportExportProvider     { get; init; }
    public IDiagramDocumentProvider?     DiagramDocumentProvider  { get; init; }
    public IWireframeDocumentProvider?    WireframeDocumentProvider  { get; init; }
    public ISpreadsheetDocumentProvider?  SpreadsheetDocumentProvider{ get; init; }
    public INotionSyncedBlockProvider?    SyncedBlockProvider        { get; init; }
    public INotionMediaLibraryProvider?   MediaLibraryProvider       { get; init; }
    public ITokenDataProvider?            TokenProvider              { get; init; }

    /// <summary>
    /// Optional document library used by the Tempo blocks (wireframe/diagram/spreadsheet) to offer
    /// "Insert existing…", render previews and refresh them. When null, only "Create" is offered.
    /// </summary>
    public Tempo.Blazor.DocumentLibrary.ITempoDocumentLibraryProvider? DocumentLibraryProvider { get; init; }

    /// <summary>
    /// Optional live-change notifier. When present, embedded Tempo blocks subscribe to their
    /// linked document and refresh automatically when it changes elsewhere.
    /// </summary>
    public Tempo.Blazor.DocumentLibrary.ITempoDocumentChangeNotifier? DocumentChangeNotifier { get; init; }

    /// <summary>Active collaboration sync service (null when CollaborationProvider is absent).</summary>
    public NotionCollaborationSync?        CollaborationSync         { get; init; }

    /// <summary>
    /// Navigates to the given page by ID. Supplied by TmNotionEditor and used by
    /// child page / linked page / breadcrumb blocks to trigger in-editor navigation.
    /// </summary>
    public Func<string, Task>?            NavigateTo               { get; init; }

    /// <summary>Currently selected workspace/space identifier for sidebar-scoped navigation.</summary>
    public string?                         SelectedSpaceId          { get; init; }

    /// <summary>Raised by the sidebar space switcher when the active space changes.</summary>
    public Func<string?, Task>?            SelectSpace              { get; init; }

    /// <summary>
    /// How the block / text comment panels present (NO1): <c>Popover</c> keeps the anchored
    /// bubble, <c>Dialog</c> the core modal, <c>Auto</c> (the default) resolves per layout.
    /// Set from <c>TmNotionEditor.CommentPresentation</c>.
    /// </summary>
    public NotionCommentPresentation CommentPresentation { get; init; } = NotionCommentPresentation.Auto;

    /// <summary>Raised after the active page is moved to another space.</summary>
    public Func<string, Task>?             CurrentPageMovedToSpace  { get; init; }

    /// <summary>Opens the new-page template gallery from the sidebar.</summary>
    public Func<Task>?                     OpenTemplateGallery      { get; init; }

    /// <summary>
    /// Page-level actions (add block, comments, page settings) the rendered
    /// <see cref="Page.TmNotionPage"/> registers for chrome above it — the mobile action bar
    /// invokes these instead of duplicating page logic. Preserved across <c>BuildContext</c>
    /// rebuilds; populated by the page itself.
    /// </summary>
    internal NotionPageActions? PageActions { get; set; }

    /// <summary>
    /// Raised by the page right after it (re)registers <see cref="PageActions"/> — the editor
    /// re-renders the chrome (the mobile bar) so the new actions appear without waiting for the
    /// next editor-owned state change.
    /// </summary>
    internal Func<Task>? PageActionsChanged { get; set; }

    /// <summary>
    /// Raised when a block type is converted (e.g. via slash menu or toolbar).
    /// Nested components such as column blocks subscribe to keep their local
    /// child cache in sync with the remote store.
    /// </summary>
    public Action<IPageBlock>? BlockConverted;

    /// <summary>Invokes <see cref="BlockConverted"/> if any subscriber is attached.</summary>
    public void RaiseBlockConverted(IPageBlock block) => BlockConverted?.Invoke(block);

    /// <summary>Raised by nested blocks when they create a sibling block through the shared provider.</summary>
    public Func<IPageBlock, Task>? BlockCreated { get; set; }

    /// <summary>Invokes <see cref="BlockCreated"/> if the active page subscribed to sibling creation events.</summary>
    public Task RaiseBlockCreatedAsync(IPageBlock block) => BlockCreated?.Invoke(block) ?? Task.CompletedTask;

    /// <summary>
    /// When non-null, only these block types are available in the slash menu and type
    /// conversion ("Turn Into") menus. Existing blocks of other types still render normally.
    /// When null, all block types are allowed (default).
    /// </summary>
    public IReadOnlySet<BlockType>? AllowedBlockTypes { get; init; }

    /// <summary>
    /// Block types that must never be created/converted-to regardless of <see cref="AllowedBlockTypes"/>.
    /// Used by single-page mode to hide multi-page blocks (child pages, linked pages/databases, etc.).
    /// When null, nothing extra is denied.
    /// </summary>
    public IReadOnlySet<BlockType>? DeniedBlockTypes { get; init; }

    /// <summary>True when <see cref="DeniedBlockTypes"/> restricts at least one block type.</summary>
    public bool HasDeniedBlockTypes => DeniedBlockTypes is { Count: > 0 };

    /// <summary>Returns true when the given block type may be created or converted to.</summary>
    public bool IsBlockTypeAllowed(BlockType type)
        => (AllowedBlockTypes is null || AllowedBlockTypes.Contains(type))
        && (DeniedBlockTypes is null || !DeniedBlockTypes.Contains(type));
}
