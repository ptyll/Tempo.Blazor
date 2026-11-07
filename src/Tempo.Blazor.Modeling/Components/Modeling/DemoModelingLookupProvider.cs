using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Demo lookup provider (phase 5, decision <c>MODELING-PROPERTY-SCHEMA</c> §5.3): the fictional
/// process-role and system catalogue the step-attribute schema searches. It is a real
/// implementation of <see cref="IModelingLookupProvider"/> — paging
/// (<see cref="ModelingLookupRequest.Skip"/> / <see cref="ModelingLookupRequest.Take"/>), the
/// <c>HasMore</c>/<c>TotalCount</c> contract and accent-insensitive substring matching — so a host
/// writer can copy it as the reference shape.
/// <para>
/// It also owns <see cref="ContainsReference"/>, which the demo writer's save-time rejection uses:
/// a stored reference the catalogue no longer knows is exactly the "the host may reject a reference"
/// case of §5.4.
/// </para>
/// </summary>
public sealed class DemoModelingLookupProvider : IModelingLookupProvider
{
    private static readonly ModelingLookupItemDto[] Roles =
    [
        new() { RefType = "role", RefId = "role-sales", Display = "Sales", Description = "Owns the customer order", SecondaryText = "Sales • CZ" },
        new() { RefType = "role", RefId = "role-risk", Display = "Risk owner", Description = "Owns the risk register", SecondaryText = "Risk • CZ" },
        new() { RefType = "role", RefId = "role-warehouse", Display = "Warehouse", Description = "Packs and ships orders", SecondaryText = "Logistics • CZ" },
        new() { RefType = "role", RefId = "role-finance", Display = "Finance", Description = "Approves discounts", SecondaryText = "Finance • CZ" }
    ];

    private static readonly ModelingLookupItemDto[] Systems =
    [
        new() { RefType = "system", RefId = "sys-erp", Display = "ERP", Description = "Enterprise resource planning", SecondaryText = "Core" },
        new() { RefType = "system", RefId = "sys-crm", Display = "CRM", Description = "Customer relationship management", SecondaryText = "Core" },
        new() { RefType = "system", RefId = "sys-wms", Display = "WMS", Description = "Warehouse management", SecondaryText = "Logistics" },
        new() { RefType = "system", RefId = "sys-pay", Display = "Payment gateway", Description = "Card and bank payments", SecondaryText = "Payments" }
    ];

    /// <summary>The provider key of the demo model provider (one lookup provider per model provider).</summary>
    public string ProviderKey => DemoModelingModelProvider.ProviderKeyValue;

    /// <inheritdoc />
    public Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var catalogue = request.LookupType switch
        {
            "role" => Roles,
            "system" => Systems,
            _ => []
        };

        var search = request.SearchText?.Trim() ?? string.Empty;
        var matches = catalogue
            .Where(item => string.IsNullOrEmpty(search)
                           || Contains(item.Display, search)
                           || Contains(item.Description ?? string.Empty, search)
                           || Contains(item.RefId, search))
            .ToArray();

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 20 : request.Take;
        var page = matches.Skip(skip).Take(take).ToArray();

        return Task.FromResult(new ModelingLookupResultDto
        {
            Items = [.. page],
            TotalCount = matches.Length,
            HasMore = skip + page.Length < matches.Length,
            ContinuationToken = skip + page.Length < matches.Length
                ? (skip + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null
        });
    }

    /// <summary>Whether the demo catalogue still knows a stored typed reference (§5.4: the host may
    /// reject one at save time).</summary>
    public static bool ContainsReference(string refType, string refId)
        => Find(refType, refId) is not null;

    /// <summary>
    /// Phase 5 review (A1): re-resolves the DISPLAY LABELS of stored references. An id the catalogue
    /// no longer knows is simply absent from <see cref="ModelingLookupResultDto.Items"/> — the
    /// inspector renders the stored snapshot plus its localized "reference may be stale" hint and
    /// still lets the save through.
    /// </summary>
    public Task<ModelingLookupResultDto?> ResolveAsync(ModelingLookupResolveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<ModelingLookupItemDto>();
        foreach (var reference in request.References)
        {
            if (Find(reference.RefType, reference.RefId) is { } known)
            {
                items.Add(known);
            }
        }

        return Task.FromResult<ModelingLookupResultDto?>(new ModelingLookupResultDto
        {
            Items = items,
            TotalCount = items.Count
        });
    }

    /// <summary>The catalogue entry of one typed reference, or <c>null</c> when it is unknown.</summary>
    private static ModelingLookupItemDto? Find(string refType, string refId)
        => Catalogue(refType).FirstOrDefault(item => string.Equals(item.RefId, refId, StringComparison.Ordinal));

    private static ModelingLookupItemDto[] Catalogue(string refType) => refType switch
    {
        "role" => Roles,
        "system" => Systems,
        _ => []
    };

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
