using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Modeling;

/// <summary>
/// Resolves the <see cref="IModelingPropertySchemaProvider"/> the modeling MCP tools read from and
/// validate against. ONE rule for <c>modeling_apply_operations</c> and
/// <c>modeling_get_property_schema</c>, so the schema the agent READS is the schema its batch is
/// judged by ("one semantics").
/// </summary>
internal static class ModelingSchemaProviders
{
    /// <summary>
    /// The provider the caller asked for: the requested key when it names a registered provider, else
    /// the only registered one, else the first registered one (a host that passes a key for a provider
    /// it did not register still gets the validation it asked for) — <c>null</c> only when the host
    /// registered none. A provider with a blank key is ignored: it could never be resolved by key.
    /// </summary>
    /// <param name="providers">The registered providers (DI injects an empty sequence when none).</param>
    /// <param name="providerKey">The optional key the caller requested.</param>
    /// <returns>The resolved provider, or <c>null</c> when no provider is registered.</returns>
    internal static IModelingPropertySchemaProvider? Resolve(
        IEnumerable<IModelingPropertySchemaProvider>? providers,
        string? providerKey)
    {
        if (providers is null)
        {
            return null;
        }

        var registered = providers
            .Where(provider => provider is not null && !string.IsNullOrWhiteSpace(provider.ProviderKey))
            .ToArray();
        if (registered.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            return registered.FirstOrDefault(provider =>
                       string.Equals(provider.ProviderKey, providerKey, StringComparison.Ordinal))
                   ?? registered[0];
        }

        return registered[0];
    }
}
