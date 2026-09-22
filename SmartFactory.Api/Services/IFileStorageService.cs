using Microsoft.AspNetCore.Http;

namespace SmartFactory.Api.Services;

public interface IFileStorageService
{
    Task<string> SaveFileAsync(IFormFile file, string subFolder, CancellationToken ct = default);
    bool DeleteFile(string relativeOrFullPath);
}
