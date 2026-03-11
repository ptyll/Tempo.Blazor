using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JsonDocumentationGenerator;

/// <summary>
/// Reads component .razor source files, extracts [Parameter] properties,
/// and enriches JSON documentation with parameters, kind, and category.
/// </summary>
static class ParameterEnricher
{
    // Maps source subfolder names to user-facing category labels
    static readonly Dictionary<string, string> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Activity"] = "Activity",
        ["Avatars"] = "Avatars",
        ["Buttons"] = "Buttons",
        ["Charts"] = "Charts",
        ["Dashboard"] = "Dashboard",
        ["DataDisplay"] = "Data Display",
        ["DataTable"] = "Data Table",
        ["Dropdowns"] = "Dropdowns",
        ["Feedback"] = "Feedback",
        ["Files"] = "Files",
        ["Filters"] = "Filters",
        ["Forms"] = "Forms",
        ["Gallery"] = "Gallery",
        ["Icons"] = "Icons",
        ["ImportExport"] = "Import / Export",
        ["Inputs"] = "Inputs",
        ["Layout"] = "Layout",
        ["Navigation"] = "Navigation",
        ["Notifications"] = "Notifications",
        ["Pickers"] = "Pickers",
        ["RichTextEditor"] = "Rich Text Editor",
        ["Scheduler"] = "Scheduler",
        ["Tags"] = "Tags",
        ["Timeline"] = "Timeline",
        ["Toolbar"] = "Toolbar",
        ["TreeView"] = "Tree View",
        ["Workflow"] = "Workflow",
    };

    // Regex for: /// <summary>...</summary>  (single or multi-line)
    static readonly Regex SummaryRegex = new(
        @"///\s*<summary>(.*?)</summary>",
        RegexOptions.Singleline);

    // Regex for [Parameter] (optionally with CaptureUnmatchedValues)
    // followed by: public TYPE NAME { get; set; } = DEFAULT;
    static readonly Regex ParamRegex = new(
        @"\[Parameter(?:\(CaptureUnmatchedValues\s*=\s*true\))?\]\s*public\s+(.+?)\s+(\w+)\s*\{\s*get;\s*set;\s*\}\s*(?:=\s*(.+?)\s*;)?",
        RegexOptions.Compiled);

    record ParamInfo(string Name, string Type, bool IsRequired, string? Default, string? Description);

    public static int Run(string srcComponentsDir, string jsonComponentsDir)
    {
        // Build a map: componentName -> (razorFilePath, category)
        var componentMap = new Dictionary<string, (string FilePath, string Category)>(StringComparer.OrdinalIgnoreCase);
        foreach (var subDir in Directory.GetDirectories(srcComponentsDir))
        {
            var categoryFolder = Path.GetFileName(subDir);
            foreach (var razorFile in Directory.GetFiles(subDir, "*.razor"))
            {
                var name = Path.GetFileNameWithoutExtension(razorFile);
                componentMap[name] = (razorFile, categoryFolder);
            }
        }

        var jsonFiles = Directory.GetFiles(jsonComponentsDir, "*.json");
        int updated = 0;

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        foreach (var jsonFile in jsonFiles)
        {
            var componentName = Path.GetFileNameWithoutExtension(jsonFile);
            if (!componentMap.TryGetValue(componentName, out var info))
            {
                Console.WriteLine($"  SKIP {componentName} (no matching .razor file)");
                continue;
            }

            // Read all source code (combine .razor + .razor.cs if exists)
            var sourceCode = File.ReadAllText(info.FilePath);
            var codeBehind = info.FilePath + ".cs";
            if (File.Exists(codeBehind))
                sourceCode += "\n" + File.ReadAllText(codeBehind);

            // Extract parameters
            var parameters = ExtractParameters(sourceCode);

            // Read and update JSON
            var jsonText = File.ReadAllText(jsonFile);
            var root = JsonNode.Parse(jsonText)!.AsObject();

            // Add kind
            root["kind"] = "Component";

            // Add category
            var category = CategoryMap.TryGetValue(info.Category, out var label) ? label : info.Category;
            root["category"] = category;

            // Add/replace parameters (but only if we found some, or fix existing ones)
            if (parameters.Count > 0)
            {
                var paramsArray = new JsonArray();
                foreach (var p in parameters)
                {
                    var paramObj = new JsonObject
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["isRequired"] = p.IsRequired
                    };
                    if (p.Default != null)
                        paramObj["default"] = p.Default;
                    if (p.Description != null)
                        paramObj["description"] = p.Description;
                    paramsArray.Add(paramObj);
                }
                root["parameters"] = paramsArray;
            }
            else if (root.ContainsKey("parameters"))
            {
                // Fix existing parameters: rename "required" -> "isRequired"
                var existingParams = root["parameters"]!.AsArray();
                foreach (var param in existingParams)
                {
                    if (param is JsonObject obj && obj.ContainsKey("required"))
                    {
                        var val = obj["required"]!.GetValue<bool>();
                        obj.Remove("required");
                        obj["isRequired"] = val;
                    }
                }
            }

            // Write updated JSON - reorder keys so kind and category come first
            var orderedRoot = ReorderKeys(root);
            File.WriteAllText(jsonFile, orderedRoot.ToJsonString(jsonOptions));
            Console.WriteLine($"  OK {componentName} ({parameters.Count} params, category={category})");
            updated++;
        }

        return updated;
    }

    static List<ParamInfo> ExtractParameters(string source)
    {
        var results = new List<ParamInfo>();

        // Split lines to find summary comments above [Parameter]
        var lines = source.Split('\n');
        var summaries = new Dictionary<int, string>(); // lineIndex -> summary text

        // First pass: collect summaries
        for (int i = 0; i < lines.Length; i++)
        {
            var match = SummaryRegex.Match(lines[i]);
            if (match.Success)
            {
                summaries[i] = match.Groups[1].Value.Trim();
            }
        }

        // Second pass: find [Parameter] lines and extract info
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("[Parameter"))
                continue;

            bool isCaptureUnmatched = line.Contains("CaptureUnmatchedValues");

            // The property declaration may be on the same line or next line
            string combined = line;
            if (i + 1 < lines.Length)
                combined += " " + lines[i + 1].Trim();

            var paramMatch = ParamRegex.Match(combined);
            if (!paramMatch.Success)
                continue;

            var type = paramMatch.Groups[1].Value.Trim();
            var name = paramMatch.Groups[2].Value.Trim();
            var defaultVal = paramMatch.Groups[3].Success ? paramMatch.Groups[3].Value.Trim() : null;

            // Skip CaptureUnmatchedValues, ChildContent, and private-like params
            if (isCaptureUnmatched)
                continue;
            if (name == "ChildContent" || name == "AdditionalAttributes")
                continue;

            // Clean up type
            type = CleanType(type);

            // Clean up default
            defaultVal = CleanDefault(defaultVal, type);

            // Determine isRequired: non-nullable reference types without default
            bool isRequired = !type.EndsWith("?") && defaultVal == null
                              && !type.StartsWith("EventCallback")
                              && !type.StartsWith("RenderFragment")
                              && !type.StartsWith("bool")
                              && !type.StartsWith("int")
                              && !type.StartsWith("double")
                              && !type.StartsWith("float")
                              && !type.StartsWith("decimal")
                              && !type.StartsWith("long");

            // Find summary (look at i-1, i-2 etc. for the nearest summary above)
            string? description = null;
            for (int j = i - 1; j >= Math.Max(0, i - 3); j--)
            {
                if (summaries.TryGetValue(j, out var desc))
                {
                    description = desc;
                    break;
                }
            }

            results.Add(new ParamInfo(name, type, isRequired, defaultVal, description));
        }

        return results;
    }

    static string CleanType(string type)
    {
        // Remove nullable marker for display but remember it
        // E.g. "Func<TItem, string>?" -> "Func<TItem, string>?"
        // Just simplify common patterns
        type = type.Replace("IReadOnlyList", "IReadOnlyList");

        // Remove generic TItem for simpler display
        // Keep as-is for now, the JSON consumer can handle it
        return type;
    }

    static string? CleanDefault(string? defaultVal, string type)
    {
        if (defaultVal == null) return null;

        // Remove trailing semicolons, new() patterns etc.
        defaultVal = defaultVal.TrimEnd(';').Trim();

        // Simplify enum defaults: ButtonVariant.Primary -> "Primary"
        if (defaultVal.Contains('.'))
        {
            var parts = defaultVal.Split('.');
            if (parts.Length == 2)
                return parts[1];
        }

        // new List<>() or new() -> null (no meaningful default to show)
        if (defaultVal.StartsWith("new ") || defaultVal == "new()")
            return null;

        return defaultVal;
    }

    static JsonObject ReorderKeys(JsonObject original)
    {
        var ordered = new JsonObject();
        // Desired key order
        string[] firstKeys = ["itemName", "kind", "category", "description", "requiredImports", "parameters"];

        foreach (var key in firstKeys)
        {
            if (original.ContainsKey(key))
            {
                ordered[key] = original[key]?.DeepClone();
            }
        }

        // Add remaining keys
        foreach (var kvp in original)
        {
            if (!ordered.ContainsKey(kvp.Key))
            {
                ordered[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        return ordered;
    }
}
