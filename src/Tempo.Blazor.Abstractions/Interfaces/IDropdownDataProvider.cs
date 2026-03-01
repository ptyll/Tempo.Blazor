using Tempo.Blazor.Models;

namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Server-side data provider for TmFilterableDropdown.
/// Implement this interface to provide paginated, searchable items from any data source.
/// </summary>
/// <typeparam name="TItem">The item type returned by the provider.</typeparam>
/// <example>
/// <code>
/// public class UsersDropdownProvider : IDropdownDataProvider&lt;UserDto&gt;
/// {
///     public async Task&lt;DropdownDataResult&lt;UserDto&gt;&gt; GetItemsAsync(
///         DropdownSearchRequest request, CancellationToken ct = default)
///     {
///         var result = await _api.SearchUsersAsync(request.SearchText, request.Page, request.PageSize, ct);
///         return DropdownDataResult&lt;UserDto&gt;.WithItems(result.Items, result.TotalCount);
///     }
/// }
/// </code>
/// </example>
public interface IDropdownDataProvider<TItem>
{
    /// <summary>
    /// Retrieves items matching the search criteria.
    /// </summary>
    /// <param name="request">Search parameters including text, pagination, and excluded IDs.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DropdownDataResult<TItem>> GetItemsAsync(DropdownSearchRequest request, CancellationToken ct = default);
}
