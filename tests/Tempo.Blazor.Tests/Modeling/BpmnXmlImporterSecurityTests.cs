using FluentAssertions;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Mandatory security tests for the headless BPMN importer (plan 6fca9139 phase 2):
/// XXE and billion-laughs payloads must fail closed (DTD prohibited, no resolver),
/// input size / depth / element-count limits must be enforced on streams that are not
/// seekable, and malformed XML must produce an error carrying the line number.
/// </summary>
public sealed class BpmnXmlImporterSecurityTests
{
    private static BpmnImportResult ImportHostile(string fileName, BpmnImportOptions? options = null)
        => new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadHostileFixture(fileName), options);

    [Fact]
    public void Xxe_payload_is_rejected_without_resolving_the_external_entity()
    {
        var result = ImportHostile("xxe.bpmn");

        result.HasErrors.Should().BeTrue();
        result.Models.Should().BeEmpty();
        result.ErrorMessage.Should().Contain("DTD");
    }

    [Fact]
    public void Billion_laughs_payload_is_rejected_at_the_dtd()
    {
        var result = ImportHostile("billion-laughs.bpmn");

        result.HasErrors.Should().BeTrue();
        result.Models.Should().BeEmpty();
        result.ErrorMessage.Should().Contain("DTD");
    }

    [Fact]
    public void Malformed_xml_reports_an_error_with_the_line_number()
    {
        var result = ImportHostile("malformed.bpmn");

        result.HasErrors.Should().BeTrue();
        result.ErrorMessage.Should().Match("*line 10*");
    }

    [Fact]
    public void Document_without_definitions_root_is_rejected()
    {
        var result = ImportHostile("wrong-root.bpmn");

        result.HasErrors.Should().BeTrue();
        result.ErrorMessage.Should().Contain("'notBpmn'");
        result.ErrorMessage.Should().Contain("definitions");
    }

    [Fact]
    public void Oversized_stream_is_aborted_by_the_bounded_reader()
    {
        // The fixture is ~3.7 KB; cap the input at 1 KB. Streams passed by HTTP callers are not
        // seekable, so the limit must hold for plain forward-only streams too.
        using var stream = new NonSeekableMemoryStream(
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
                BpmnCorpusPaths.ReadHostileFixture("oversized.bpmn")));

        var result = new BpmnXmlImporter().Import(stream, new BpmnImportOptions { MaxInputBytes = 1024 });

        result.HasErrors.Should().BeTrue();
        result.ErrorMessage.Should().Contain("MaxInputBytes");
    }

    [Fact]
    public void Oversized_string_is_rejected_before_parsing()
    {
        var xml = BpmnCorpusPaths.ReadHostileFixture("oversized.bpmn");

        var result = ImportHostile("oversized.bpmn", new BpmnImportOptions { MaxInputBytes = 1024 });

        result.HasErrors.Should().BeTrue();
        result.ErrorMessage.Should().Contain("1024");
    }

    [Fact]
    public void Too_deep_document_is_aborted_by_the_depth_limit()
    {
        // The fixture nests subprocesses 40 levels deep; cap the depth at 16.
        var result = ImportHostile("too-deep.bpmn", new BpmnImportOptions { MaxDepth = 16 });

        result.HasErrors.Should().BeTrue();
        result.ErrorMessage.Should().Contain("MaxDepth");
    }

    [Fact]
    public void Too_many_elements_is_aborted_by_the_element_count_limit()
    {
        var result = ImportHostile("too-many-elements.bpmn", new BpmnImportOptions { MaxElements = 20 });

        result.HasErrors.Should().BeTrue();
        result.ErrorMessage.Should().Contain("MaxElements");
    }

    [Fact]
    public void Normal_documents_still_import_under_tight_but_sufficient_limits()
    {
        var result = new BpmnXmlImporter().Import(
            BpmnCorpusPaths.ReadFixture("simple-process.bpmn"),
            new BpmnImportOptions { MaxInputBytes = 64 * 1024, MaxDepth = 64, MaxElements = 500 });

        result.HasErrors.Should().BeFalse();
        result.Models.Should().ContainSingle();
    }

    [Fact]
    public void Cancellation_is_honored()
    {
        using var stream = new MemoryStream(
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
                BpmnCorpusPaths.ReadFixture("simple-process.bpmn")));
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => new BpmnXmlImporter().Import(stream, new BpmnImportOptions(), cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Cancellation_mid_read_aborts_the_import()
    {
        // The stream cancels its own token while serving the first chunk; the bounded reader must
        // observe the token on the next read and abort with OperationCanceledException.
        var cts = new CancellationTokenSource();
        using var stream = new CancelOnFirstReadStream(
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
                BpmnCorpusPaths.ReadFixture("simple-process.bpmn")), cts);

        var act = () => new BpmnXmlImporter().Import(stream, new BpmnImportOptions(), cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Bounded_import_leaves_the_callers_stream_readable()
    {
        var bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(
            BpmnCorpusPaths.ReadHostileFixture("oversized.bpmn"));
        using var stream = new MemoryStream(bytes, writable: false);

        var result = new BpmnXmlImporter().Import(stream, new BpmnImportOptions { MaxInputBytes = 1024 });

        result.HasErrors.Should().BeTrue();
        stream.CanRead.Should().BeTrue("the bounded reader must not dispose the caller's stream");
        stream.Position = 0;
        stream.ReadByte().Should().Be(bytes[0]);
    }

    [Fact]
    public void String_import_honors_a_utf16_encoding_declaration()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-16"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="D" targetNamespace="urn:x">
              <process id="P" isExecutable="false">
                <task id="T" name="task"/>
              </process>
            </definitions>
            """;

        var result = new BpmnXmlImporter().Import(xml);

        result.HasErrors.Should().BeFalse(
            "the string overload decodes itself, so a utf-16 declaration cannot clash with the actual decoding");
        result.Models.Should().ContainSingle();
    }

    [Fact]
    public void Assignment_without_from_or_to_does_not_throw()
    {
        var result = ImportHostile("missing-assignment-endpoints.bpmn");

        result.HasErrors.Should().BeFalse("the semantic walk must tolerate missing assignment endpoints");
        var model = result.Models.Should().ContainSingle().Which;
        var association = model.Relationships.Should().ContainSingle().Which;
        association.Properties["assignments"][0].GetProperty("from").GetString().Should().BeEmpty();
        association.Properties["assignments"][0].GetProperty("to").GetString().Should().BeEmpty();
    }

    [Fact]
    public void Collaboration_with_only_a_message_flow_imports_into_a_synthesized_model()
    {
        var result = ImportHostile("collaboration-only-message-flows.bpmn");

        result.HasErrors.Should().BeFalse("a participant-less collaboration must not crash on an empty model list");
        var model = result.Models.Should().ContainSingle().Which;
        model.Id.Should().Be("Collaboration_Only");
        var flow = model.Relationships.Should().ContainSingle().Which;
        flow.RelationshipType.Should().Be("MessageFlow");
        flow.SourceElementId.Should().BeEmpty();
        flow.TargetElementId.Should().BeEmpty();
    }

    private sealed class CancelOnFirstReadStream : MemoryStream
    {
        private readonly CancellationTokenSource _cts;
        private bool _cancelled;

        public CancelOnFirstReadStream(byte[] buffer, CancellationTokenSource cts) : base(buffer, writable: false)
        {
            _cts = cts;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_cancelled)
            {
                _cancelled = true;
                _cts.Cancel();
            }

            return base.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));
    }

    private sealed class NonSeekableMemoryStream : MemoryStream
    {
        public NonSeekableMemoryStream(byte[] buffer) : base(buffer, writable: false)
        {
        }

        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
