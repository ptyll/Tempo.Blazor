using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Import;

public class MjmlImportFidelityTests
{
    private static readonly MjmlImporter Importer = new();
    private static readonly MjmlGenerator Generator = new();
    private static readonly IMjmlCompiler Compiler = new MjmlNetCompiler();

    // A realistic external template (mjml.io-style): head fonts/styles/attributes + multi-column body.
    private const string ExternalTemplate = """
    <mjml>
      <mj-head>
        <mj-title>Welcome</mj-title>
        <mj-font name="Raleway" href="https://fonts.googleapis.com/css?family=Raleway" />
        <mj-attributes>
          <mj-all font-family="Raleway, Arial"></mj-all>
          <mj-text font-size="14px" color="#555"></mj-text>
        </mj-attributes>
        <mj-style>.link { color: #1188ff; }</mj-style>
      </mj-head>
      <mj-body background-color="#f4f4f4">
        <mj-section background-color="#ffffff" padding="20px">
          <mj-column>
            <mj-image src="https://example.com/logo.png" alt="Brand" width="120px" />
            <mj-text font-size="20px" font-weight="bold">Welcome aboard!</mj-text>
            <mj-text>Thanks for joining. Click below to get started.</mj-text>
            <mj-button href="https://example.com/start" background-color="#1188ff">Get started</mj-button>
          </mj-column>
        </mj-section>
        <mj-section>
          <mj-column width="50%"><mj-text>Left column</mj-text></mj-column>
          <mj-column width="50%"><mj-text>Right column</mj-text></mj-column>
        </mj-section>
        <mj-section>
          <mj-column>
            <mj-divider border-color="#dddddd" />
            <mj-social mode="horizontal">
              <mj-social-element name="twitter" href="https://twitter.com/x">Twitter</mj-social-element>
              <mj-social-element name="facebook" href="https://fb.com/x">Facebook</mj-social-element>
            </mj-social>
          </mj-column>
        </mj-section>
      </mj-body>
    </mjml>
    """;

    [Fact]
    public void ExternalTemplate_ImportsWithoutErrors()
    {
        var result = Importer.Import(ExternalTemplate);

        result.Errors.Should().BeEmpty();
        result.Document!.Subject.Should().Be("Welcome");
        result.Document.Sections.Should().HaveCount(3);
        result.Document.Styles.Attributes.PerTag.Should().ContainKey("mj-text");
    }

    [Fact]
    public void ExternalTemplate_ImportExportRender_PreservesKeyContent()
    {
        // Fidelity: import → re-export → compile must yield HTML carrying the original content.
        var doc = Importer.Import(ExternalTemplate).Document!;
        var reexported = Generator.Generate(doc);
        var compiled = Compiler.Compile(reexported);

        compiled.Errors.Should().BeEmpty();
        compiled.Html.Should().Contain("Welcome aboard!");
        compiled.Html.Should().Contain("Get started");
        compiled.Html.Should().Contain("Left column").And.Contain("Right column");
        compiled.Html.Should().Contain("https://example.com/start");
    }

    [Fact]
    public void ExternalTemplate_ReexportIsIdempotent()
    {
        var doc = Importer.Import(ExternalTemplate).Document!;
        var first = Generator.Generate(doc);
        var second = Generator.Generate(Importer.Import(first).Document!);

        Normalize(second).Should().Be(Normalize(first));
    }

    private static string Normalize(string mjml)
        => string.Join('\n', mjml.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
}
