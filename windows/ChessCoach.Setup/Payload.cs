using System.Text;

namespace ChessCoach.Setup;

internal sealed class Payload
{
    private const string Magic = "CCSETUP1";
    private const int FooterSize = 16 + 8;

    public required string File { get; init; }
    public long Start { get; init; }
    public long Length { get; init; }
    public long StubLength => Start;

    public static Payload? Find(string file)
    {
        using var stream = System.IO.File.OpenRead(file);
        if (stream.Length < FooterSize) return null;
        var footer = new byte[FooterSize];
        stream.Seek(-FooterSize, SeekOrigin.End);
        stream.ReadExactly(footer);
        if (Encoding.ASCII.GetString(footer, 16, 8) != Magic) return null;
        if (!long.TryParse(Encoding.ASCII.GetString(footer, 0, 16), System.Globalization.NumberStyles.HexNumber, null, out var length)) return null;
        var start = stream.Length - FooterSize - length;
        return start < 0 ? null : new Payload { File = file, Start = start, Length = length };
    }

    public Stream Open() => new Section(System.IO.File.OpenRead(File), Start, Length);

    public static byte[] Footer(long length) => Encoding.ASCII.GetBytes(length.ToString("x16") + Magic);

    private sealed class Section(Stream inner, long start, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => _position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = (int)Math.Min(count, length - _position);
            if (take <= 0) return 0;
            inner.Seek(start + _position, SeekOrigin.Begin);
            var read = inner.Read(buffer, offset, take);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            _position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset, _ => length + offset };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
