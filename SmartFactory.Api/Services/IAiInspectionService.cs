namespace SmartFactory.Api.Services;

public record AiAnalysisResult(string DefectType, string Severity, string RootCauseAnalysis, string SuggestedAction);

public interface IAiInspectionService
{
    Task<AiAnalysisResult> AnalyzeDefectAsync(string description, string? imageFileName = null, CancellationToken ct = default);
}
