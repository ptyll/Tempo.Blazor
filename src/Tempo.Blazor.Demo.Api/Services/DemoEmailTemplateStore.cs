using Tempo.Blazor.EmailTemplates.Abstractions.Contracts;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.Demo.Api.Services;

/// <summary>
/// In-memory demo implementation of <see cref="IEmailTemplateStore"/>. All mutations take a lock so
/// each operation is atomic (a simple unit of work). Seeded with three sample templates.
/// </summary>
public sealed class DemoEmailTemplateStore : IEmailTemplateStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Stored> _items = new();

    /// <summary>Creates a seeded store.</summary>
    public DemoEmailTemplateStore() => Seed();

    /// <summary>Resets the store back to its seeded templates.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _items.Clear();
            Seed();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailTemplateSummaryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<EmailTemplateSummaryDto> list = _items.Values
                .OrderBy(s => s.Document.Name, StringComparer.OrdinalIgnoreCase)
                .Select(s => EmailTemplateMapper.ToSummaryDto(s.Document, s.IsActive))
                .ToList();
            return Task.FromResult(list);
        }
    }

    /// <inheritdoc />
    public Task<EmailTemplateDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.TryGetValue(id, out var stored)
                ? EmailTemplateMapper.ToDetailDto(stored.Document, stored.IsActive, stored.SampleDataJson)
                : null);
        }
    }

    /// <inheritdoc />
    public Task<EmailTemplateDetailDto> CreateAsync(CreateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var document = EmailTemplateMapper.ApplyCreate(request);
            document.Id = Guid.NewGuid();
            document.UpdatedAt = DateTime.UtcNow;
            _items[document.Id] = new Stored(document, IsActive: true, request.SampleDataJson);
            return Task.FromResult(EmailTemplateMapper.ToDetailDto(document, true, request.SampleDataJson));
        }
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(Guid id, UpdateEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_items.ContainsKey(id)) return Task.FromResult(false);
            var document = EmailTemplateMapper.ApplyUpdate(request);
            document.Id = id;
            _items[id] = new Stored(document, request.IsActive, request.SampleDataJson);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.Remove(id));
        }
    }

    /// <inheritdoc />
    public Task<bool> IsNameAvailableAsync(string name, Guid? excludingId = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var taken = _items.Values.Any(s =>
                s.Document.Id != excludingId &&
                string.Equals(s.Document.Name, name, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(!taken);
        }
    }

    private void Seed()
    {
        Add(SeedWelcome(), "{\"first_name\":\"Jane\"}");
        Add(SeedNewsletter(), "{\"newsletter_title\":\"June News\",\"articles\":[{\"title\":\"Launch\",\"summary\":\"We shipped!\"},{\"title\":\"Tips\",\"summary\":\"Five tips\"}]}");
        Add(SeedOrderConfirmation(), "{\"order_id\":\"A-1001\",\"is_paid\":true,\"customer_name\":\"Jane Doe\"}");
    }

    private void Add(EmailTemplateDocument document, string sampleDataJson)
        => _items[document.Id] = new Stored(document, IsActive: true, sampleDataJson);

    private static EmailTemplateDocument SeedWelcome()
    {
        var doc = new EmailTemplateDocument
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Welcome email",
            Subject = "Welcome {{ first_name }}!",
            Preheader = "Thanks for joining",
            Language = "en",
            UpdatedAt = DateTime.UtcNow,
        };
        var col = new EmailColumn();
        // Fáze 9A: the pipeline's sanitizer (HtmlSanitizerPolicy) keeps only inline formatting — a heading is
        // a TEXT BLOCK with a heading TYPOGRAPHY, not an <h1>. The demo therefore shows what the export will
        // really emit: an <h1> would reach the mj-text as escaped text in the WASM (conservative) pass, and
        // the WYSIWYG canvas would honestly show exactly that.
        col.Blocks.Add(new EmailTextBlock
        {
            Content = "<strong>Welcome, {{ first_name }}!</strong>",
            FontSize = "24px",
            FontWeight = "bold",
            LineHeight = "1.25",
        });
        col.Blocks.Add(new EmailTextBlock { Content = "We're glad to have you on board." });
        col.Blocks.Add(new EmailButtonBlock { Text = "Get started", Href = "https://example.com/start" });
        var section = new EmailSection { BackgroundColor = "#ffffff" };
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    private static EmailTemplateDocument SeedNewsletter()
    {
        var doc = new EmailTemplateDocument
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Newsletter",
            Subject = "{{ newsletter_title }}",
            Preheader = "Your monthly update",
            Language = "en",
            UpdatedAt = DateTime.UtcNow,
        };
        var col = new EmailColumn { Width = "66.666%" };
        col.Blocks.Add(new EmailTextBlock
        {
            Content = "<strong>{{ newsletter_title }}</strong>",
            FontSize = "20px",
            FontWeight = "bold",
            LineHeight = "1.3",
        });
        col.Blocks.Add(new EmailTextBlock
        {
            Content = "{{ for article in articles }}<strong>{{ article.title }}</strong><p>{{ article.summary }}</p>{{ end }}",
        });
        col.Blocks.Add(new EmailImageBlock { Src = "https://example.com/banner.png", Alt = "Newsletter banner" });
        var emptyCol = new EmailColumn { Width = "33.333%" };
        var section = new EmailSection();
        section.Columns.Add(col);
        section.Columns.Add(emptyCol);
        doc.Sections.Add(section);
        return doc;
    }

    private static EmailTemplateDocument SeedOrderConfirmation()
    {
        var doc = new EmailTemplateDocument
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Order confirmation",
            Subject = "Order {{ order_id }} confirmed",
            Preheader = "Your order is on its way",
            Language = "en",
            UpdatedAt = DateTime.UtcNow,
        };
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "<p>Hi {{ customer_name }}, thanks for your order!</p>" });

        // Fáze 9 demo data: a header row, a linked cell with its own background and a plain cell, so the
        // table-cell dialog, the URL allow-list and the bgcolor emission can be reviewed on a real document.
        var table = new EmailTableBlock();
        var header = new EmailTableRow { IsHeader = true };
        header.Cells.Add(new EmailTableCell { Text = "Item" });
        header.Cells.Add(new EmailTableCell { Text = "Price" });
        var row = new EmailTableRow();
        row.Cells.Add(new EmailTableCell
        {
            Text = "Widget",
            Href = "https://example.com/widget",
            BackgroundColor = "#f5f5f5",
        });
        row.Cells.Add(new EmailTableCell { Text = "$9.99" });
        var more = new EmailTableRow();
        more.Cells.Add(new EmailTableCell { Text = "Gadget", Href = "https://example.com/gadget" });
        more.Cells.Add(new EmailTableCell { Text = "$19.99" });
        table.Rows.Add(header);
        table.Rows.Add(row);
        table.Rows.Add(more);
        col.Blocks.Add(table);

        // Fáze 9 demo data: two social elements, one with a built-in network name and one "custom" (which
        // requires an icon URL), so the dialog's validation is reachable from the demo.
        var social = new EmailSocialBlock();
        social.Elements.Add(new EmailSocialElement
        {
            Name = "facebook",
            Href = "https://facebook.com/example",
            Label = "Facebook",
        });
        social.Elements.Add(new EmailSocialElement
        {
            Name = "instagram",
            Href = "https://instagram.com/example",
            Label = "Instagram",
        });
        col.Blocks.Add(social);

        col.Blocks.Add(new EmailTextBlock
        {
            Content = "Thank you for your payment.",
            VisibleWhen = "is_paid",
        });

        var section = new EmailSection();
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    private sealed record Stored(EmailTemplateDocument Document, bool IsActive, string? SampleDataJson);
}
