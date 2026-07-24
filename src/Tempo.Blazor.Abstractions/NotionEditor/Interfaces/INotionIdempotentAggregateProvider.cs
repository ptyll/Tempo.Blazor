using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.NotionEditor.Interfaces;

/// <summary>
/// Extends aggregate persistence with a durable, atomic idempotent execution boundary.
/// </summary>
/// <remarks>
/// This optional contract lets an authoring client commit its complete response receipt in the
/// same transaction as every aggregate write performed by its callback. Providers must scope
/// receipts by their own tenant/application context in addition to
/// <see cref="NotionIdempotentExecutionRequest.OperationScope"/> and
/// <see cref="NotionIdempotentExecutionRequest.Key"/>.
/// </remarks>
public interface INotionIdempotentAggregateProvider : INotionAggregateProvider
{
    /// <summary>
    /// Executes an aggregate operation once and durably stores its opaque response for replay.
    /// </summary>
    /// <param name="request">Stable operation scope, key, canonical request hash, and retention.</param>
    /// <param name="operation">
    /// Library operation to invoke only when no unexpired receipt exists. The provider passed to
    /// the callback must participate in the same transaction as the receipt.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel before the transaction commits.</param>
    /// <returns>
    /// Executed with the newly committed response, replayed with the original response, or collision
    /// when the scoped key already belongs to a different request hash.
    /// </returns>
    /// <remarks>
    /// The provider must not invoke <paramref name="operation"/> for replay or collision outcomes.
    /// If the callback throws or cancellation wins before commit, neither its aggregate writes nor
    /// a receipt may remain. Concurrent calls for the same scoped key must invoke the callback at
    /// most once.
    /// </remarks>
    Task<NotionIdempotentExecutionResult> ExecuteIdempotentAsync(
        NotionIdempotentExecutionRequest request,
        Func<INotionAggregateProvider, CancellationToken, Task<string>> operation,
        CancellationToken cancellationToken = default);
}
