using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Audit trail viewer (Signing redesign phase 7, S1-1): Documents/Signers/Events render as real
/// <c>&lt;table&gt;</c> elements with <c>th scope="col"</c>, empty values render an en dash, the
/// network evidence splits into two lines (IP, then agent + zone), signers get a
/// <see cref="Tempo.Blazor.Components.Avatars.TmAvatar"/>, and the document title/status is a
/// host-owned <c>HeaderContent</c> slot (Q-S10 — the model stays unchanged).
/// </summary>
public class TmAuditTrailViewerTests : LocalizationTestBase
{
    [Fact]
    public void Audit_RendersThreeTables_WithThScopeCol()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters.Add(p => p.Trail, CreateTrail()));

        cut.FindAll("table").Should().HaveCount(3, "Documents, Signers and Events each render a table");
        cut.FindAll("table th[scope='col']").Should().NotBeEmpty("every header cell names its column");
        cut.FindAll("table th[scope='col']").Select(th => th.TextContent.Trim())
            .Should().Contain(new[] { "File name", "Checksum", "Identity", "Network", "Verification method", "Event", "Date and time", "Actor / description" });
    }

    [Fact]
    public void Audit_EmptyValues_RenderDash()
    {
        var trail = CreateTrail();
        var cut = Render<TmAuditTrailViewer>(parameters => parameters.Add(p => p.Trail, trail));

        // The event carries no verification method: the cell shows the dash, not blank space.
        var eventRow = cut.FindAll("tbody tr").Last();
        eventRow.TextContent.Should().Contain("–");
    }

    [Fact]
    public void Audit_Network_SplitIntoTwoLines()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters.Add(p => p.Trail, CreateTrail()));

        var primary = cut.FindAll(".tm-audit-trail-viewer__network-primary");
        primary.Should().NotBeEmpty("the IP address is the primary network line");
        primary.First().TextContent.Should().Contain("203.0.113.10");

        var secondary = cut.FindAll(".tm-audit-trail-viewer__network-secondary");
        secondary.Should().NotBeEmpty("the user agent and time zone are the secondary line");
        secondary.First().TextContent.Should().Contain("Mozilla");
        secondary.First().TextContent.Should().Contain("Europe/Prague");
    }

    [Fact]
    public void Audit_Signers_UseTmAvatar()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters.Add(p => p.Trail, CreateTrail()));

        cut.FindComponents<Tempo.Blazor.Components.Avatars.TmAvatar>().Should().NotBeEmpty("a signer row identifies its person through an avatar");
        cut.FindComponents<Tempo.Blazor.Components.Avatars.TmAvatar>()
            .First().Instance.Name.Should().Be("Alex Johnson");
    }

    [Fact]
    public void Audit_HeaderContentSlot()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters
            .Add(p => p.Trail, CreateTrail())
            .Add(p => p.HeaderContent, "<div class=\"audit-host-header\">Mutual NDA · signed</div>"));

        cut.Find(".audit-host-header").TextContent.Should().Contain("Mutual NDA · signed");
        // With a host header the default title is not duplicated.
        cut.Markup.Should().NotContain("Audit trail</h2>");
    }

    /// <summary>The header only renders when there is something to head (phase 7 review): a
    /// Trail-less, header-less viewer shows just the empty state.</summary>
    [Fact]
    public void Audit_NoTrailAndNoHeaderContent_RendersNoHeader()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters
            .Add(p => p.Trail, (SigningAuditTrail?)null));

        cut.FindAll(".tm-audit-trail-viewer__header").Should().BeEmpty();
        cut.Markup.Should().NotContain("Audit trail</h2>");
        cut.Find(".tm-audit-trail-viewer__empty").Should().NotBeNull();
    }

    /// <summary>The tables carry explicit ARIA roles (phase 7 review): the narrow-panel CSS
    /// collapses them to display:block cards, which would otherwise drop the table semantics.</summary>
    [Fact]
    public void Audit_Tables_CarryExplicitAriaRoles()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters.Add(p => p.Trail, CreateTrail()));

        cut.FindAll("table[role='table']").Should().HaveCount(3);
        cut.FindAll("thead[role='rowgroup'], tbody[role='rowgroup']").Should().HaveCount(6);
        cut.FindAll("tr[role='row']").Should().NotBeEmpty();
        cut.FindAll("th[role='columnheader']").Should().NotBeEmpty();
        cut.FindAll("td[role='cell']").Should().NotBeEmpty();
    }

    /// <summary>The avatar identifies the person even without a full name (phase 7 review).</summary>
    [Fact]
    public void Audit_SignerAvatar_FallsBackToEmailWhenFullNameMissing()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters.Add(p => p.Trail, new SigningAuditTrail
        {
            Signers =
            [
                new SigningAuditTrailSigner
                {
                    Email = "alex@example.test",
                    IpAddress = "203.0.113.10"
                }
            ]
        }));

        cut.FindComponents<Tempo.Blazor.Components.Avatars.TmAvatar>()
            .First().Instance.Name.Should().Be("alex@example.test");
    }

    [Fact]
    public void Render_DocumentsSignersNetworkVerificationAndAuditPdf()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters
            .Add(p => p.Trail, CreateTrail()));

        cut.Markup.Should().Contain("contract.pdf");
        cut.Markup.Should().Contain("sha256-contract");
        cut.Markup.Should().Contain("Alex Johnson");
        cut.Markup.Should().Contain("alex@example.test");
        cut.Markup.Should().Contain("203.0.113.10");
        cut.Markup.Should().Contain("Europe/Prague");
        cut.Markup.Should().Contain("SMS");
        cut.Find(".tm-audit-trail-viewer__audit-pdf").GetAttribute("href").Should().Be("/audit.pdf");
    }

    [Fact]
    public void Render_AuditEvents()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters
            .Add(p => p.Trail, CreateTrail()));

        cut.Markup.Should().Contain("Document opened");
        cut.Markup.Should().Contain("Mozilla");
    }

    [Fact]
    public void Render_LocalizationSnapshotShowsCultureFallbackAndResolvedLabels()
    {
        var cut = Render<TmAuditTrailViewer>(parameters => parameters
            .Add(p => p.Trail, CreateTrail())
            .Add(p => p.LocalizationSnapshot, CreateLocalizationSnapshot()));

        cut.Markup.Should().Contain("Signing culture");
        cut.Markup.Should().Contain("en-US");
        cut.Markup.Should().Contain("cs-CZ");
        cut.Markup.Should().Contain("Original PDF language");
        cut.Markup.Should().Contain("Full name");
        cut.Markup.Should().Contain("Email");
        cut.Find("[data-field-uuid='recipient-name']").TextContent.Should().Contain("Full name");
    }

    private static SigningAuditTrail CreateTrail()
    {
        return new SigningAuditTrail
        {
            AuditPdfUrl = "/audit.pdf",
            Documents =
            [
                new SigningAuditTrailDocument
                {
                    FileName = "contract.pdf",
                    Checksum = "sha256-contract"
                }
            ],
            Signers =
            [
                new SigningAuditTrailSigner
                {
                    FullName = "Alex Johnson",
                    Email = "alex@example.test",
                    IpAddress = "203.0.113.10",
                    UserAgent = "Mozilla",
                    TimeZone = "Europe/Prague",
                    VerificationMethod = "SMS"
                }
            ],
            Events =
            [
                new SigningAuditTrailEvent
                {
                    Label = "Document opened",
                    Actor = "Alex Johnson",
                    IpAddress = "203.0.113.10",
                    UserAgent = "Mozilla",
                    TimeZone = "Europe/Prague",
                    VerificationMethod = "SMS"
                },
                new SigningAuditTrailEvent
                {
                    Label = "Document downloaded",
                    Actor = "Alex Johnson"
                }
            ]
        };
    }

    private static SigningSubmissionLocalizationSnapshot CreateLocalizationSnapshot()
    {
        return new SigningSubmissionLocalizationSnapshot
        {
            Culture = "en-US",
            FallbackCulture = "cs-CZ",
            PdfContentTranslated = false,
            Fields =
            [
                new SigningSubmissionFieldLocalizationSnapshot
                {
                    FieldUuid = "recipient-name",
                    Label = "Full name",
                    Title = "Recipient full name",
                    Options =
                    [
                        new SigningSubmissionOptionLocalizationSnapshot
                        {
                            OptionUuid = "delivery-email",
                            Value = "email",
                            Label = "Email"
                        }
                    ]
                }
            ]
        };
    }
}
