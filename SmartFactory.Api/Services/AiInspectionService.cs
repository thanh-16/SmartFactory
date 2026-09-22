namespace SmartFactory.Api.Services;

public class AiInspectionService : IAiInspectionService
{
    public Task<AiAnalysisResult> AnalyzeDefectAsync(string description, string? imageFileName = null, CancellationToken ct = default)
    {
        var lower = (description ?? string.Empty).ToLowerInvariant();

        string defectType;
        string severity;
        string rootCause;
        string suggestedAction;

        if (lower.Contains("nứt") || lower.Contains("crack"))
        {
            defectType = "Crack";
            severity = "Critical";
            rootCause = "Ứng suất nhiệt hoặc áp lực uốn khuôn vượt quá giới hạn dẻo của vật liệu kim loại.";
            suggestedAction = "Khóa lô hàng ngay lập tức. Kiểm tra áp lực xi-lanh thủy lực và làm nguội khuôn.";
        }
        else if (lower.Contains("trầy") || lower.Contains("xước") || lower.Contains("scratch"))
        {
            defectType = "Scratch";
            severity = "Minor";
            rootCause = "Ma sát với phoi tiện hoặc bàn thao tác có dị vật trong quá trình gắp sản phẩm.";
            suggestedAction = "Vệ sinh bàn kẹp và chổi quét phoi trạm cắt.";
        }
        else if (lower.Contains("biến dạng") || lower.Contains("móp") || lower.Contains("deformation"))
        {
            defectType = "Deformation";
            severity = "Major";
            rootCause = "Sai lệch hành trình gập hoặc phôi bị kẹt trong buồng ép.";
            suggestedAction = "Hiệu chuẩn cảm biến cữ chặn trạm gập chấn và cách ly lô hàng.";
        }
        else
        {
            defectType = "Defect";
            severity = "Major";
            rootCause = "Chưa xác định rõ nguồn gốc từ mô tả ban đầu.";
            suggestedAction = "Yêu cầu giám định chất lượng chuyên sâu từ Kỹ sư xưởng.";
        }

        return Task.FromResult(new AiAnalysisResult(defectType, severity, rootCause, suggestedAction));
    }
}
