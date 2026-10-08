using Microsoft.AspNetCore.SignalR.Client;
using Tempo.Blazor.DocumentLibrary;

namespace Tempo.Blazor.DocumentLibrary.Collaboration;

/// <summary>
/// SignalR-backed <see cref="ITempoDocumentChangeNotifier"/>. A client subscribes to the
/// specific documents it displays; the server broadcasts <see cref="TempoDocumentChange"/> to
/// the matching group so open editors and embedded blocks refresh without polling.
/// </summary>
/// <remarks>
/// Mirrors the dual-mode design of the document-editor collaboration provider: pass an
/// in-process <see cref="ITempoDocumentChangeNotifier"/> transport (tests/single-process), or a
/// hub URL / <see cref="HubConnection"/> for real SignalR.
/// </remarks>
public sealed class SignalRTempoDocumentChangeNotifier : ITempoDocumentChangeNotifier, IAsyncDisposable
{
    /// <summary>Hub method and group-name conventions shared by the client and the server hub.</summary>
    public static class HubMethods
    {
        /// <summary>Joins the group for a specific document.</summary>
        public const string JoinDocument = nameof(JoinDocument);

        /// <summary>Leaves the group for a specific document.</summary>
        public const string LeaveDocument = nameof(LeaveDocument);

        /// <summary>Client event carrying a remote document change.</summary>
        public const string RemoteDocumentChanged = nameof(RemoteDocumentChanged);
    }

    /// <summary>The SignalR group name for a given document.</summary>
    public static string GroupName(TempoDocumentKind kind, Guid documentId)
        => $"doclib:{kind}:{documentId}";

    private readonly ITempoDocumentChangeNotifier? _transport;
    private readonly HubConnection? _hub;
    private readonly Dictionary<(TempoDocumentKind Kind, Guid Id), int> _refCounts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    /// <inheritdoc />
    public event Func<TempoDocumentChange, CancellationToken, Task>? Changed;

    /// <summary>Creates an in-process adapter around an existing notifier (tests / single process).</summary>
    public SignalRTempoDocumentChangeNotifier(ITempoDocumentChangeNotifier transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.Changed += (change, ct) =>
            Changed is null ? Task.CompletedTask : Changed(change, ct);
    }

    /// <summary>Creates a SignalR notifier from a hub URL.</summary>
    public SignalRTempoDocumentChangeNotifier(string hubUrl)
        : this(new HubConnectionBuilder().WithUrl(hubUrl).WithAutomaticReconnect().Build())
    {
    }

    /// <summary>Creates a SignalR notifier from a prepared hub connection.</summary>
    public SignalRTempoDocumentChangeNotifier(HubConnection hub)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _hub.On<TempoDocumentChange>(HubMethods.RemoteDocumentChanged, change =>
            Changed is null ? Task.CompletedTask : Changed(change, CancellationToken.None));
        // SignalR does not restore group membership after an automatic reconnect —
        // without this the client silently stops receiving changes after any dropped connection.
        _hub.Reconnected += OnReconnectedAsync;
    }

    /// <inheritdoc />
    public async Task SubscribeAsync(
        TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
    {
        bool isFirst;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _refCounts.TryGetValue((kind, documentId), out var count);
            isFirst = count == 0;
            _refCounts[(kind, documentId)] = count + 1;
        }
        finally
        {
            _gate.Release();
        }

        if (!isFirst)
        {
            return;
        }

        if (_transport is not null)
        {
            await _transport.SubscribeAsync(kind, documentId, cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        await _hub!.InvokeAsync(HubMethods.JoinDocument, kind, documentId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync(
        TempoDocumentKind kind, Guid documentId, CancellationToken cancellationToken = default)
    {
        bool isLast;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_refCounts.TryGetValue((kind, documentId), out var count) || count == 0)
            {
                return;
            }
            count--;
            isLast = count == 0;
            if (isLast)
            {
                _refCounts.Remove((kind, documentId));
            }
            else
            {
                _refCounts[(kind, documentId)] = count;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (!isLast)
        {
            return;
        }

        if (_transport is not null)
        {
            await _transport.UnsubscribeAsync(kind, documentId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_hub!.State == HubConnectionState.Connected)
        {
            await _hub.InvokeAsync(HubMethods.LeaveDocument, kind, documentId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task OnReconnectedAsync(string? connectionId)
    {
        List<(TempoDocumentKind Kind, Guid Id)> documents;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            documents = new List<(TempoDocumentKind Kind, Guid Id)>(_refCounts.Keys);
        }
        finally
        {
            _gate.Release();
        }

        foreach (var (kind, documentId) in documents)
        {
            try
            {
                await _hub!.InvokeAsync(HubMethods.JoinDocument, kind, documentId)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best effort — the next reconnect or resubscribe retries the join.
            }
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                await _hub!.StartAsync(cancellationToken).ConfigureAwait(false);
                _started = true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync().ConfigureAwait(false);
        }
        _gate.Dispose();
    }
}
