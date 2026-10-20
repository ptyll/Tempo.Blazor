using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Rendering;

public class MjmlCompilerTests
{
    private static readonly IMjmlCompiler Compiler = new MjmlNetCompiler();
    private static readonly MjmlGenerator Generator = new();

    [Fact]
    public void Compile_ValidDocument_ProducesHtml_NoErrors()
    {
        var doc = new EmailTemplateDocument { Subject = "Hi" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "Hello world" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var result = Compiler.Compile(Generator.Generate(doc));

        result.Errors.Should().BeEmpty();
        result.Html.Should().Contain("Hello world");
        result.Html.Should().Contain("<html");
    }

    [Fact]
    public void Compile_UnknownElement_ReportsErrorWithoutThrowing()
    {
        const string mjml = "<mjml><mj-body><mj-section><mj-column><mj-bogus>x</mj-bogus></mj-column></mj-section></mj-body></mjml>";

        var result = Compiler.Compile(mjml);

        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(e => e.Message.Contains("mj-bogus"));
    }

    [Fact]
    public void Compile_MalformedMjml_DoesNotThrow_ReportsError()
    {
        // E0.9: truncated MJML makes Mjml.Net throw; the compiler must convert that to an error.
        const string mjml = "<mjml><mj-body><mj-section><mj-column><mj-text>x</mj-text>";

        var act = () => Compiler.Compile(mjml);

        act.Should().NotThrow();
        Compiler.Compile(mjml).Errors.Should().NotBeEmpty();
    }
}
