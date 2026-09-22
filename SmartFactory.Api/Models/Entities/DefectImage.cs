namespace SmartFactory.Api.Models.Entities;

public class DefectImage
{
    public int Id { get; set; }
    public int NcrReportId { get; set; }
    public NcrReport? NcrReport { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
