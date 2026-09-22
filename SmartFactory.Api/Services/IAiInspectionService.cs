using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Services;

/// <summary>
/// Kết quả phân tích sự cố chất lượng từ AI
/// </summary>
public record AiAnalysisResult(
    string DefectType, 
    string Severity, 
    string RootCauseAnalysis, 
    string SuggestedAction,
    float Confidence = 0.95f,
    string Provider = "Gemini-1.5-Flash"
);

public interface IAiInspectionService
{
    /// <summary>
    /// Phân tích ảnh và mô tả sự cố bằng Google Gemini Vision API kèm cơ chế Fallback thông minh
    /// </summary>
    Task<AiAnalysisResult> AnalyzeDefectAsync(
        string description, 
        string? imageFileName = null, 
        byte[]? imageBytes = null, 
        string? mimeType = null, 
        CancellationToken ct = default);

    /// <summary>
    /// Điều tra nguyên nhân gốc rễ chuyên sâu (5-Why và Ishikawa 6M)
    /// </summary>
    Task<RootCauseAnalysisResult> InvestigateRootCauseAsync(
        string defectType, 
        string severity, 
        string description, 
        string? imageFileName = null, 
        byte[]? imageBytes = null, 
        string? mimeType = null, 
        CancellationToken ct = default);
}
