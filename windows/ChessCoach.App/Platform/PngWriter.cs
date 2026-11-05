using System.IO.Compression;

namespace ChessCoach.App.Platform;

internal static class PngWriter
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
        {
            var line = new byte[width * 3 + 1];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) Array.Copy(rgba, (y * width + x) * 4, line, 1 + x * 3, 3);
                z.Write(line);
            }
        }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteInt(header, 0, width); WriteInt(header, 4, height);
        header[8] = 8; header[9] = 2;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24); buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8); buffer[offset + 3] = (byte)value;
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        var head = new byte[4];
        WriteInt(head, 0, data.Length);
        output.Write(head);
        output.Write(body);
        uint crc = 0xFFFFFFFF;
        foreach (var b in body) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        WriteInt(head, 0, (int)~crc);
        output.Write(head);
    }
}
