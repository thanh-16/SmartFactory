using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Data;
using SmartFactory.Api.Middleware;
using SmartFactory.Api.Repositories;
using SmartFactory.Api.Services;

using SmartFactory.Api.Hubs;
using QuestPDF.Infrastructure;

// Configure QuestPDF License (Community)
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

// Configure CORS for web client and SignalR WebSocket
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Configure SQLite Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Data Source=smartfactory.db;Cache=Shared";

builder.Services.AddDbContext<FactoryDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Register Repositories
builder.Services.AddScoped<IProductionLotRepository, ProductionLotRepository>();
builder.Services.AddScoped<INcrReportRepository, NcrReportRepository>();
builder.Services.AddScoped<IWorkStationRepository, WorkStationRepository>();
builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();

// Register Services
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<INcrService, NcrService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IAiInspectionService, AiInspectionService>();
builder.Services.AddScoped<INcrPdfExportService, NcrPdfExportService>();
builder.Services.AddScoped<ISpcAnalysisService, SpcAnalysisService>();
builder.Services.AddHostedService<SpcMonitoringBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();
app.MapHub<FactoryHub>("/hubs/factory");
app.MapHealthChecks("/health");

// Ensure DB schema and apply SQLite WAL pragmas
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
    db.Database.EnsureCreated();
    try
    {
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode = 'wal'; PRAGMA busy_timeout = 5000;");
    }
    catch
    {
        // Safe fallback for providers or in-memory contexts that do not support PRAGMA
    }
}

app.Run();

public partial class Program { }
