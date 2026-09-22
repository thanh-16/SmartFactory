using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;

namespace SmartFactory.Tests.Fixtures;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
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
        }
    }
}
