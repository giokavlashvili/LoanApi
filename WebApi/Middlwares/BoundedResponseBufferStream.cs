using System.Net.Http.Headers;
using System.Text;

namespace WebApi.Middlwares
{
    /// <summary>
    /// Wraps the response body so it can be logged without ever holding a whole payload
    /// in memory. Writes always pass straight through; a copy is kept only when the
    /// response turns out to be a loggable media type, and only up to a byte cap.
    /// <para>
    /// This is deliberately not the usual "swap in a MemoryStream" trick: that buffers
    /// file downloads in full and will exhaust memory on a large one. Here the decision
    /// is deferred to the first write, when <c>Content-Type</c> is finally known.
    /// </para>
    /// </summary>
    internal sealed class BoundedResponseBufferStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponse _response;
        private readonly Func<string?, bool> _isLoggableContentType;
        private readonly int _maxBytes;

        private MemoryStream? _buffer;
        private bool _decided;
        private bool _capturing;
        private bool _truncated;
        private string? _skipReason;

        public BoundedResponseBufferStream(
            Stream inner,
            HttpResponse response,
            Func<string?, bool> isLoggableContentType,
            int maxBytes)
        {
            _inner = inner;
            _response = response;
            _isLoggableContentType = isLoggableContentType;
            _maxBytes = maxBytes;
        }

        /// <summary>The captured body, or null when nothing loggable was written.</summary>
        public string? GetCapturedText()
        {
            if (!_capturing || _buffer is null || _buffer.Length == 0)
                return null;

            return Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (int)_buffer.Length);
        }

        /// <summary>
        /// True when the response exceeded the cap. The captured text is then a partial
        /// document, which cannot be parsed and therefore cannot be safely redacted — the
        /// caller must discard it rather than log it raw.
        /// </summary>
        public bool IsTruncated => _truncated;

        /// <summary>Why the body was not captured, for the log row. Null when it was.</summary>
        public string? SkipReason => _decided && !_capturing ? _skipReason : null;

        private void EnsureDecision()
        {
            if (_decided)
                return;

            _decided = true;

            var contentType = _response.ContentType;

            // A download is content we must never copy, whatever its media type claims.
            if (IsAttachment())
            {
                _skipReason = "[response body omitted: attachment]";
                return;
            }

            if (!_isLoggableContentType(contentType))
            {
                var length = _response.ContentLength;
                _skipReason = $"[response body omitted: {contentType ?? "unknown content type"}" +
                              (length.HasValue ? $", {length.Value} bytes]" : "]");
                return;
            }

            _capturing = true;
            _buffer = new MemoryStream();
        }

        private bool IsAttachment()
        {
            var disposition = _response.Headers.ContentDisposition.ToString();

            if (string.IsNullOrEmpty(disposition))
                return false;

            return ContentDispositionHeaderValue.TryParse(disposition, out var parsed)
                && string.Equals(parsed.DispositionType, "attachment", StringComparison.OrdinalIgnoreCase);
        }

        private void Capture(ReadOnlySpan<byte> data)
        {
            EnsureDecision();

            if (!_capturing || _buffer is null || data.IsEmpty)
                return;

            var remaining = _maxBytes - (int)_buffer.Length;

            if (remaining <= 0)
            {
                _truncated = true;
                return;
            }

            if (data.Length > remaining)
            {
                _buffer.Write(data[..remaining]);
                _truncated = true;
                return;
            }

            _buffer.Write(data);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            _inner.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            Span<byte> single = stackalloc byte[1] { value };
            Capture(single);
            _inner.WriteByte(value);
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _buffer?.Dispose();

            // The inner stream belongs to the server; the middleware restores it instead.
            base.Dispose(disposing);
        }
    }
}
