using Microsoft.AspNetCore.Http;

namespace SmartFactory.Tests.Helpers;

public static class TestFileHelper
{
    public static byte[] CreateValidJpegBytes()
    {
        // Standard JPEG SOI (FF D8) + APP0 marker with JFIF
        return new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46,
            0x00, 0x01, 0x01, 0x01, 0x00, 0x60, 0x00, 0x60, 0x00, 0x00,
            0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x06, 0x06, 0x07, 0x06,
            0xFF, 0xD9 // EOI
        };
    }

    public static byte[] CreateValidPngBytes()
    {
        // Standard PNG 8-byte signature: 89 50 4E 47 0D 0A 1A 0A followed by IHDR chunk
        return new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89
        };
    }

    public static byte[] CreateValidWebpBytes()
    {
        // RIFF header (52 49 46 46) + 4 bytes length + WEBP (57 45 42 50) + VP8 payload
        return new byte[]
        {
            0x52, 0x49, 0x46, 0x46, // RIFF
            0x20, 0x00, 0x00, 0x00, // Size
            0x57, 0x45, 0x42, 0x50, // WEBP
            0x56, 0x50, 0x38, 0x20, // VP8 
            0x14, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
    }

    public static byte[] CreateFakeExeBytes()
    {
        // DOS executable MZ header (4D 5A) disguised as image
        return new byte[]
        {
            0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
            0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
    }

    public static byte[] CreateZeroBytes()
    {
        return Array.Empty<byte>();
    }

    public static byte[] CreateOver5MbBytes()
    {
        // 5MB + 1024 bytes (5,243,904 bytes) with valid JPEG header at the start
        var large = new byte[(5 * 1024 * 1024) + 1024];
        var header = CreateValidJpegBytes();
        Array.Copy(header, 0, large, 0, header.Length);
        return large;
    }

    public static byte[] CreateExact5MbBytes()
    {
        // Exactly 5,242,880 bytes with valid JPEG header
        var exact = new byte[5 * 1024 * 1024];
        var header = CreateValidJpegBytes();
        Array.Copy(header, 0, exact, 0, header.Length);
        return exact;
    }

    public static byte[] Create5MbPlusOneBytes()
    {
        // Exactly 5,242,881 bytes (5MB + 1 byte) with valid JPEG header
        var overOne = new byte[(5 * 1024 * 1024) + 1];
        var header = CreateValidJpegBytes();
        Array.Copy(header, 0, overOne, 0, header.Length);
        return overOne;
    }

    public static IFormFile CreateFormFile(byte[] content, string fileName, string contentType = "image/jpeg")
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "Image", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
