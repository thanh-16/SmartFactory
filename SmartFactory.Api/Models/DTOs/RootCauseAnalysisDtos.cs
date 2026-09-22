using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SmartFactory.Api.Models.DTOs;

/// <summary>
/// Bước phân tích trong chuỗi 5-Why
/// </summary>
public class FiveWhyItem
{
    [Range(1, 5)]
    public int Step { get; set; }

    [Required]
    public string Question { get; set; } = string.Empty;

    [Required]
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// Danh mục phân loại nguyên nhân theo mô hình Ishikawa 6M
/// </summary>
public static class IshikawaCategoryNames
{
    public const string Man = "Man";                 // Con người
    public const string Machine = "Machine";         // Máy móc / Thiết bị
    public const string Material = "Material";       // Vật tư / Nguyên liệu
    public const string Method = "Method";           // Phương pháp / Quy trình
    public const string Measurement = "Measurement"; // Đo lường / Hiệu chuẩn
    public const string Environment = "Environment"; // Môi trường nhà xưởng
}

/// <summary>
/// Kết quả tổng hợp phân tích nguyên nhân gốc rễ chuyên sâu
/// </summary>
public class RootCauseAnalysisResult
{
    public List<FiveWhyItem> FiveWhys { get; set; } = new();

    public Dictionary<string, List<string>> IshikawaCategories { get; set; } = new()
    {
        [IshikawaCategoryNames.Man] = new List<string>(),
        [IshikawaCategoryNames.Machine] = new List<string>(),
        [IshikawaCategoryNames.Material] = new List<string>(),
        [IshikawaCategoryNames.Method] = new List<string>(),
        [IshikawaCategoryNames.Measurement] = new List<string>(),
        [IshikawaCategoryNames.Environment] = new List<string>()
    };

    [Required]
    public string PrimaryRootCause { get; set; } = string.Empty;

    [Required]
    public string RecommendedCorrectiveAction { get; set; } = string.Empty;

    [Required]
    public string RecommendedPreventiveAction { get; set; } = string.Empty;

    public float Confidence { get; set; } = 0.95f;

    public string EngineProvider { get; set; } = "Gemini-1.5-Flash"; // or "Deterministic-Heuristic-6M"

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}
