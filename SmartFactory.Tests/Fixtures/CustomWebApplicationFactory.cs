using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartFactory.Api.Data;

namespace SmartFactory.Tests.Fixtures;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Suppress background workers (e.g. SpcMonitoringBackgroundService) during testing to avoid SQLite locks and race conditions
            services.RemoveAll<IHostedService>();

            // Remove existing DbContext options
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<FactoryDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Override IFileStorageService to ensure uploaded files are stored in the test runner's wwwroot directory
            var fileStorageDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(SmartFactory.Api.Services.IFileStorageService));
            if (fileStorageDescriptor != null)
            {
                services.Remove(fileStorageDescriptor);
            }
            var testWebRoot = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot");
            services.AddScoped<SmartFactory.Api.Services.IFileStorageService>(_ => new SmartFactory.Api.Services.FileStorageService(testWebRoot));

            // Create persistent in-memory SQLite connection
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<FactoryDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Ensure schema and seed data are populated
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        });

        builder.UseEnvironment("Testing");
    }

    public void ResetDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Close();
            _connection?.Dispose();

            // Auto-cleanup test mock uploads to preserve disk space and pipeline hygiene
            try
            {
                var testUploadDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot", "uploads", "defects");
                if (System.IO.Directory.Exists(testUploadDir))
                {
                    var testFiles = System.IO.Directory.GetFiles(testUploadDir);
                    foreach (var file in testFiles)
                    {
                        try { System.IO.File.Delete(file); } catch { /* Ignore locked or in-use files */ }
                    }
                }
            }
            catch
            {
                // Non-blocking cleanup
            }
        }
    }
}
