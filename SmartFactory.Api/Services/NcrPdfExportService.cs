using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using SmartFactory.Api.Data;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Pdf;

namespace SmartFactory.Api.Services;

public class NcrPdfExportService : INcrPdfExportService
{
    private readonly FactoryDbContext _context;
    private readonly IWebHostEnvironment _env;

    public NcrPdfExportService(FactoryDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    public async Task<byte[]> GenerateNcrPdfAsync(int ncrReportId, CancellationToken ct = default)
    {
        var ncr = await _context.NcrReports
            .Include(r => r.ProductionLot)
            .Include(r => r.WorkStation)
            .Include(r => r.ReportedByUser)
            .Include(r => r.DefectImages)
            .Include(r => r.Decisions)
                .ThenInclude(d => d.ApprovedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ncrReportId, ct);

        if (ncr == null)
        {
            throw new NotFoundException($"NCR Report with ID {ncrReportId} not found.");
        }

        byte[]? imageBytes = null;
        var firstImage = ncr.DefectImages.FirstOrDefault();
        if (firstImage != null && !string.IsNullOrWhiteSpace(firstImage.ImageUrl))
        {
            try
            {
                var cleanRelative = firstImage.ImageUrl.TrimStart('/', '\\');
                var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var physicalPath = Path.Combine(webRoot, cleanRelative);

                if (File.Exists(physicalPath))
                {
                    using var fileStream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var memStream = new MemoryStream();
                    await fileStream.CopyToAsync(memStream, ct);
                    imageBytes = memStream.ToArray();
                }
            }
            catch
            {
                imageBytes = null;
            }
        }

        var document = new NcrIsoDocument(ncr, imageBytes);
        using var pdfStream = new MemoryStream();
        document.GeneratePdf(pdfStream);
        return pdfStream.ToArray();
    }
}
