using Microsoft.AspNetCore.Http;
using SmartFactory.Api.Exceptions;

namespace SmartFactory.Api.Services;

public class FileStorageService : IFileStorageService
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB
    private readonly string _baseDirectory;

    public FileStorageService(IWebHostEnvironment? env = null)
    {
        _baseDirectory = env?.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        if (!Directory.Exists(_baseDirectory))
        {
            Directory.CreateDirectory(_baseDirectory);
        }
    }

    // Constructor overload allowing explicit root directory (useful for unit tests)
    public FileStorageService(string baseDirectory)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        if (!Directory.Exists(_baseDirectory))
        {
            Directory.CreateDirectory(_baseDirectory);
        }
    }

    public async Task<string> SaveFileAsync(IFormFile file, string subFolder, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
        {
            throw new InvalidFileFormatException("File is empty or zero bytes.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new PayloadTooLargeException($"File size {file.Length} bytes exceeds the maximum allowed limit of {MaxFileSizeBytes} bytes (5MB).");
        }

        ValidateMagicBytes(file);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".jpg";
        }

        var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
        var targetDirectory = Path.Combine(_baseDirectory, "uploads", subFolder);

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var fullPath = Path.Combine(targetDirectory, uniqueFileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await file.CopyToAsync(stream, ct);
        }

        return $"/uploads/{subFolder}/{uniqueFileName}";
    }

    public bool DeleteFile(string relativeOrFullPath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrFullPath))
        {
            return false;
        }

        try
        {
            string fullPath;
            if (Path.IsPathRooted(relativeOrFullPath) && !relativeOrFullPath.StartsWith("/"))
            {
                fullPath = Path.GetFullPath(relativeOrFullPath);
            }
            else
            {
                var cleanRelative = relativeOrFullPath.TrimStart('/', '\\');
                fullPath = Path.GetFullPath(Path.Combine(_baseDirectory, cleanRelative));
            }

            // Path Traversal Security Guard: Ensure target path resides within allowed directory
            var resolvedBase = Path.GetFullPath(_baseDirectory);
            if (!fullPath.StartsWith(resolvedBase, StringComparison.OrdinalIgnoreCase))
            {
                // Reject path traversal attempt
                return false;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateMagicBytes(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        if (stream.Length < 4)
        {
            throw new InvalidFileFormatException("File is corrupted or too small to contain a valid image header.");
        }

        var header = new byte[16];
        var bytesRead = stream.Read(header, 0, Math.Min((int)stream.Length, 16));

        // 1. Check for Executable / Script spoofing (.exe MZ header)
        if (bytesRead >= 2 && header[0] == 0x4D && header[1] == 0x5A) // "MZ"
        {
            throw new InvalidFileFormatException("Executable files (.exe) are strictly prohibited.");
        }

        // 2. Check for JPEG: FF D8 FF
        if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return;
        }

        // 3. Check for PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytesRead >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return;
        }

        // 4. Check for WEBP: RIFF (bytes 0..3) .... WEBP (bytes 8..11)
        if (bytesRead >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // "RIFF"
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)  // "WEBP"
        {
            return;
        }

        throw new InvalidFileFormatException("Invalid image format. Only genuine JPEG, PNG, and WEBP files are accepted.");
    }
}
