using System.Text;
using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class ImportExportEndpoints
{
    public static void MapImportExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/import-export").WithTags("Import/Export");

        // POST /api/import-export/export
        group.MapPost("/export", (ExportRequest request, MockPersonStore personStore) =>
        {
            // Simulate export generation
            var records = new List<Dictionary<string, object>>();

            foreach (var entityType in request.SelectedEntityTypes)
            {
                switch (entityType.ToLower())
                {
                    case "users":
                    case "persons":
                        records.AddRange(personStore.Persons.Take(10).Select(p => new Dictionary<string, object>
                        {
                            ["Id"] = p.Id,
                            ["FirstName"] = p.FirstName,
                            ["LastName"] = p.LastName,
                            ["Email"] = p.Email,
                            ["Department"] = p.Department,
                            ["Role"] = p.Role,
                            ["IsActive"] = p.IsActive
                        }));
                        break;
                }
            }

            var result = new ExportResult
            {
                Format = request.Format,
                RecordCount = records.Count,
                EntityTypes = request.SelectedEntityTypes,
                GeneratedAt = DateTime.UtcNow,
                DownloadUrl = $"/api/import-export/download/{Guid.NewGuid()}"
            };

            return Results.Ok(result);
        });

        // POST /api/import-export/validate
        group.MapPost("/validate", (ValidateImportRequest request) =>
        {
            // Simulate validation
            var result = new ImportValidationResult
            {
                TotalRows = request.Rows?.Count ?? 0,
                ValidRows = Math.Max(0, (request.Rows?.Count ?? 0) - 3),
                WarningCount = 3,
                ErrorCount = 0,
                Messages = new List<ImportValidationMessage>
                {
                    new("Row 5", "Warning", "Email format may be invalid"),
                    new("Row 12", "Warning", "Department name normalized"),
                    new("Row 18", "Warning", "Duplicate entry detected"),
                }
            };

            return Results.Ok(result);
        });

        // POST /api/import-export/import
        group.MapPost("/import", (ImportRequest request) =>
        {
            // Simulate import
            var result = new ImportResult
            {
                Success = true,
                ImportedCount = request.Rows?.Count ?? 0,
                FailedCount = 0,
                ImportId = Guid.NewGuid().ToString(),
                CompletedAt = DateTime.UtcNow
            };

            return Results.Ok(result);
        });

        // GET /api/import-export/templates/{format}
        group.MapGet("/templates/{format}", (string format) =>
        {
            var headers = "Id,FirstName,LastName,Email,Department,Role,IsActive";
            var sample = "1,John,Doe,john@example.com,Engineering,Developer,true";
            
            var content = format.ToLower() switch
            {
                "csv" => $"{headers}\n{sample}",
                "json" => "[{\"Id\":1,\"FirstName\":\"John\",\"LastName\":\"Doe\",\"Email\":\"john@example.com\",\"Department\":\"Engineering\",\"Role\":\"Developer\",\"IsActive\":true}]",
                _ => headers
            };

            var bytes = Encoding.UTF8.GetBytes(content);
            var mime = format.ToLower() switch
            {
                "csv" => "text/csv",
                "json" => "application/json",
                "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "text/plain"
            };

            return Results.File(bytes, mime, $"template.{format}");
        });
    }
}

public record ExportRequest(string Format, IReadOnlyList<string> SelectedEntityTypes);

public record ExportResult
{
    public string Format { get; set; } = default!;
    public int RecordCount { get; set; }
    public IReadOnlyList<string> EntityTypes { get; set; } = default!;
    public DateTime GeneratedAt { get; set; }
    public string DownloadUrl { get; set; } = default!;
}

public record ValidateImportRequest(List<Dictionary<string, object>>? Rows);

public record ImportValidationResult
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public List<ImportValidationMessage> Messages { get; set; } = new();
}

public record ImportValidationMessage(string Location, string Severity, string Message);

public record ImportRequest(List<Dictionary<string, object>>? Rows);

public record ImportResult
{
    public bool Success { get; set; }
    public int ImportedCount { get; set; }
    public int FailedCount { get; set; }
    public string ImportId { get; set; } = default!;
    public DateTime CompletedAt { get; set; }
}
