using System.Text.Json;
using FluentValidation;
using Tempo.Blazor.EmailTemplates.Abstractions.Contracts;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
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
            IEmailTemplateStore store, IServiceProvider services, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            // The server applies the SAME import limits as the editor (Fáze 10, carry-forward): a payload
            // that the editor's import would refuse is refused here too, with a localizable key instead of
            // an unhandled exception from the store.
            if (CheckContent(request.ContentJson, services) is { } invalid) return invalid;

            var created = await store.CreateAsync(request, ct);
            return Results.Created($"/api/email-templates/{created.Id}", created);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateEmailTemplateRequest request, IValidator<UpdateEmailTemplateRequest> validator,
            IEmailTemplateStore store, IServiceProvider services, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            if (CheckContent(request.ContentJson, services) is { } invalid) return invalid;

            return await store.UpdateAsync(id, request, ct) ? Results.NoContent() : Results.NotFound();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IEmailTemplateStore store, CancellationToken ct) =>
            await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // The preview endpoint goes through the SAME seam the editor uses (IEmailPreviewRenderer), so the
        // WASM demo renders on the server exactly like a real host would (contract §3.4).
        group.MapPost("/preview", async (
            RenderPreviewRequest request, IEmailPreviewRenderer renderer, IServiceProvider services,
            CancellationToken ct) =>
        {
            EmailTemplateDocumentOrError parsed = ParseDocument(request.ContentJson, services);
            if (parsed.Problem is not null) return parsed.Problem;

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

        group.MapPost("/validate", (RenderPreviewRequest request, IServiceProvider services) =>
        {
            var parsed = ParseDocument(request.ContentJson, services);
            if (parsed.Problem is not null) return parsed.Problem;

            var messages = new EmailDocumentValidator().Validate(parsed.Document!)
                .Select(m => new { severity = m.Severity.ToString(), key = m.Key, path = m.Path });
            return Results.Ok(messages);
        });

        group.MapPost("/{id:guid}/send", async (
            Guid id, SendEmailRequest request, IValidator<SendEmailRequest> validator,
            IEmailTemplateStore store, IEmailTemplateRenderer renderer, IEmailSender sender,
            IServiceProvider services, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var detail = await store.GetAsync(id, ct);
            if (detail is null) return Results.NotFound();

            var parsed = ParseDocument(detail.ContentJson, services);
            if (parsed.Problem is not null) return parsed.Problem;

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

    /// <summary>
    /// The import limits this API applies: a host-registered <see cref="EmailImportOptions"/> wins,
    /// otherwise the contract defaults (5 MB input, 200 sections, 2000 blocks, depth 8 — the same values the
    /// editor's import dialog enforces).
    /// </summary>
    private static EmailImportOptions OptionsFor(IServiceProvider services)
        => services.GetService(typeof(EmailImportOptions)) as EmailImportOptions ?? new EmailImportOptions();

    /// <summary>Parses the content JSON with the API's import limits, or describes why it cannot.</summary>
    private static EmailTemplateDocumentOrError ParseDocument(string contentJson, IServiceProvider services)
    {
        try
        {
            return new EmailTemplateDocumentOrError
            {
                Document = EmailTemplateMapper.ToDocument(contentJson, OptionsFor(services)),
            };
        }
        catch (EmailTemplateSerializationException ex)
        {
            return new EmailTemplateDocumentOrError { Problem = InvalidContent(ex) };
        }
    }

    /// <summary>Returns a 422 when the content JSON cannot be imported, <see langword="null"/> when it can.</summary>
    private static IResult? CheckContent(string contentJson, IServiceProvider services)
    {
        try
        {
            EmailTemplateMapper.ToDocument(contentJson, OptionsFor(services));
            return null;
        }
        catch (EmailTemplateSerializationException ex)
        {
            return InvalidContent(ex);
        }
    }

    /// <summary>
    /// A 422 whose <c>errors</c> carry the LOCALIZATION KEY of the failure (never a message): the client
    /// owns the wording, so the same response works in every language. A failure without a key is the
    /// generic malformed JSON case.
    /// </summary>
    private static IResult InvalidContent(EmailTemplateSerializationException exception)
        => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [ContentJsonPropertyName] = [exception.Key ?? SerializationKeys.Malformed],
            },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>The request field a content failure is attributed to (matches the DTO's <c>ContentJson</c>).</summary>
    private const string ContentJsonPropertyName = "contentJson";

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
        public IResult? Problem { get; init; }
    }
}
