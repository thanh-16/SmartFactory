using FluentAssertions;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Services;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Unit;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly FileStorageService _sut;

    public FileStorageServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "SmartFactoryTests_" + Guid.NewGuid().ToString("N"));
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
            // Ignore temp folder cleanup errors
        }
    }

    [Fact]
    public async Task SaveFileAsync_WithValidJpeg_ReturnsRelativePathAndSavesFile()
    {
        // Arrange
        var bytes = TestFileHelper.CreateValidJpegBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "defect1.jpg", "image/jpeg");

        // Act
        var result = await _sut.SaveFileAsync(file, "defects");

        // Assert
        result.Should().StartWith("/uploads/defects/");
        result.Should().EndWith(".jpg");

        var diskPath = Path.Combine(_tempFolder, result.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFileAsync_WithValidPng_ReturnsRelativePathAndSavesFile()
    {
        // Arrange
        var bytes = TestFileHelper.CreateValidPngBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "defect2.png", "image/png");

        // Act
        var result = await _sut.SaveFileAsync(file, "defects");

        // Assert
        result.Should().StartWith("/uploads/defects/");
        result.Should().EndWith(".png");

        var diskPath = Path.Combine(_tempFolder, result.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFileAsync_WithValidWebp_ReturnsRelativePathAndSavesFile()
    {
        // Arrange
        var bytes = TestFileHelper.CreateValidWebpBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "defect3.webp", "image/webp");

        // Act
        var result = await _sut.SaveFileAsync(file, "defects");

        // Assert
        result.Should().StartWith("/uploads/defects/");
        result.Should().EndWith(".webp");

        var diskPath = Path.Combine(_tempFolder, result.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveFileAsync_WithFakeExeDisguisedAsJpg_ThrowsInvalidFileFormatException()
    {
        // Arrange: Spoofed file with MZ header renamed to .jpg
        var bytes = TestFileHelper.CreateFakeExeBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "trojan.jpg", "image/jpeg");

        // Act
        var act = () => _sut.SaveFileAsync(file, "defects");

        // Assert
        await act.Should().ThrowAsync<InvalidFileFormatException>()
            .WithMessage("*Executable files (.exe) are strictly prohibited.*");
    }

    [Fact]
    public async Task SaveFileAsync_WithZeroByteFile_ThrowsInvalidFileFormatException()
    {
        // Arrange
        var bytes = TestFileHelper.CreateZeroBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "empty.jpg", "image/jpeg");

        // Act
        var act = () => _sut.SaveFileAsync(file, "defects");

        // Assert
        await act.Should().ThrowAsync<InvalidFileFormatException>()
            .WithMessage("*File is empty or zero bytes.*");
    }

    [Fact]
    public async Task SaveFileAsync_WithOver5MbFile_ThrowsPayloadTooLargeException()
    {
        // Arrange: >5MB payload
        var bytes = TestFileHelper.CreateOver5MbBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "large.jpg", "image/jpeg");

        // Act
        var act = () => _sut.SaveFileAsync(file, "defects");

        // Assert
        await act.Should().ThrowAsync<PayloadTooLargeException>()
            .WithMessage("*exceeds the maximum allowed limit of 5242880 bytes (5MB)*");
    }

    [Fact]
    public async Task DeleteFile_WithExistingFile_DeletesPhysicalFileAndReturnsTrue()
    {
        // Arrange
        var bytes = TestFileHelper.CreateValidJpegBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "test.jpg", "image/jpeg");
        var savedPath = await _sut.SaveFileAsync(file, "defects");

        var diskPath = Path.Combine(_tempFolder, savedPath.TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue();

        // Act
        var deleted = _sut.DeleteFile(savedPath);

        // Assert
        deleted.Should().BeTrue();
        File.Exists(diskPath).Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_WithNonExistentFile_ReturnsFalse()
    {
        // Act
        var result = _sut.DeleteFile("/uploads/defects/nonexistent.jpg");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void DeleteFile_WithPathTraversalAttempt_ReturnsFalse()
    {
        // Act: attempt to traverse outside baseDirectory
        var result = _sut.DeleteFile("../../Windows/System32/drivers/etc/hosts");

        // Assert
        result.Should().BeFalse();
    }
}
