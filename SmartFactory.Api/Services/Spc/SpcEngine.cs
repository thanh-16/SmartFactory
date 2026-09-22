using System;
using System.Collections.Generic;
using System.Linq;
using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Services;

/// <summary>
/// Thuật toán kiểm soát quá trình thống kê SPC thuần (Nelson Rules, EWMA, Cpk)
/// </summary>
public static class SpcEngine
{
    public const double DefaultLambda = 0.2; // Hệ số làm mịn EWMA
    public const double FallbackUclThreshold = 0.05; // 5% ngưỡng kiểm soát trên mặc định
    public const double DefaultUsl = 0.05; // 5% Upper Specification Limit chuẩn công nghiệp

    /// <summary>
    /// Đánh giá vi phạm bộ 3 quy tắc Nelson Rules chuyên biệt cho dây chuyền sản xuất
    /// </summary>
    public static SpcEvaluationResult EvaluateNelsonRules(
        List<double> recentDefectRates, 
        double historicalMean, 
        double ucl, 
        string dominantDefect = "None")
    {
        var result = new SpcEvaluationResult();
        if (recentDefectRates == null || recentDefectRates.Count == 0)
        {
            return result;
        }

        var latestRate = recentDefectRates.Last();

        // 1. Kiểm tra Nelson Rule 1: Điểm vượt ngưỡng UCL hoặc vượt ngưỡng trần 5%
        var effectiveUcl = ucl > 0 ? ucl : FallbackUclThreshold;
        if (latestRate > effectiveUcl)
        {
            result.HasViolation = true;
            result.ViolatedRule = "NelsonRule1";
            result.RuleDescription = $"Tỷ lệ lỗi hiện tại ({latestRate:P1}) vượt ngưỡng kiểm soát trên UCL ({effectiveUcl:P1}).";
            result.AlertLevel = "Critical";
            result.AnomalyScore = 95;
            result.RootCauseHypothesis = "Đột biến áp lực máy hoặc gãy hỏng cơ cấu định vị phôi.";
            result.RecommendedAction = "Tạm dừng máy kiểm tra lập tức. Cách ly lô hàng đang gia công trên trạm.";
            return result;
        }

        // 2. Kiểm tra Nelson Rule 2: 3 điểm tăng liên tục
        if (recentDefectRates.Count >= 3)
        {
            var p0 = recentDefectRates[^1];
            var p1 = recentDefectRates[^2];
            var p2 = recentDefectRates[^3];

            if (p0 > p1 && p1 > p2 && p0 > historicalMean)
            {
                result.HasViolation = true;
                result.ViolatedRule = "NelsonRule2";
                result.RuleDescription = "3 ca kiểm tra liên tiếp có tỷ lệ khuyết tật tăng dần ngặt (Xu hướng leo thang).";
                result.AlertLevel = "Warning";
                result.AnomalyScore = 75;
                result.RootCauseHypothesis = $"Gia tốc mòn dao cắt hoặc nhiệt độ buồng trạm máy gia tăng bất thường (Loại lỗi: {dominantDefect}).";
                result.RecommendedAction = "Bảo trì viên vệ sinh bàn kẹp, đo bù dao và kiểm tra nhiệt độ dầu làm mát.";
                return result;
            }
        }

        // 3. Kiểm tra Nelson Rule 3: 8 điểm liên tiếp nằm trên đường trung bình
        if (recentDefectRates.Count >= 8)
        {
            var last8 = recentDefectRates.TakeLast(8).ToList();
            if (last8.All(p => p > historicalMean))
            {
                result.HasViolation = true;
                result.ViolatedRule = "NelsonRule3";
                result.RuleDescription = $"8 điểm đo liên tiếp đều nằm trên mức trung bình ({historicalMean:P2}). Trục quá trình bị trôi (Process Shift).";
                result.AlertLevel = "Warning";
                result.AnomalyScore = 80;
                result.RootCauseHypothesis = "Hiện tượng mòn tự nhiên của cữ chặn hoặc suy hao áp lực xi lanh dẫn hướng.";
                result.RecommendedAction = "Hiệu chuẩn lại thước đo laser điểm Zero và siết lại bu-lông đồ gá.";
                return result;
            }
        }

        return result;
    }

    /// <summary>
    /// Bộ lọc trung bình trượt làm mịn hàm mũ EWMA
    /// </summary>
    public static double CalculateEwma(double previousEwma, double currentVal, double lambda = DefaultLambda)
    {
        if (double.IsNaN(previousEwma) || double.IsInfinity(previousEwma)) previousEwma = currentVal;
        if (double.IsNaN(currentVal) || double.IsInfinity(currentVal)) return previousEwma;

        var result = (lambda * currentVal) + ((1.0 - lambda) * previousEwma);
        return double.IsNaN(result) || double.IsInfinity(result) ? currentVal : result;
    }

    /// <summary>
    /// Tính toán giới hạn kiểm soát nhị thức p-chart (UCL, LCL, Sigma)
    /// </summary>
    public static (double UCL, double LCL, double Sigma) CalculateBinomialControlLimits(double pMean, double averageSampleSize)
    {
        if (averageSampleSize <= 0 || pMean <= 0)
        {
            return (FallbackUclThreshold, 0.0, 0.0);
        }

        var clampedP = Math.Clamp(pMean, 0.0, 1.0);
        var variance = (clampedP * (1.0 - clampedP)) / averageSampleSize;
        var sigma = variance > 0 ? Math.Sqrt(variance) : 0.0;

        var ucl = Math.Min(1.0, clampedP + (3.0 * sigma));
        var lcl = Math.Max(0.0, clampedP - (3.0 * sigma));

        // Nếu ucl quá nhỏ do dữ liệu mẫu quá ít, dùng ngưỡng tối thiểu 0.03
        if (ucl < 0.01) ucl = FallbackUclThreshold;

        return (ucl, lcl, sigma);
    }

    /// <summary>
    /// Tính chỉ số năng lực quá trình Cpk đối với tỷ lệ phế phẩm (phân phối tiệm cận)
    /// </summary>
    public static double CalculateCpk(double pMean, double sigma, double usl = DefaultUsl)
    {
        if (sigma <= 0.000001)
        {
            return pMean <= usl ? 2.0 : 0.5;
        }

        var cpk = (usl - pMean) / (3.0 * sigma);
        if (double.IsNaN(cpk) || double.IsInfinity(cpk))
        {
            return 1.33;
        }

        return Math.Round(Math.Max(0.0, cpk), 2);
    }
}
