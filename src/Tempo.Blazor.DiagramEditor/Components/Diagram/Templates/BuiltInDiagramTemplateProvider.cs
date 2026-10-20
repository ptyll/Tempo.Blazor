using System.Text.Json;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Templates;

/// <summary>
/// Built-in provider that loads diagram templates from embedded resources
/// (compiled from <c>wwwroot/diagram-templates/*.json</c> with explicit
/// <c>LogicalName</c>s — the lookup is by exact name, never a manifest suffix scan).
/// The catalog loads once per provider instance, thread-safely; a faulted cached load is
/// forgotten so the next call retries instead of replaying the exception forever.
/// </summary>
public sealed class BuiltInDiagramTemplateProvider : IDiagramTemplateProvider
{
    private static readonly System.Reflection.Assembly _assembly = typeof(BuiltInDiagramTemplateProvider).Assembly;

    private Lazy<Task<IEnumerable<DiagramTemplateCategory>>> _categories =
        new(LoadCategoriesAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public Task<IEnumerable<DiagramTemplateCategory>> GetTemplateCategoriesAsync()
    {
        var lazy = Volatile.Read(ref _categories);
        try
        {
            return lazy.Value;
        }
        catch
        {
            // A faulted Lazy stays faulted forever: swap in a fresh one (only if the cached
            // value is still the one that faulted) so the next call retries the load.
            Interlocked.CompareExchange(ref _categories,
                new Lazy<Task<IEnumerable<DiagramTemplateCategory>>>(LoadCategoriesAsync, LazyThreadSafetyMode.ExecutionAndPublication),
                lazy);
            throw;
        }
    }

    private static async Task<IEnumerable<DiagramTemplateCategory>> LoadCategoriesAsync()
    {
        var catalogJson = await ReadResourceAsync("diagram-templates/index.json");
        if (string.IsNullOrWhiteSpace(catalogJson))
            return [];

        List<DiagramTemplate> templates;
        try
        {
            using var doc = JsonDocument.Parse(catalogJson);
            if (!doc.RootElement.TryGetProperty("templates", out var templatesEl) || templatesEl.ValueKind != JsonValueKind.Array)
                return [];

            templates = [];
            foreach (var el in templatesEl.EnumerateArray())
            {
                var id = GetString(el, "id");
                var name = GetString(el, "name");
                var category = GetString(el, "category");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;

                var template = new DiagramTemplate
                {
                    Id = id,
                    Name = name,
                    Category = category,
                    Tags = GetStringArray(el, "tags"),
                    ThumbnailUrl = GetString(el, "thumbnailUrl")
                };

                var documentJsonPath = GetString(el, "documentJson");
                if (!string.IsNullOrWhiteSpace(documentJsonPath))
                    template.DocumentJson = await ReadResourceAsync(documentJsonPath.TrimStart('/'));

                // An explicit thumbnailUrl from the catalog wins; the SVG is generated only
                // when the entry ships none (a template without a document gets null — the card
                // renders its category icon placeholder instead).
                if (string.IsNullOrEmpty(template.ThumbnailUrl))
                    template.ThumbnailUrl = DiagramTemplateThumbnails.FromDocumentJson(template.DocumentJson);

                templates.Add(template);
            }
        }
        catch (JsonException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        return templates
            .GroupBy(t => t.Category)
            .Select(g => new DiagramTemplateCategory
            {
                Name = g.Key,
                Templates = g.OrderBy(t => t.Name).ToList()
            })
            .OrderBy(c => c.Name)
            .ToList();
    }

    // Converts "diagram-templates/index.json" into the exact embedded resource name declared by
    // the csproj LogicalName ("Tempo.Blazor.DiagramEditor.diagram-templates.%(Filename)%(Extension)").
    internal static async Task<string> ReadResourceAsync(string relativePath)
    {
        var resourceName = "Tempo.Blazor.DiagramEditor." + relativePath.Replace('\\', '.').Replace('/', '.');
        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return string.Empty;

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string GetString(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? "";
        return "";
    }

    private static string[] GetStringArray(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return [];
        return prop.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}
