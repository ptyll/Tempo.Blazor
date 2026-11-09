using System.Xml;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>Raised internally when a safe-loading rule (input size, depth, element count) is violated.</summary>
internal sealed class BpmnXmlLimitException : Exception
{
    public BpmnXmlLimitException(string message) : base(message)
    {
    }
}

/// <summary>Safe XML loading for the BPMN importer: DTD prohibited, no resolver, bounded size/depth/elements.</summary>
internal static class BpmnXmlSecurity
{
    /// <summary>Settings shared by every load path — DTD processing is prohibited and no resolver is ever used.</summary>
    public static XmlReaderSettings CreateSafeSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = false,
        IgnoreComments = false,
        IgnoreProcessingInstructions = false,
        CloseInput = false,
    };

    /// <summary>
    /// Wraps a source stream so reading past <paramref name="maxBytes"/> aborts with a
    /// <see cref="BpmnXmlLimitException"/>. The wrapper never disposes the caller-owned
    /// <paramref name="source"/> stream and honors <paramref name="cancellationToken"/> on
    /// every read.
    /// </summary>
    public static Stream CreateBoundedStream(Stream source, long maxBytes, CancellationToken cancellationToken = default)
        => new BoundedReadStream(source, maxBytes, cancellationToken);

    /// <summary>Wraps a reader so documents deeper than <paramref name="maxDepth"/> or with more than
    /// <paramref name="maxElements"/> elements abort with a <see cref="BpmnXmlLimitException"/>.</summary>
    public static XmlReader CreateLimitEnforcingReader(XmlReader reader, int maxDepth, int maxElements)
        => new LimitEnforcingXmlReader(reader, maxDepth, maxElements);

    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private readonly CancellationToken _cancellationToken;
        private long _read;

        public BoundedReadStream(Stream inner, long maxBytes, CancellationToken cancellationToken)
        {
            _inner = inner;
            _maxBytes = maxBytes;
            _cancellationToken = cancellationToken;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var allowed = (int)Math.Min(count, _maxBytes - _read + 1);
            if (allowed <= 0)
            {
                throw new BpmnXmlLimitException(
                    $"The BPMN XML input exceeds the size limit of {_maxBytes} bytes (MaxInputBytes).");
            }

            var read = _inner.Read(buffer, offset, allowed);
            _read += read;
            if (_read > _maxBytes)
            {
                throw new BpmnXmlLimitException(
                    $"The BPMN XML input exceeds the size limit of {_maxBytes} bytes (MaxInputBytes).");
            }

            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allowed = (int)Math.Min(count, _maxBytes - _read + 1);
            if (allowed <= 0)
            {
                throw new BpmnXmlLimitException(
                    $"The BPMN XML input exceeds the size limit of {_maxBytes} bytes (MaxInputBytes).");
            }

            var read = await _inner.ReadAsync(buffer.AsMemory(offset, allowed), cancellationToken).ConfigureAwait(false);
            _read += read;
            if (_read > _maxBytes)
            {
                throw new BpmnXmlLimitException(
                    $"The BPMN XML input exceeds the size limit of {_maxBytes} bytes (MaxInputBytes).");
            }

            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // Deliberately no Dispose override: the caller owns the underlying stream and must be
        // able to keep reading (or reuse) it after the import has consumed the bounded prefix.
    }

    private sealed class LimitEnforcingXmlReader : XmlReader
    {
        private readonly XmlReader _inner;
        private readonly int _maxDepth;
        private readonly int _maxElements;
        private int _elements;

        public LimitEnforcingXmlReader(XmlReader inner, int maxDepth, int maxElements)
        {
            _inner = inner;
            _maxDepth = maxDepth;
            _maxElements = maxElements;
        }

        public override XmlNodeType NodeType => _inner.NodeType;
        public override string LocalName => _inner.LocalName;
        public override string NamespaceURI => _inner.NamespaceURI;
        public override string Prefix => _inner.Prefix;
        public override string Value => _inner.Value;
        public override int Depth => _inner.Depth;
        public override string BaseURI => _inner.BaseURI;
        public override bool IsEmptyElement => _inner.IsEmptyElement;
        public override XmlNameTable NameTable => _inner.NameTable;
        public override int AttributeCount => _inner.AttributeCount;
        public override bool EOF => _inner.EOF;
        public override ReadState ReadState => _inner.ReadState;
        public override XmlSpace XmlSpace => _inner.XmlSpace;
        public override string XmlLang => _inner.XmlLang;
        public override bool HasValue => _inner.HasValue;

        public override bool Read()
        {
            var moved = _inner.Read();
            if (!moved)
            {
                return false;
            }

            if (_inner.NodeType == XmlNodeType.Element)
            {
                _elements++;
                if (_elements > _maxElements)
                {
                    throw new BpmnXmlLimitException(
                        $"The BPMN XML document exceeds the element count limit of {_maxElements} (MaxElements).");
                }

                if (_inner.Depth > _maxDepth)
                {
                    throw new BpmnXmlLimitException(
                        $"The BPMN XML document exceeds the depth limit of {_maxDepth} (MaxDepth).");
                }
            }

            return true;
        }

        public override string GetAttribute(string name) => _inner.GetAttribute(name);
        public override string GetAttribute(string name, string? namespaceURI) => _inner.GetAttribute(name, namespaceURI);
        public override string GetAttribute(int i) => _inner.GetAttribute(i);
        public override bool MoveToAttribute(string name) => _inner.MoveToAttribute(name);
        public override bool MoveToAttribute(string name, string? ns) => _inner.MoveToAttribute(name, ns);
        public override bool MoveToFirstAttribute() => _inner.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => _inner.MoveToNextAttribute();
        public override bool MoveToElement() => _inner.MoveToElement();
        public override string LookupNamespace(string prefix) => _inner.LookupNamespace(prefix);
        public override bool ReadAttributeValue() => _inner.ReadAttributeValue();
        public override void ResolveEntity() => _inner.ResolveEntity();
        public override bool CanResolveEntity => false;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
