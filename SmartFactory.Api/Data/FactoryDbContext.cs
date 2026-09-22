using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Data;

public class FactoryDbContext : DbContext
{
    public FactoryDbContext(DbContextOptions<FactoryDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<WorkStation> WorkStations => Set<WorkStation>();
    public DbSet<ProductionLot> ProductionLots => Set<ProductionLot>();
    public DbSet<NcrReport> NcrReports => Set<NcrReport>();
    public DbSet<DefectImage> DefectImages => Set<DefectImage>();
    public DbSet<NcrDecision> NcrDecisions => Set<NcrDecision>();
    public DbSet<StationHourlyMetric> StationHourlyMetrics => Set<StationHourlyMetric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // AppUser
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // WorkStation
        modelBuilder.Entity<WorkStation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(150);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasIndex(e => e.Code).IsUnique();
        });

        // ProductionLot
        modelBuilder.Entity<ProductionLot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LotNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.LotNumber).IsUnique();

            entity.HasOne(e => e.WorkStation)
                  .WithMany(w => w.ProductionLots)
                  .HasForeignKey(e => e.WorkStationId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // NcrReport
        modelBuilder.Entity<NcrReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NcrNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DefectType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.RootCauseAnalysisJson).HasMaxLength(8000).IsRequired(false);
            entity.HasIndex(e => e.NcrNumber).IsUnique();

            entity.HasOne(e => e.ProductionLot)
                  .WithMany(l => l.NcrReports)
                  .HasForeignKey(e => e.ProductionLotId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.WorkStation)
                  .WithMany(w => w.NcrReports)
                  .HasForeignKey(e => e.WorkStationId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReportedByUser)
                  .WithMany()
                  .HasForeignKey(e => e.ReportedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // DefectImage
        modelBuilder.Entity<DefectImage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ImageUrl).IsRequired().HasMaxLength(500);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ContentType).HasMaxLength(100);

            entity.HasOne(e => e.NcrReport)
                  .WithMany(n => n.DefectImages)
                  .HasForeignKey(e => e.NcrReportId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // NcrDecision
        modelBuilder.Entity<NcrDecision>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Decision).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasOne(e => e.NcrReport)
                  .WithMany(n => n.Decisions)
                  .HasForeignKey(e => e.NcrReportId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ApprovedByUser)
                  .WithMany()
                  .HasForeignKey(e => e.ApprovedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // StationHourlyMetric (SPC time series)
        modelBuilder.Entity<StationHourlyMetric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DominantDefectType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TriggeredRule).IsRequired().HasMaxLength(50);

            entity.HasIndex(e => new { e.WorkStationId, e.WindowStartTime });

            entity.HasOne(e => e.WorkStation)
                  .WithMany()
                  .HasForeignKey(e => e.WorkStationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var seedDate = DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime();

        // Seed 3 WorkStations
        modelBuilder.Entity<WorkStation>().HasData(
            new WorkStation
            {
                Id = 1,
                Code = "ST-01",
                Name = "Trạm Cắt & Dập",
                Description = "Gia công thô tấm kim loại định hình",
                IsActive = true,
                CreatedAt = seedDate
            },
            new WorkStation
            {
                Id = 2,
                Code = "ST-02",
                Name = "Trạm Hàn Robot",
                Description = "Hàn kết cấu tự động công nghệ cao",
                IsActive = true,
                CreatedAt = seedDate
            },
            new WorkStation
            {
                Id = 3,
                Code = "ST-03",
                Name = "Trạm Sơn Tĩnh Điện",
                Description = "Phủ sơn bảo vệ bề mặt thành phẩm",
                IsActive = true,
                CreatedAt = seedDate
            }
        );

        // Seed 3 AppUsers
        modelBuilder.Entity<AppUser>().HasData(
            new AppUser
            {
                Id = 1,
                FullName = "Trần Văn Quản Đốc",
                Email = "supervisor@smartfactory.vn",
                Role = "Supervisor",
                CreatedAt = seedDate
            },
            new AppUser
            {
                Id = 2,
                FullName = "Lê Thị KCS",
                Email = "kcs@smartfactory.vn",
                Role = "KCS",
                CreatedAt = seedDate
            },
            new AppUser
            {
                Id = 3,
                FullName = "Vũ Đình Giám Đốc",
                Email = "manager@smartfactory.vn",
                Role = "Manager",
                CreatedAt = DateTime.Parse("2026-01-01T00:00:00Z")
            }
        );

        // Seed 3 ProductionLots
        modelBuilder.Entity<ProductionLot>().HasData(
            new ProductionLot
            {
                Id = 1,
                LotNumber = "LOT-2026-001",
                WorkStationId = 1,
                ProductName = "Vỏ Máy Biến Áp 250kVA",
                Quantity = 500,
                DefectQuantity = 0,
                Status = "InProgress",
                CreatedAt = seedDate,
                UpdatedAt = null
            },
            new ProductionLot
            {
                Id = 2,
                LotNumber = "LOT-2026-002",
                WorkStationId = 2,
                ProductName = "Khung Thép Tủ Điện Trung Thế",
                Quantity = 300,
                DefectQuantity = 0,
                Status = "InProgress",
                CreatedAt = seedDate,
                UpdatedAt = null
            },
            new ProductionLot
            {
                Id = 3,
                LotNumber = "LOT-2026-003",
                WorkStationId = 3,
                ProductName = "Cánh Cửa Chống Cháy Công Nghiệp",
                Quantity = 200,
                DefectQuantity = 0,
                Status = "Completed",
                CreatedAt = seedDate,
                UpdatedAt = null
            }
        );
    }
}
