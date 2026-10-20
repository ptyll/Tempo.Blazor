using System.Text.Json;
using FluentValidation;
using Tempo.Blazor.EmailTemplates.Abstractions.Contracts;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.Demo.Api.Endpoints;

/// <summary>Demo endpoints for email templates: CRUD, preview, validation and send.</summary>
public static class EmailTemplateEndpoints
{
    /// <summary>Maps the email template demo endpoints.</summary>
    public static IEndpointRouteBuilder MapEmailTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/email-templates").WithTags("Email Templates");

        group.MapGet("/", async (IEmailTemplateStore store, CancellationToken ct) =>
            Results.Ok(await store.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, IEmailTemplateStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } detail ? Results.Ok(detail) : Results.NotFound());

        group.MapGet("/name-available", async (string name, Guid? excludingId, IEmailTemplateStore store, CancellationToken ct) =>
            Results.Ok(await store.IsNameAvailableAsync(name, excludingId, ct)));

        group.MapPost("/", async (
            CreateEmailTemplateRequest request, IValidator<CreateEmailTemplateRequest> validator,
            IEmailTemplateStore store, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var created = await store.CreateAsync(request, ct);
            return Results.Created($"/api/email-templates/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateEmailTemplateRequest request, IValidator<UpdateEmailTemplateRequest> validator,
            IEmailTemplateStore store, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            return await store.UpdateAsync(id, request, ct) ? Results.NoContent() : Results.NotFound();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IEmailTemplateStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // The preview endpoint goes through the SAME seam the editor uses (IEmailPreviewRenderer), so the
        // WASM demo renders on the server exactly like a real host would (contract §3.4).
        group.MapPost("/preview", async (
            RenderPreviewRequest request, IEmailPreviewRenderer renderer, CancellationToken ct) =>
        {
            EmailTemplateDocumentOrError parsed = ParseDocument(request.ContentJson);
            if (parsed.Error is not null) return Results.BadRequest(parsed.Error);

            var result = await renderer.RenderAsync(new EmailPreviewRequest
            {
                Document = parsed.Document!,
                VariablesJson = request.VariablesJson,
                SubstituteVariables = request.SubstituteVariables,
                CultureName = request.CultureName,
            }, ct);

            return Results.Ok(new RenderPreviewResponse
            {
                Html = result.Html,
                Text = result.Text,
                Subject = result.Subject,
                Preheader = result.Preheader,
                Errors = result.Errors.Select(ToDto).ToList(),
                Status = result.Status,
            });
        });

        group.MapPost("/validate", (RenderPreviewRequest request) =>
        {
            var parsed = ParseDocument(request.ContentJson);
            if (parsed.Error is not null) return Results.BadRequest(parsed.Error);

            var messages = new EmailDocumentValidator().Validate(parsed.Document!)
                .Select(m => new { severity = m.Severity.ToString(), key = m.Key, path = m.Path });
            return Results.Ok(messages);
        });

        group.MapPost("/{id:guid}/send", async (
            Guid id, SendEmailRequest request, IValidator<SendEmailRequest> validator,
            IEmailTemplateStore store, IEmailTemplateRenderer renderer, IEmailSender sender, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var detail = await store.GetAsync(id, ct);
            if (detail is null) return Results.NotFound();

            var parsed = ParseDocument(detail.ContentJson);
            if (parsed.Error is not null) return Results.BadRequest(parsed.Error);

            var model = ParseVariables(request.VariablesJson);
            var result = await renderer.RenderAsync(parsed.Document!, model, cancellationToken: ct);
            if (!result.Success)
                return Results.UnprocessableEntity(result.Errors.Select(ToDto));

            await sender.SendAsync(new EmailMessage(
                From: null, To: request.To, Cc: request.Cc,
                Subject: result.Subject, Html: result.Html, Text: result.TextVersion), ct);
            return Results.Accepted($"/api/email-templates/{id}");
        });

        return app;
    }

    private static RenderErrorDto ToDto(RenderError error)
        => new(error.Message, error.Line, error.Column, error.Key, error.Severity);

    private static EmailTemplateDocumentOrError ParseDocument(string contentJson)
    {
        try
        {
            return new EmailTemplateDocumentOrError { Document = EmailTemplateSerializer.Deserialize(contentJson) };
        }
        catch (EmailTemplateSerializationException ex)
        {
            return new EmailTemplateDocumentOrError { Error = ex.Message };
        }
    }

    private static Dictionary<string, object?>? ParseVariables(string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(variablesJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class EmailTemplateDocumentOrError
    {
        public Tempo.Blazor.EmailTemplates.Abstractions.Model.EmailTemplateDocument? Document { get; init; }
        public string? Error { get; init; }
    }
}
