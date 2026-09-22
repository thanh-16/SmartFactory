using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Services;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class FileUploadAdversarialChallengeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _baseDir;
    private readonly string _uploadDir;

    public FileUploadAdversarialChallengeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _baseDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        _uploadDir = Path.Combine(_baseDir, "uploads", "defects");
        if (!Directory.Exists(_uploadDir))
        {
            Directory.CreateDirectory(_uploadDir);
        }
    }

    [Fact]
    public async Task Empirical_GenuineJpegFile_IsAcceptedAndPersisted()
    {
        // Arrange
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Crack", "Minor", "Valid JPEG test", jpegBytes, "genuine.jpg", "image/jpeg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        report.Should().NotBeNull();
        report!.ImageUrls.Should().HaveCount(1);
        report.ImageUrls[0].Should().StartWith("/uploads/defects/");
        report.ImageUrls[0].Should().EndWith(".jpg");

        var diskPath = Path.Combine(_baseDir, report.ImageUrls[0].TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue("Uploaded JPEG must physically exist on disk.");
    }

    [Fact]
    public async Task Empirical_GenuinePngFile_IsAcceptedAndPersisted()
    {
        // Arrange
        var pngBytes = TestFileHelper.CreateValidPngBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Scratch", "Minor", "Valid PNG test", pngBytes, "genuine.png", "image/png");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        report.Should().NotBeNull();
        report!.ImageUrls.Should().HaveCount(1);
        report.ImageUrls[0].Should().StartWith("/uploads/defects/");
        report.ImageUrls[0].Should().EndWith(".png");

        var diskPath = Path.Combine(_baseDir, report.ImageUrls[0].TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue("Uploaded PNG must physically exist on disk.");
    }

    [Fact]
    public async Task Empirical_GenuineWebpFile_IsAcceptedAndPersisted()
    {
        // Arrange
        var webpBytes = TestFileHelper.CreateValidWebpBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Dent", "Minor", "Valid WEBP test", webpBytes, "genuine.webp", "image/webp");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        report.Should().NotBeNull();
        report!.ImageUrls.Should().HaveCount(1);
        report.ImageUrls[0].Should().StartWith("/uploads/defects/");
        report.ImageUrls[0].Should().EndWith(".webp");

        var diskPath = Path.Combine(_baseDir, report.ImageUrls[0].TrimStart('/'));
        File.Exists(diskPath).Should().BeTrue("Uploaded WEBP must physically exist on disk.");
    }

    [Fact]
    public async Task Empirical_FakeExeDisguisedAsJpg_IsRejectedWith400ProblemDetails_AndLeavesNoOrphan()
    {
        // Arrange
        var filesBefore = Directory.GetFiles(_uploadDir);
        var fakeExe = TestFileHelper.CreateFakeExeBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Crack", "Major", "Fake exe test", fakeExe, "trojan.jpg", "image/jpeg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Invalid File Format");
        problem.Detail.Should().Contain("Executable files (.exe) are strictly prohibited");

        var filesAfter = Directory.GetFiles(_uploadDir);
        filesAfter.Length.Should().Be(filesBefore.Length, "Rejected fake .exe must never leave a file on disk.");
    }

    [Fact]
    public async Task Empirical_FakeExeDisguisedAsPng_IsRejectedWith400ProblemDetails()
    {
        // Arrange
        var fakeExe = TestFileHelper.CreateFakeExeBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Crack", "Major", "Fake exe PNG test", fakeExe, "trojan.png", "image/png");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Detail.Should().Contain("Executable files (.exe) are strictly prohibited");
    }

    [Fact]
    public async Task Empirical_ZeroByteFile_IsRejectedWith400ProblemDetails_AndLeavesNoOrphan()
    {
        // Arrange
        var filesBefore = Directory.GetFiles(_uploadDir);
        var zeroBytes = TestFileHelper.CreateZeroBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Crack", "Minor", "0-byte test", zeroBytes, "empty.jpg", "image/jpeg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Invalid File Format");
        problem.Detail.Should().Contain("File is empty or zero bytes");

        var filesAfter = Directory.GetFiles(_uploadDir);
        filesAfter.Length.Should().Be(filesBefore.Length, "0-byte file must never be written to disk.");
    }

    [Fact]
    public async Task Empirical_Over5MbFile_IsRejectedWith400Or413ProblemDetails_AndLeavesNoOrphan()
    {
        // Arrange
        var filesBefore = Directory.GetFiles(_uploadDir);
        var largeBytes = TestFileHelper.CreateOver5MbBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Deformation", "Major", "Over 5MB test", largeBytes, "huge.jpg", "image/jpeg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Detail.Should().Contain("exceeds the maximum allowed limit of 5242880 bytes (5MB)");

        var filesAfter = Directory.GetFiles(_uploadDir);
        filesAfter.Length.Should().Be(filesBefore.Length, "Over-limit payload must never be written to disk.");
    }

    [Fact]
    public async Task Empirical_DisguisedScript_IsRejectedWith400ProblemDetails()
    {
        // Arrange: A bash script with .jpg extension
        var scriptBytes = System.Text.Encoding.UTF8.GetBytes("#!/bin/bash\nrm -rf /");
        using var form = CreateMultipartForm(1, 1, 2, "Crack", "Minor", "Bash script test", scriptBytes, "script.jpg", "image/jpeg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Detail.Should().Contain("Invalid image format. Only genuine JPEG, PNG, and WEBP files are accepted.");
    }

    [Fact]
    public async Task Empirical_DatabaseRollbackSimulation_PhysicallyDeletesFile_ZeroOrphanArtifacts()
    {
        // Arrange: Spy wrapper on IFileStorageService to capture exact saved relative path
        string? capturedSavedPath = null;
        string? capturedDeletedPath = null;
        bool deleteFileWasCalled = false;

        var interceptor = new DbCrashInterceptor { TriggerFailure = true };
        var crashConnection = new SqliteConnection("DataSource=:memory:");
        crashConnection.Open();

        using var crashingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Decorate / wrap IFileStorageService to trace SaveFileAsync and DeleteFile calls
                var storageDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IFileStorageService));
                var realStorage = new FileStorageService(_baseDir);

                if (storageDescriptor != null)
                {
                    services.Remove(storageDescriptor);
                }

                var spyService = new SpyFileStorageService(realStorage, 
                    onSaved: path => capturedSavedPath = path,
                    onDeleted: (path, result) =>
                    {
                        capturedDeletedPath = path;
                        deleteFileWasCalled = true;
                    });

                services.AddSingleton<IFileStorageService>(spyService);

                // Configure crashing DbContext
                var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<FactoryDbContext>));
                if (dbDescriptor != null)
                {
                    services.Remove(dbDescriptor);
                }

                services.AddDbContext<FactoryDbContext>(options =>
                {
                    options.UseSqlite(crashConnection);
                    options.AddInterceptors(interceptor);
                });

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
                db.Database.EnsureCreated();
            });
        });

        var client = crashingFactory.CreateClient();

        var filesBefore = Directory.GetFiles(_uploadDir);
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = CreateMultipartForm(1, 1, 2, "Crack", "Critical", "Crash rollback test", jpegBytes, "crash_target.jpg", "image/jpeg");

        // Act
        var response = await client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert: 1. API fails with 500 InternalServerError
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Assert: 2. File was actually saved during the request
        capturedSavedPath.Should().NotBeNullOrEmpty("File must have been saved prior to DB transaction.");
        var physicalPath = Path.Combine(_baseDir, capturedSavedPath!.TrimStart('/'));

        // Assert: 3. DeleteFile was invoked for the compensating cleanup
        deleteFileWasCalled.Should().BeTrue("Compensating cleanup must invoke DeleteFile on rollback.");
        capturedDeletedPath.Should().Be(capturedSavedPath, "Compensating cleanup must delete the exact file that was uploaded.");

        // Assert: 4. The physical file does NOT exist on disk anymore
        File.Exists(physicalPath).Should().BeFalse("Physical file must be deleted from disk upon DB transaction rollback.");

        // Assert: 5. Zero orphan files
        var filesAfter = Directory.GetFiles(_uploadDir);
        filesAfter.Length.Should().Be(filesBefore.Length, "Directory must have exactly zero orphan files remaining after rollback.");

        crashConnection.Close();
        crashConnection.Dispose();
    }

    private static MultipartFormDataContent CreateMultipartForm(
        int lotId, int stationId, int userId, string defectType, string severity, string description,
        byte[] fileBytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(lotId.ToString()), "LotId");
        form.Add(new StringContent(stationId.ToString()), "StationId");
        form.Add(new StringContent(userId.ToString()), "ReportedByUserId");
        form.Add(new StringContent(defectType), "DefectType");
        form.Add(new StringContent(severity), "Severity");
        form.Add(new StringContent(description), "Description");

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "Image", fileName);

        return form;
    }

    private class SpyFileStorageService : IFileStorageService
    {
        private readonly IFileStorageService _inner;
        private readonly Action<string> _onSaved;
        private readonly Action<string, bool> _onDeleted;

        public SpyFileStorageService(IFileStorageService inner, Action<string> onSaved, Action<string, bool> onDeleted)
        {
            _inner = inner;
            _onSaved = onSaved;
            _onDeleted = onDeleted;
        }

        public async Task<string> SaveFileAsync(Microsoft.AspNetCore.Http.IFormFile file, string subFolder, CancellationToken ct = default)
        {
            var path = await _inner.SaveFileAsync(file, subFolder, ct);
            _onSaved(path);
            return path;
        }

        public bool DeleteFile(string relativeFilePath)
        {
            var result = _inner.DeleteFile(relativeFilePath);
            _onDeleted(relativeFilePath, result);
            return result;
        }
    }
}
