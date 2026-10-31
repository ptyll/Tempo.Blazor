using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// PDF signature verification (Signing redesign phase 7, S1-1): the status banner is a
/// <c>TmAlert</c> with a live-region role and a status modifier class, each signature is a card
/// with an avatar and a three-column <c>dl</c>, and the checksum is truncated with a
/// <c>TmCopyButton</c> carrying the full value.
/// </summary>
public class TmPdfSignatureVerificationTests : LocalizationTestBase
{
    private static SigningPdfVerificationResult VerifiedResult() => new()
    {
        Status = SigningPdfVerificationStatus.Verified,
        FileName = "signed.pdf",
        Checksum = "3f2a9c6e7d4b8a1c0e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d",
        Signatures =
        [
            new SigningPdfSignatureInfo
            {
                SignerName = "Alex Johnson",
                SignerEmail = "alex@example.test",
                SignedAt = new DateTimeOffset(2026, 5, 6, 10, 24, 0, TimeSpan.Zero),
                VerificationMethod = "SMS",
                CertificateSubject = "CN=Alex"
            }
        ]
    };

    [Fact]
    public void Verification_Banner_HasRoleStatus_AndStatusClass()
    {
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, VerifiedResult()));

        var banner = cut.Find(".tm-pdf-signature-verification__banner");
        banner.GetAttribute("role").Should().Be("status", "a non-blocking verification result announces politely");
        banner.ClassList.Should().Contain("tm-pdf-signature-verification__banner--verified",
            "the banner carries the status modifier the CSS themes");

        var danger = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, new SigningPdfVerificationResult { Status = SigningPdfVerificationStatus.Error }));
        danger.Find(".tm-pdf-signature-verification__banner").GetAttribute("role").Should().Be("alert",
            "an error result announces assertively");
    }

    [Fact]
    public void Verification_Checksum_HasCopyButton()
    {
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, VerifiedResult()));

        var checksum = cut.Find(".tm-pdf-signature-verification__checksum-value");
        checksum.TextContent.Should().Contain("…", "the checksum is displayed truncated");
        checksum.TextContent.Length.Should().BeLessThan(
            VerifiedResult().Checksum!.Length, "the displayed value is the shortened form");

        var copy = cut.FindComponent<Tempo.Blazor.Components.Buttons.TmCopyButton>();
        copy.Instance.Text.Should().Be(VerifiedResult().Checksum, "the copy action carries the FULL checksum");
        checksum.GetAttribute("title").Should().Be(VerifiedResult().Checksum,
            "the full checksum stays reachable through the title tooltip");
    }

    [Fact]
    public void Verification_SignatureCard_HasAvatar_AndThreeColumnDetails()
    {
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, VerifiedResult()));

        cut.FindComponents<Tempo.Blazor.Components.Avatars.TmAvatar>().Should().HaveCount(1);
        cut.Find(".tm-pdf-signature-verification__signature").GetAttribute("class")!.Should()
            .Contain("tm-pdf-signature-verification__signature--card");
        cut.FindAll(".tm-pdf-signature-verification__signature-details > div").Should().HaveCount(3,
            "signed-at, method and certificate render as the three columns of the signature dl");
    }

    [Fact]
    public void Render_EmptyUploadState()
    {
        var requested = false;
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.OnVerifyRequested, EventCallback.Factory.Create(this, () => requested = true)));

        cut.Markup.Should().Contain("Verify a signed PDF");
        cut.FindAll(".tm-icon-unknown").Should().BeEmpty();
        cut.Find(".tm-pdf-signature-verification__verify").Click();
        requested.Should().BeTrue();
    }

    [Theory]
    [InlineData(SigningPdfVerificationStatus.Loading, "Verifying PDF")]
    [InlineData(SigningPdfVerificationStatus.Verified, "PDF verified")]
    [InlineData(SigningPdfVerificationStatus.ChecksumNotFound, "Checksum not found")]
    [InlineData(SigningPdfVerificationStatus.MalformedPdf, "Malformed PDF")]
    public void Render_StatusStates(SigningPdfVerificationStatus status, string expected)
    {
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, new SigningPdfVerificationResult { Status = status }));

        cut.Markup.Should().Contain(expected);
        cut.FindAll(".tm-icon-unknown").Should().BeEmpty();
    }

    [Fact]
    public void Render_VerifiedDetailsAndSignatures()
    {
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, new SigningPdfVerificationResult
            {
                Status = SigningPdfVerificationStatus.Verified,
                FileName = "signed.pdf",
                Checksum = "sha256-demo",
                Signatures =
                [
                    new SigningPdfSignatureInfo
                    {
                        SignerName = "Alex Johnson",
                        SignerEmail = "alex@example.test",
                        VerificationMethod = "SMS",
                        CertificateSubject = "CN=Alex"
                    }
                ]
            }));

        cut.Markup.Should().Contain("signed.pdf");
        cut.Markup.Should().Contain("sha256-demo");
        cut.Markup.Should().Contain("Alex Johnson");
        cut.Markup.Should().Contain("CN=Alex");
    }

    [Fact]
    public void Render_VerificationDataIsIndependentFromSigningFieldLocalization()
    {
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, new SigningPdfVerificationResult
            {
                Status = SigningPdfVerificationStatus.ChecksumNotFound,
                FileName = "localized-template.pdf",
                Checksum = "sha256-original-value",
                Message = "Stored field label: Podpis"
            }));

        cut.Markup.Should().Contain("localized-template.pdf");
        cut.Markup.Should().Contain("sha256-original-value");
        cut.Markup.Should().Contain("Stored field label: Podpis");
    }

    /// <summary>A signature without a recorded name or email renders the localized "Unknown
    /// signer" — never a blank card header (phase 7 review).</summary>
    [Fact]
    public void SignatureCard_WithoutNameOrEmail_ShowsTheUnknownSignerFallback()
    {
        var cut = Render<TmPdfSignatureVerification>(parameters => parameters
            .Add(p => p.Result, new SigningPdfVerificationResult
            {
                Status = SigningPdfVerificationStatus.Verified,
                Signatures =
                [
                    new SigningPdfSignatureInfo { VerificationMethod = "Certificate" }
                ]
            }));

        cut.Find(".tm-pdf-signature-verification__signature-identity strong").TextContent
            .Should().Be("Unknown signer");
    }
}
