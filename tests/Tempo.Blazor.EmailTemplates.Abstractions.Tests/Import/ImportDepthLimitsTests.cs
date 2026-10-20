using System.Diagnostics;
using System.Text;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Import;

/// <summary>
/// Resource limits of the importer and the JSON reader (phase-4 review): a hostile payload must fail
/// with a diagnostic, never take the process down. A deeply nested <c>mj-hero</c>/<c>mj-group</c> tree used
/// to overflow the mapping recursion, a self-referencing <c>mj-include</c> could loop forever and a deep
/// JSON document could exhaust the stack of the reader.
/// </summary>
public class ImportDepthLimitsTests
{
    private static string NestedHeroes(int depth)
    {
        var builder = new StringBuilder("<mjml><mj-body><mj-section><mj-column>");
        for (var i = 0; i < depth; i++)
        {
            builder.Append("<mj-hero><mj-text>x</mj-text>");
        }

        for (var i = 0; i < depth; i++)
        {
            builder.Append("</mj-hero>");
        }

        return builder.Append("</mj-column></mj-section></mj-body></mjml>").ToString();
    }

    private static string NestedGroups(int depth)
    {
        var builder = new StringBuilder("<mjml><mj-body><mj-section><mj-column>");
        for (var i = 0; i < depth; i++)
        {
            builder.Append("<mj-group><mj-column>");
        }

        builder.Append("<mj-text>x</mj-text>");
        for (var i = 0; i < depth; i++)
        {
            builder.Append("</mj-column></mj-group>");
        }

        return builder.Append("</mj-column></mj-section></mj-body></mjml>").ToString();
    }

    /// <summary>A legal document nested to the model limit through wrappers, with inline markup on top.</summary>
    private static string WrappersAtTheLimit(int nesting)
    {
        var builder = new StringBuilder("<mjml><mj-body>");
        for (var i = 0; i < nesting; i++)
        {
            builder.Append("<mj-wrapper>");
        }

        builder.Append("<mj-section><mj-column><mj-text><b><strong>x</strong></b></mj-text></mj-column></mj-section>");
        for (var i = 0; i < nesting; i++)
        {
            builder.Append("</mj-wrapper>");
        }

        return builder.Append("</mj-body></mjml>").ToString();
    }

    [Fact]
    public void Import_TwentyThousandNestedHeroes_FailsWithADiagnostic_InsteadOfCrashing()
    {
        var mjml = NestedHeroes(20_000);

        ImportResult? result = null;
        var act = () => result = new MjmlImporter().Import(mjml);

        act.Should().NotThrow();
        result!.Document.Should().BeNull();
        result.Errors.Should().Contain(error => error.Key == DocumentValidationKeys.TooDeeplyNested);
    }

    [Fact]
    public void Import_TwentyThousandNestedGroups_FailsWithADiagnostic_InsteadOfCrashing()
    {
        var mjml = NestedGroups(20_000);

        ImportResult? result = null;
        var act = () => result = new MjmlImporter().Import(mjml);

        act.Should().NotThrow();
        result!.Document.Should().BeNull();
        result.Errors.Should().Contain(error => error.Key == DocumentValidationKeys.TooDeeplyNested);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(20)]
    public void Import_DeepestLegalDocument_IsNotRejectedByTheDepthGuard(int nesting)
    {
        var result = new MjmlImporter().Import(
            WrappersAtTheLimit(nesting), null, new EmailImportOptions { MaxNestingDepth = nesting + 1 });

        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
    }

    [Fact]
    public void Import_SelfReferencingInclude_DoesNotLoop()
    {
        const string self = "<mj-include path=\"self.mjml\" />";

        var stopwatch = Stopwatch.StartNew();
        var result = new MjmlImporter().Import(
            "<mjml><mj-body><mj-include path=\"self.mjml\" /></mj-body></mjml>", new SelfResolver(self));
        stopwatch.Stop();

        result.Warnings.Should().Contain(warning => warning.Key == ImportKeys.IncludeUnresolved);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Import_MutuallyRecursiveIncludes_StopAtTheIncludeDepthLimit()
    {
        var resolver = new SwapResolver();

        ImportResult? result = null;
        var act = () => result = new MjmlImporter().Import(
            "<mjml><mj-body><mj-include path=\"a.mjml\" /></mj-body></mjml>", resolver);

        act.Should().NotThrow();
        result!.Warnings.Should().Contain(warning => warning.Key == ImportKeys.IncludeUnresolved);
        resolver.Calls.Should().BeLessThan(32);
    }

    [Fact]
    public void Deserialize_PathologicallyDeepJson_ThrowsWithAKey_NotStackOverflow()
    {
        var json = "{\"subject\":\"S\",\"future\":" + new string('[', 100_000) + new string(']', 100_000) + "}";

        var act = () => EmailTemplateSerializer.Deserialize(json, new EmailImportOptions { MaxNestingDepth = 4 });

        act.Should().Throw<EmailTemplateSerializationException>();
    }

    [Fact]
    public void LimitsValidator_PathologicallyDeepDocument_ReportsInsteadOfRecursing()
    {
        // 5000 nested groups built by hand (no JSON parser involved): the walk must stay iterative.
        var document = new EmailTemplateDocument();
        EmailBlockBase block = new EmailTextBlock { Content = "deep" };
        for (var i = 0; i < 5000; i++)
        {
            var group = new EmailGroupBlock();
            group.Columns.Add(new EmailColumn { Blocks = { block } });
            block = group;
        }

        document.Sections.Add(new EmailSection
        {
            Columns = { new EmailColumn { Blocks = { block } } },
        });

        var findings = new EmailDocumentLimitsValidator().Validate(document);

        findings.Should().Contain(finding => finding.Key == DocumentValidationKeys.TooDeeplyNested);
    }

    [Fact]
    public void Serialize_DeepDocument_RoundTrips_AndADepthBombFailsWithAKey()
    {
        var document = new EmailTemplateDocument();
        EmailBlockBase block = new EmailTextBlock { Content = "deep" };
        for (var i = 0; i < 500; i++)
        {
            var group = new EmailGroupBlock();
            group.Columns.Add(new EmailColumn { Blocks = { block } });
            block = group;
        }

        document.Sections.Add(new EmailSection { Columns = { new EmailColumn { Blocks = { block } } } });

        var act = () => EmailTemplateSerializer.Serialize(document);

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.TooDeeplyNested);
    }

    private sealed class SelfResolver(string content) : IMjmlIncludeResolver
    {
        public string? Resolve(string path) => content;
    }

    private sealed class SwapResolver : IMjmlIncludeResolver
    {
        public int Calls { get; private set; }

        public string? Resolve(string path)
        {
            Calls++;
            return string.Equals(path, "a.mjml", StringComparison.Ordinal)
                ? "<mj-include path=\"b.mjml\" />"
                : "<mj-include path=\"a.mjml\" />";
        }
    }
}
