using Tempo.Blazor.Components.Diagram.Templates;

namespace Tempo.Blazor.Components.Diagram.Templates;

/// <summary>
/// Central registry of all diagram templates.
/// Registered as a singleton DI service. Thread-safe: <see cref="InitializeAsync"/> runs the
/// catalog load once through a Lazy&lt;Task&gt; (ExecutionAndPublication), concurrent callers all
/// await the same load, and the template dictionary is guarded by a lock.
/// </summary>
public sealed class DiagramTemplateRegistry
{
    private readonly Dictionary<string, (DiagramTemplate Template, int Priority)> _templates = new(StringComparer.Ordinal);
    private readonly List<IDiagramTemplateProvider> _providers = [];
    private readonly object _sync = new();
    private Lazy<Task>? _initialization;

    /// <summary>Registers a provider for lazy async loading.</summary>
    public void RegisterProvider(IDiagramTemplateProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_sync)
        {
            _providers.Add(provider);
        }
    }

    /// <summary>
    /// Initializes the registry by loading all templates from registered providers.
    /// Safe to call multiple times and from multiple threads concurrently — the catalog loads
    /// exactly once and every caller awaits the same task. A faulted load is forgotten again:
    /// the cached task is reset under the lock so the next call retries instead of replaying
    /// the exception forever.
    /// </summary>
    public async Task InitializeAsync()
    {
        Lazy<Task> initialization;
        lock (_sync)
        {
            _initialization ??= new Lazy<Task>(InitializeCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
            initialization = _initialization;
        }

        try
        {
            await initialization.Value.ConfigureAwait(false);
        }
        catch
        {
            // A Lazy<Task> stays faulted forever: reset it (under the lock, only when it is
            // still the one we awaited) so the next InitializeAsync starts a fresh load.
            lock (_sync)
            {
                if (ReferenceEquals(_initialization, initialization))
                    _initialization = null;
            }
            throw;
        }
    }

    private async Task InitializeCoreAsync()
    {
        IDiagramTemplateProvider[] providers;
        lock (_sync)
        {
            providers = _providers.ToArray();
        }

        foreach (var provider in providers.OrderBy(p => p.Priority))
        {
            var categories = await provider.GetTemplateCategoriesAsync();
            foreach (var category in categories)
            {
                foreach (var template in category.Templates)
                    RegisterTemplate(template, provider.Priority);
            }
        }
    }

    /// <summary>
    /// Registers a single template.
    /// If a template with the same id already exists, the one with higher
    /// <paramref name="priority"/> wins (ties keep the existing entry).
    /// </summary>
    public void RegisterTemplate(DiagramTemplate template, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(template);
        lock (_sync)
        {
            if (_templates.TryGetValue(template.Id, out var existing) && existing.Priority >= priority)
                return;
            _templates[template.Id] = (template, priority);
        }
    }

    /// <summary>Returns the template for <paramref name="templateId"/>, or <c>null</c> if not found.</summary>
    public DiagramTemplate? GetTemplate(string templateId)
    {
        lock (_sync)
        {
            _templates.TryGetValue(templateId, out var entry);
            return entry.Template;
        }
    }

    /// <summary>Returns all registered templates ordered by Category then Name.</summary>
    public IEnumerable<DiagramTemplate> GetAll()
    {
        lock (_sync)
        {
            return _templates.Values
                .Select(e => e.Template)
                .OrderBy(t => t.Category)
                .ThenBy(t => t.Name)
                .ToList();
        }
    }

    /// <summary>Returns all distinct category names in display order.</summary>
    public string[] GetCategories()
    {
        lock (_sync)
        {
            return _templates.Values
                .Select(e => e.Template.Category)
                .Distinct()
                .Order()
                .ToArray();
        }
    }

    /// <summary>Returns templates belonging to <paramref name="category"/>.</summary>
    public IEnumerable<DiagramTemplate> GetByCategory(string category)
    {
        lock (_sync)
        {
            return _templates.Values
                .Where(e => string.Equals(e.Template.Category, category, StringComparison.Ordinal))
                .Select(e => e.Template)
                .OrderBy(t => t.Name)
                .ToList();
        }
    }

    /// <summary>Total number of registered templates.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _templates.Count;
            }
        }
    }
}
