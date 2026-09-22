using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Services;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Unit;

public class EmpiricalSecurityChallengeTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly FileStorageService _sut;

    public EmpiricalSecurityChallengeTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "EmpiricalSecTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
        _sut = new FileStorageService(_tempFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch
        {
            // Ignore temp folder cleanup
        }
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    public async Task Challenge_MZExecutableDisguisedAsImages_IsBlockedRegardlessOfExtension(string extension)
    {
        var fakeExe = TestFileHelper.CreateFakeExeBytes();
        var file = TestFileHelper.CreateFormFile(fakeExe, $"malware{extension}", "application/octet-stream");

        var act = () => _sut.SaveFileAsync(file, "defects");

        var ex = await act.Should().ThrowAsync<InvalidFileFormatException>();
        ex.WithMessage("*Executable files (.exe) are strictly prohibited.*");
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".EXE")]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".sh")]
    [InlineData(".php")]
    [InlineData(".aspx")]
    [InlineData(".dll")]
    public async Task Challenge_PolyglotFiles_ValidJpegHeaderWithDisallowedExtension_IsRejected(string badExtension)
    {
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        var file = TestFileHelper.CreateFormFile(jpegBytes, $"exploit{badExtension}", "image/jpeg");

        var act = () => _sut.SaveFileAsync(file, "defects");

        var ex = await act.Should().ThrowAsync<InvalidFileFormatException>();
        ex.WithMessage($"*File extension '{badExtension.ToLowerInvariant()}' is not allowed*");
    }

    [Fact]
    public async Task Challenge_PolyglotPngWithDisallowedExtension_IsRejected()
    {
        var pngBytes = TestFileHelper.CreateValidPngBytes();
        var file = TestFileHelper.CreateFormFile(pngBytes, "exploit.exe", "image/png");

        var act = () => _sut.SaveFileAsync(file, "defects");

        var ex = await act.Should().ThrowAsync<InvalidFileFormatException>();
        ex.WithMessage("*File extension '.exe' is not allowed*");
    }

    [Fact]
    public async Task Challenge_PolyglotWebpWithDisallowedExtension_IsRejected()
    {
        var webpBytes = TestFileHelper.CreateValidWebpBytes();
        var file = TestFileHelper.CreateFormFile(webpBytes, "exploit.sh", "image/webp");

        var act = () => _sut.SaveFileAsync(file, "defects");

        var ex = await act.Should().ThrowAsync<InvalidFileFormatException>();
        ex.WithMessage("*File extension '.sh' is not allowed*");
    }

    [Theory]
    [InlineData("7F 45 4C 46 02 01 01 00", "ELF binary disguised as .jpg", ".jpg")] // ELF
    [InlineData("25 50 44 46 2D 31 2E 34", "PDF header disguised as .png", ".png")]  // %PDF-1.4
    [InlineData("3C 73 63 72 69 70 74 3E", "<script> alert disguised as .webp", ".webp")]
    [InlineData("50 4B 03 04 14 00 06 00", "ZIP header disguised as .jpg", ".jpg")]   // PK zip
    public async Task Challenge_ForeignFileSignaturesDisguisedAsImages_AreRejected(string hexBytes, string desc, string ext)
    {
        var rawBytes = hexBytes.Split(' ')
            .Select(hex => Convert.ToByte(hex, 16))
            .Concat(new byte[32])
            .ToArray();

        var file = TestFileHelper.CreateFormFile(rawBytes, $"spoofed{ext}", "image/jpeg");

        var act = () => _sut.SaveFileAsync(file, "defects");

        var ex = await act.Should().ThrowAsync<InvalidFileFormatException>(because: desc);
        ex.WithMessage("*Invalid image format. Only genuine JPEG, PNG, and WEBP files are accepted.*");
    }

    [Fact]
    public async Task Challenge_TruncatedFileLessThan4Bytes_IsRejected()
    {
        var tinyBytes = new byte[] { 0xFF, 0xD8, 0xFF }; // 3 bytes
        var file = TestFileHelper.CreateFormFile(tinyBytes, "tiny.jpg", "image/jpeg");

        var act = () => _sut.SaveFileAsync(file, "defects");

        var ex = await act.Should().ThrowAsync<InvalidFileFormatException>();
        ex.WithMessage("*File is corrupted or too small*");
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("..\\..\\windows")]
    [InlineData("..")]
    [InlineData("../")]
    [InlineData("..\\")]
    [InlineData("sub/../../escaped")]
    [InlineData("sub\\..\\..\\escaped")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Challenge_SubFolderTraversalPayloads_AreRejectedWithArgumentException(string traversalPayload)
    {
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        var file = TestFileHelper.CreateFormFile(jpegBytes, "valid.jpg", "image/jpeg");

        var act = () => _sut.SaveFileAsync(file, traversalPayload);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*subfolder*");
    }

    [Theory]
    [InlineData("../../Windows/System32/drivers/etc/hosts")]
    [InlineData("..\\..\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("uploads/defects/../../secret.txt")]
    [InlineData("uploads\\defects\\..\\..\\secret.txt")]
    public void Challenge_DeleteFile_PathTraversalPayloads_AreBlockedAndReturnFalse(string traversalPath)
    {
        // Place a secret file outside the baseDirectory to ensure it cannot be deleted
        var outsideFolder = Path.Combine(Path.GetTempPath(), "EmpiricalOutside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideFolder);
        var secretFile = Path.Combine(outsideFolder, "secret.txt");
        File.WriteAllText(secretFile, "TOP_SECRET_CANARY");

        try
        {
            var result = _sut.DeleteFile(traversalPath);
            result.Should().BeFalse("Path traversal attempt in DeleteFile must return false.");
            File.Exists(secretFile).Should().BeTrue("Outside secret file must never be deleted by path traversal.");
        }
        finally
        {
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".JPG")]
    [InlineData(".jpeg")]
    [InlineData(".JPEG")]
    public async Task Challenge_GenuineJpegWithCaseVariations_IsAcceptedAndStored(string ext)
    {
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        var file = TestFileHelper.CreateFormFile(jpegBytes, $"sample{ext}", "image/jpeg");

        var result = await _sut.SaveFileAsync(file, "defects");

        result.Should().StartWith("/uploads/defects/");
        var diskPath = Path.Combine(_tempFolder, result.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".PNG")]
    public async Task Challenge_GenuinePngWithCaseVariations_IsAcceptedAndStored(string ext)
    {
        var pngBytes = TestFileHelper.CreateValidPngBytes();
        var file = TestFileHelper.CreateFormFile(pngBytes, $"sample{ext}", "image/png");

        var result = await _sut.SaveFileAsync(file, "defects");

        result.Should().StartWith("/uploads/defects/");
        var diskPath = Path.Combine(_tempFolder, result.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();
    }

    [Theory]
    [InlineData(".webp")]
    [InlineData(".WEBP")]
    public async Task Challenge_GenuineWebpWithCaseVariations_IsAcceptedAndStored(string ext)
    {
        var webpBytes = TestFileHelper.CreateValidWebpBytes();
        var file = TestFileHelper.CreateFormFile(webpBytes, $"sample{ext}", "image/webp");

        var result = await _sut.SaveFileAsync(file, "defects");

        result.Should().StartWith("/uploads/defects/");
        var diskPath = Path.Combine(_tempFolder, result.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();
    }

    [Fact]
    public async Task Challenge_Exact5MbBoundary_IsAccepted_And_5MbPlusOneByte_IsRejected()
    {
        // Exact 5MB (5,242,880 bytes)
        var exact5Mb = TestFileHelper.CreateExact5MbBytes();
        exact5Mb.Length.Should().Be(5 * 1024 * 1024);
        var passFile = TestFileHelper.CreateFormFile(exact5Mb, "pass5mb.jpg", "image/jpeg");

        var savedPath = await _sut.SaveFileAsync(passFile, "defects");
        savedPath.Should().NotBeNullOrEmpty();
        var diskPath = Path.Combine(_tempFolder, savedPath.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();

        // 5MB + 1 byte (5,242,881 bytes)
        var overOneByte = TestFileHelper.Create5MbPlusOneBytes();
        overOneByte.Length.Should().Be((5 * 1024 * 1024) + 1);
        var failFile = TestFileHelper.CreateFormFile(overOneByte, "fail5mb1b.jpg", "image/jpeg");

        var act = () => _sut.SaveFileAsync(failFile, "defects");
        var ex = await act.Should().ThrowAsync<PayloadTooLargeException>();
        ex.WithMessage("*exceeds the maximum allowed limit of 5242880 bytes (5MB)*");
    }

    [Fact]
    public async Task Challenge_DeleteFile_WithValidExistingFile_DeletesPhysicalFile()
    {
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        var file = TestFileHelper.CreateFormFile(jpegBytes, "to_delete.jpg", "image/jpeg");
        var savedPath = await _sut.SaveFileAsync(file, "defects");

        var diskPath = Path.Combine(_tempFolder, savedPath.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();

        var deleted = _sut.DeleteFile(savedPath);
        deleted.Should().BeTrue();
        File.Exists(diskPath).Should().BeFalse();
    }
}
