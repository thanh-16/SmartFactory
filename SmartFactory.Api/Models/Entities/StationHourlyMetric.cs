using System;

namespace SmartFactory.Api.Models.Entities;

/// <summary>
/// Thực thể lưu trữ số liệu thống kê SPC chuỗi thời gian theo từng trạm máy
/// </summary>
public class StationHourlyMetric
{
    public int Id { get; set; }

    public int WorkStationId { get; set; }
    public WorkStation? WorkStation { get; set; }

    /// <summary>
    /// Thời điểm bắt đầu của cửa sổ trượt thời gian (UTC)
    /// </summary>
    public DateTime WindowStartTime { get; set; }

    /// <summary>
    /// Thời điểm kết thúc của cửa sổ trượt (UTC)
    /// </summary>
    public DateTime WindowEndTime { get; set; }

    /// <summary>
    /// Tổng số lượng sản phẩm/lượt đã kiểm tra trong cửa sổ
    /// </summary>
    public int TotalInspected { get; set; }

    /// <summary>
    /// Tổng số lượng khuyết tật phát hiện trong cửa sổ
    /// </summary>
    public int TotalDefects { get; set; }

    /// <summary>
    /// Tỷ lệ khuyết tật p = TotalDefects / TotalInspected (0.0 đến 1.0)
    /// </summary>
    public double DefectRate { get; set; }

    /// <summary>
    /// Độ lệch chuẩn mẫu (Sample Standard Deviation - Sigma)
    /// </summary>
    public double StdDev { get; set; }

    /// <summary>
    /// Giá trị trung bình trượt làm mịn hàm mũ (EWMA Filter Value)
    /// </summary>
    public double MovingAverage { get; set; }

    /// <summary>
    /// Giới hạn kiểm soát trên (Upper Control Limit - UCL)
    /// </summary>
    public double UpperControlLimit { get; set; }

    /// <summary>
    /// Giới hạn kiểm soát dưới (Lower Control Limit - LCL)
    /// </summary>
    public double LowerControlLimit { get; set; }

    /// <summary>
    /// Loại lỗi xuất hiện nhiều nhất trong cửa sổ
    /// </summary>
    public string DominantDefectType { get; set; } = "None";

    /// <summary>
    /// Điểm đánh giá dị thường tổng hợp (0: An toàn đến 100: Cực kỳ nguy hiểm)
    /// </summary>
    public int AnomalyScore { get; set; }

    /// <summary>
    /// Cờ đánh dấu đã phát cảnh báo sớm
    /// </summary>
    public bool IsWarningTriggered { get; set; }

    /// <summary>
    /// Quy tắc SPC bị vi phạm (None, NelsonRule1, NelsonRule2, NelsonRule3)
    /// </summary>
    public string TriggeredRule { get; set; } = "None";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
