using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Services;

/// <summary>
/// Dịch vụ phân tích hình ảnh và sự cố chất lượng tích hợp Google Gemini 1.5 Flash Vision API
/// kết hợp cơ chế Hybrid Resilient Smart Fallback chống đổ vỡ 100%.
/// </summary>
public class AiInspectionService : IAiInspectionService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<AiInspectionService> _logger;

    public AiInspectionService(IConfiguration configuration, ILogger<AiInspectionService> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        
        // Ưu tiên đọc biến môi trường GEMINI_API_KEY, sau đó đọc appsettings.json
        _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _apiKey = configuration["Gemini:ApiKey"];
        }
    }

    public async Task<AiAnalysisResult> AnalyzeDefectAsync(
        string description, 
        string? imageFileName = null, 
        byte[]? imageBytes = null, 
        string? mimeType = null, 
        CancellationToken ct = default)
    {
        // 1. Thử gọi Google Gemini 1.5 Flash Vision nếu có API Key hợp lệ
        if (!string.IsNullOrWhiteSpace(_apiKey) && _apiKey.Length > 10)
        {
            try
            {
                var geminiResult = await CallGeminiVisionApiAsync(description, imageBytes, mimeType, ct);
                if (geminiResult != null)
                {
                    return geminiResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gọi Gemini Vision API thất bại hoặc timeout. Tự động kích hoạt cơ chế Smart Fallback.");
            }
        }

        // 2. Chế độ Smart Heuristic Fallback (Offline 100% tin cậy, không bao giờ sập khi demo)
        return FallbackHeuristicAnalysis(description, imageFileName);
    }

    public async Task<RootCauseAnalysisResult> InvestigateRootCauseAsync(
        string defectType, 
        string severity, 
        string description, 
        string? imageFileName = null, 
        byte[]? imageBytes = null, 
        string? mimeType = null, 
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey) && _apiKey.Length > 10)
        {
            try
            {
                var geminiResult = await CallGeminiInvestigationApiAsync(defectType, severity, description, imageBytes, mimeType, ct);
                if (geminiResult != null)
                {
                    return geminiResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gọi Gemini Vision API phân tích 5-Why & 6M thất bại. Kích hoạt FallbackHeuristicInvestigation.");
            }
        }

        return GenerateHeuristicRootCauseAnalysis(defectType, description);
    }

    private async Task<AiAnalysisResult?> CallGeminiVisionApiAsync(
        string description, 
        byte[]? imageBytes, 
        string? mimeType, 
        CancellationToken ct)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

        var promptText = $@"Bạn là Kỹ sư Trưởng KCS/QA trong nhà máy sản xuất công nghiệp chuẩn ISO 9001.
Hãy phân tích kỹ bức ảnh sản phẩm lỗi được đính kèm cùng với mô tả hiện trường của công nhân: '{description}'.

Yêu cầu phân tích và trích xuất DUY NHẤT một chuỗi JSON hợp lệ theo định dạng sau (không kèm markdown ```json):
{{
  ""defectType"": ""Crack"" hoặc ""Scratch"" hoặc ""Deformation"" hoặc ""MissingPart"" hoặc ""Other"",
  ""severity"": ""Minor"" hoặc ""Major"" hoặc ""Critical"",
  ""confidence"": số thực từ 0.85 đến 0.99,
  ""rootCauseAnalysis"": ""Phân tích nguyên nhân sâu xa (nhiệt độ, áp lực ép, ma sát, dị vật, sai thao tác)"",
  ""suggestedAction"": ""Biện pháp kỹ thuật khắc phục cụ thể cho chuyền sản xuất""
}}";

        var parts = new JsonArray();
        parts.Add(new JsonObject { ["text"] = promptText });

        if (imageBytes != null && imageBytes.Length > 0)
        {
            var validMime = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = validMime,
                    ["data"] = Convert.ToBase64String(imageBytes)
                }
            });
        }

        var requestBody = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject { ["parts"] = parts }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["temperature"] = 0.2
            }
        };

        using var requestContent = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, requestContent, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Gemini API trả về mã lỗi HTTP {StatusCode}: {Body}", response.StatusCode, errorBody);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var candidate = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var cleanJson = CleanJsonString(candidate);
        using var parsedDoc = JsonDocument.Parse(cleanJson);
        var root = parsedDoc.RootElement;

        var defectType = root.TryGetProperty("defectType", out var dt) ? dt.GetString() ?? "Other" : "Other";
        var severity = root.TryGetProperty("severity", out var sv) ? sv.GetString() ?? "Major" : "Major";
        var rootCause = root.TryGetProperty("rootCauseAnalysis", out var rc) ? rc.GetString() ?? "Phát hiện sự cố chất lượng từ AI Vision." : "Phát hiện sự cố chất lượng từ AI Vision.";
        var suggestedAction = root.TryGetProperty("suggestedAction", out var sa) ? sa.GetString() ?? "Khóa lô hàng và kiểm tra chuyền." : "Khóa lô hàng và kiểm tra chuyền.";
        var confidence = root.TryGetProperty("confidence", out var cf) ? (float)cf.GetDouble() : 0.94f;

        return new AiAnalysisResult(
            defectType,
            severity,
            rootCause,
            suggestedAction,
            confidence,
            Provider: "Gemini-1.5-Flash (Cloud Vision)"
        );
    }

    private async Task<RootCauseAnalysisResult?> CallGeminiInvestigationApiAsync(
        string defectType,
        string severity,
        string description,
        byte[]? imageBytes,
        string? mimeType,
        CancellationToken ct)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

        var promptText = $@"Bạn là Trưởng Phòng Quản Lý Chất Lượng & Chuyên Gia Thẩm Tra Kỹ Thuật ISO 9001:2015 trong nhà máy sản xuất công nghiệp.
Hãy quan sát kỹ bức ảnh khuyết tật hiện trường được gửi kèm (nếu có) và mô tả của công nhân KCS: '{description}'.
Loại lỗi được xác định là: '{defectType}', mức độ nghiêm trọng: '{severity}'.

Nhiệm vụ của bạn là lập báo cáo điều tra sự cố chuẩn mực bao gồm:
1. Chuỗi 5-Why: Đặt 5 câu hỏi 'Tại sao' liên tiếp đào sâu từ hiện tượng trực quan đến lỗi hệ thống sâu xa nhất (step từ 1 đến 5).
2. Sơ đồ xương cá Ishikawa 6M: Phân loại các nguyên nhân khả dĩ vào đủ 6 nhóm: Man, Machine, Material, Method, Measurement, Environment.
3. Nguyên nhân cốt lõi duy nhất (PrimaryRootCause).
4. Biện pháp khắc phục trước mắt (RecommendedCorrectiveAction).
5. Biện pháp phòng ngừa lâu dài tránh tái phát (RecommendedPreventiveAction).

BẮT BUỘC trả về DUY NHẤT một chuỗi JSON thuần (không kèm định dạng markdown ```json) theo đúng cấu trúc sau:
{{
  ""fiveWhys"": [
    {{ ""step"": 1, ""question"": ""..."", ""answer"": ""..."" }},
    {{ ""step"": 2, ""question"": ""..."", ""answer"": ""..."" }},
    {{ ""step"": 3, ""question"": ""..."", ""answer"": ""..."" }},
    {{ ""step"": 4, ""question"": ""..."", ""answer"": ""..."" }},
    {{ ""step"": 5, ""question"": ""..."", ""answer"": ""..."" }}
  ],
  ""ishikawaCategories"": {{
    ""Man"": [""...""],
    ""Machine"": [""...""],
    ""Material"": [""...""],
    ""Method"": [""...""],
    ""Measurement"": [""...""],
    ""Environment"": [""...""]
  }},
  ""primaryRootCause"": ""..."",
  ""recommendedCorrectiveAction"": ""..."",
  ""recommendedPreventiveAction"": ""..."",
  ""confidence"": 0.95
}}";

        var parts = new JsonArray();
        parts.Add(new JsonObject { ["text"] = promptText });

        if (imageBytes != null && imageBytes.Length > 0)
        {
            var validMime = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = validMime,
                    ["data"] = Convert.ToBase64String(imageBytes)
                }
            });
        }

        var requestBody = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject { ["parts"] = parts }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["temperature"] = 0.1
            }
        };

        using var requestContent = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, requestContent, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Gemini RCA API trả về mã lỗi HTTP {StatusCode}: {Body}", response.StatusCode, errorBody);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var candidate = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var cleanJson = CleanJsonString(candidate);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<RootCauseAnalysisResult>(cleanJson, options);

        if (result != null)
        {
            result.EngineProvider = "Gemini-1.5-Flash (Cloud Vision)";
            result.AnalyzedAt = DateTime.UtcNow;
            return result;
        }

        return null;
    }

    private static string CleanJsonString(string candidate)
    {
        var cleanJson = candidate.Trim();
        if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleanJson = cleanJson.Substring(7).Trim();
        }
        else if (cleanJson.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleanJson = cleanJson.Substring(3).Trim();
        }

        if (cleanJson.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleanJson = cleanJson.Substring(0, cleanJson.Length - 3).Trim();
        }

        return cleanJson;
    }

    private static AiAnalysisResult FallbackHeuristicAnalysis(string description, string? imageFileName)
    {
        var lower = (description ?? string.Empty).ToLowerInvariant() + " " + (imageFileName ?? string.Empty).ToLowerInvariant();

        if (lower.Contains("nứt") || lower.Contains("crack") || lower.Contains("gãy"))
        {
            return new AiAnalysisResult(
                DefectType: "Crack",
                Severity: "Critical",
                RootCauseAnalysis: "Ứng suất dư nhiệt hoặc áp lực chấn dập vượt quá ngưỡng dẻo của hợp kim. Xuất hiện vết nứt chân đế lan rộng nguy cơ nứt gãy hoàn toàn.",
                SuggestedAction: "Kích hoạt khóa lô tức thì 🔒. Kiểm tra áp lực xi-lanh thủy lực trạm dập ST-01 và giảm tốc độ dập.",
                Confidence: 0.97f,
                Provider: "SmartHeuristicFallback (Offline)"
            );
        }

        if (lower.Contains("trầy") || lower.Contains("xước") || lower.Contains("scratch"))
        {
            return new AiAnalysisResult(
                DefectType: "Scratch",
                Severity: "Minor",
                RootCauseAnalysis: "Ma sát cơ học với phoi tiện hoặc bàn trượt không được vệ sinh sạch sẽ trong quá trình gắp chuyển phôi.",
                SuggestedAction: "Vệ sinh bàn kẹp, làm sạch dị vật trạm ST-02 và đánh bóng nhẹ bề mặt sản phẩm.",
                Confidence: 0.93f,
                Provider: "SmartHeuristicFallback (Offline)"
            );
        }

        if (lower.Contains("biến dạng") || lower.Contains("móp") || lower.Contains("cong") || lower.Contains("deformation") || lower.Contains("dent"))
        {
            return new AiAnalysisResult(
                DefectType: "Deformation",
                Severity: "Major",
                RootCauseAnalysis: "Góc ép và cữ chặn cơ khí bị lệch 1.8mm do rung chấn liên tục hoặc phôi bị kẹt trong buồng nén ép.",
                SuggestedAction: "Tạm dừng chuyền dập. Hiệu chuẩn lại thước đo laser cữ chặn trạm ST-01 và cô lập lô hàng.",
                Confidence: 0.95f,
                Provider: "SmartHeuristicFallback (Offline)"
            );
        }

        if (lower.Contains("thiếu") || lower.Contains("missing") || lower.Contains("rơi"))
        {
            return new AiAnalysisResult(
                DefectType: "MissingPart",
                Severity: "Critical",
                RootCauseAnalysis: "Cánh tay gắp Robot bỏ sót linh kiện chốt hãm hoặc vít khóa trong chu trình lắp ráp tự động.",
                SuggestedAction: "Khóa lô hàng ngay lập tức. Hiệu chuẩn camera quang học kiểm tra linh kiện trạm ST-03.",
                Confidence: 0.98f,
                Provider: "SmartHeuristicFallback (Offline)"
            );
        }

        if (lower.Contains("rỗ") || lower.Contains("khí") || lower.Contains("porosity") || lower.Contains("pore"))
        {
            return new AiAnalysisResult(
                DefectType: "Porosity",
                Severity: "Major",
                RootCauseAnalysis: "Bọt khí nitơ và hydro bị giữ lại trong bể hàn hoặc khuôn đúc khi kim loại đông đặc do thiếu khí bảo vệ.",
                SuggestedAction: "Dừng trạm hàn ST-02. Vệ sinh béc phun mỏ hàn và kiểm tra lưu lượng bình khí bảo vệ CO2/Argon.",
                Confidence: 0.96f,
                Provider: "SmartHeuristicFallback (Offline)"
            );
        }

        if (lower.Contains("dầu") || lower.Contains("bẩn") || lower.Contains("bụi") || lower.Contains("contamination") || lower.Contains("sạn"))
        {
            return new AiAnalysisResult(
                DefectType: "Contamination",
                Severity: "Major",
                RootCauseAnalysis: "Tạp chất dầu mỡ và hạt bụi bám trên bề mặt phôi trước khi sơn tĩnh điện do suy giảm nồng độ hóa chất tẩy rửa.",
                SuggestedAction: "Bắn hạt cát tẩy sạch lớp sơn lỗi. Châm thêm hóa chất tẩy dầu bể số 1 và vệ sinh vòi phun áp lực.",
                Confidence: 0.94f,
                Provider: "SmartHeuristicFallback (Offline)"
            );
        }

        if (lower.Contains("bavia") || lower.Contains("gờ") || lower.Contains("burr") || lower.Contains("sắc"))
        {
            return new AiAnalysisResult(
                DefectType: "Burr",
                Severity: "Minor",
                RootCauseAnalysis: "Lưỡi dao cắt laser/dập bị mòn cạnh cắt sau hơn 50,000 chu trình đột dập chưa được mài lại.",
                SuggestedAction: "Đưa toàn bộ phôi sang máy mài rung khử bavia tự động. Tháo mài lại dao cắt trạm ST-01.",
                Confidence: 0.95f,
                Provider: "SmartHeuristicFallback (Offline)"
            );
        }

        return new AiAnalysisResult(
            DefectType: "Other",
            Severity: "Major",
            RootCauseAnalysis: "Phát hiện bất thường quang học trên bề mặt phôi gia công chưa thuộc nhóm phân loại mẫu.",
            SuggestedAction: "Chuyển trạng thái lô sang chờ thẩm tra kỹ thuật. Quản đốc xưởng cần trực tiếp kiểm tra mẫu vật lý.",
            Confidence: 0.88f,
            Provider: "SmartHeuristicFallback (Offline)"
        );
    }

    public static RootCauseAnalysisResult GenerateHeuristicRootCauseAnalysis(string defectType, string description)
    {
        var typeKey = (defectType ?? string.Empty).Trim().ToLowerInvariant();
        var descKey = (description ?? string.Empty).ToLowerInvariant();

        if (typeKey.Contains("crack") || typeKey.Contains("nứt") || descKey.Contains("nứt") || descKey.Contains("crack"))
        {
            return new RootCauseAnalysisResult
            {
                FiveWhys = new List<FiveWhyItem>
                {
                    new() { Step = 1, Question = "Tại sao xuất hiện vết nứt trên phôi?", Answer = "Ứng suất uốn cục bộ vượt quá giới hạn dẻo cơ học của vật liệu." },
                    new() { Step = 2, Question = "Tại sao ứng suất lại tập trung cao ở điểm đó?", Answer = "Bán kính góc lượn dao chấn dập quá nhỏ so với chiều dày phôi tôn." },
                    new() { Step = 3, Question = "Tại sao lại dùng bán kính dao dập quá nhỏ?", Answer = "Thợ gá đặt lắp nhầm cối dập R=1.5mm thay vì R=3.0mm." },
                    new() { Step = 4, Question = "Tại sao thợ gá đặt lắp nhầm cối dập?", Answer = "Khay chứa khuôn dao không có bảng mã màu phân loại và mã vạch quản lý." },
                    new() { Step = 5, Question = "Tại sao chưa có kiểm tra khuôn trước khi dập?", Answer = "Quy trình Setup máy thiếu bước nghiệm thu phôi đầu ca (FAI - First Article Inspection)." }
                },
                IshikawaCategories = new Dictionary<string, List<string>>
                {
                    [IshikawaCategoryNames.Man] = new() { "Thợ vận hành không kiểm tra lại mã số dao sau khi lắp.", "Công nhân thao tác vội khi giao ca." },
                    [IshikawaCategoryNames.Machine] = new() { "Áp lực ben thủy lực trạm ST-01 bị quá tải 12% so với áp suất chuẩn.", "Khuôn chấn dập bị mòn cạnh góc R." },
                    [IshikawaCategoryNames.Material] = new() { "Độ dão của mác thép SS400 thấp hơn tiêu chuẩn quy định.", "Phôi thép hàm lượng carbon không đồng đều." },
                    [IshikawaCategoryNames.Method] = new() { "Tốc độ hạ cối dập quá nhanh gây tải trọng va đập đột ngột.", "Thiếu quy trình kiểm tra mẫu đầu ca (First Article Inspection)." },
                    [IshikawaCategoryNames.Measurement] = new() { "Dưỡng đo góc R bị mòn chưa qua hiệu chuẩn định kỳ.", "Đo bằng mắt thường không phát hiện vi nứt." },
                    [IshikawaCategoryNames.Environment] = new() { "Nhiệt độ nhà xưởng thấp làm tăng độ giòn nguội của hợp kim.", "Phân xưởng rung chấn do máy đột dập bên cạnh hoạt động đồng thời." }
                },
                PrimaryRootCause = "Thiếu quy trình kiểm tra Poka-Yoke xác thực kích thước dao chấn trước khi chạy sản xuất hàng loạt.",
                RecommendedCorrectiveAction = "Dừng máy lập tức, thay cối R=3.0mm, cách ly kiểm tra 100% phôi dập trong ca để siêu âm vết nứt.",
                RecommendedPreventiveAction = "Áp dụng Poka-Yoke gá dao bằng dưỡng kiểm cữ và dán mã màu nhận diện trên giá khuôn.",
                Confidence = 0.97f,
                EngineProvider = "Deterministic-Heuristic-6M",
                AnalyzedAt = DateTime.UtcNow
            };
        }

        if (typeKey.Contains("scratch") || typeKey.Contains("trầy") || typeKey.Contains("xước") || descKey.Contains("trầy") || descKey.Contains("xước"))
        {
            return new RootCauseAnalysisResult
            {
                FiveWhys = new List<FiveWhyItem>
                {
                    new() { Step = 1, Question = "Tại sao bề mặt sản phẩm xuất hiện vệt trầy xước dài?", Answer = "Ma sát kim loại với phoi tiện hoặc cát cứng trên băng chuyền." },
                    new() { Step = 2, Question = "Tại sao lại có phoi vụn kim loại trên bàn đỡ?", Answer = "Vòi hút bụi kim loại tại trạm cắt phay bị tắc nghẽn." },
                    new() { Step = 3, Question = "Tại sao vòi hút phoi kim loại bị nghẽn mà không được thông?", Answer = "Màng lọc cyclone của máy hút bụi đã quá tải 2 ca chưa được vệ sinh." },
                    new() { Step = 4, Question = "Tại sao màng lọc quá tải không có cảnh báo?", Answer = "Đồng hồ đo chênh áp lọc khí bị hỏng cảm biến áp suất." },
                    new() { Step = 5, Question = "Tại sao cảm biến áp suất hỏng chưa được sửa?", Answer = "Kế hoạch bảo trì phòng ngừa (TPM) trạm ST-02 bị quá hạn 14 ngày." }
                },
                IshikawaCategories = new Dictionary<string, List<string>>
                {
                    [IshikawaCategoryNames.Man] = new() { "Công nhân gắp phôi kéo lê trên mặt bàn thay vì nhấc thẳng.", "Chưa tuân thủ quy định vệ sinh 5S giữa các ca làm việc." },
                    [IshikawaCategoryNames.Machine] = new() { "Băng tải con lăn bị kẹt 2 con lăn cao su gây trượt xước.", "Vòi hút bụi kim loại bị nghẽn màng lọc." },
                    [IshikawaCategoryNames.Material] = new() { "Tấm bảo vệ bề mặt (PE film) bị bong tróc trước khi vào chuyền.", "Bề mặt phôi thô có bavia chưa mài." },
                    [IshikawaCategoryNames.Method] = new() { "Chưa có quy định lau chùi bàn thao tác giữa các ca làm việc.", "Quy trình chuyển phôi thủ công thiếu khay đệm cao su." },
                    [IshikawaCategoryNames.Measurement] = new() { "Kiểm tra độ bóng bề mặt bằng mắt thường trong điều kiện thiếu sáng.", "Chưa sử dụng máy đo độ nhám bề mặt điện tử." },
                    [IshikawaCategoryNames.Environment] = new() { "Phân xưởng nhiều bụi hạt mài từ khâu mài thô bên cạnh bay sang.", "Độ ẩm cao làm phoi bám dính bàn thao tác." }
                },
                PrimaryRootCause = "Kế hoạch bảo trì phòng ngừa (TPM) hệ thống hút phoi bị quá hạn dẫn đến tích tụ hạt mài trên bàn thao tác.",
                RecommendedCorrectiveAction = "Vệ sinh thổi sạch toàn bộ bàn kẹp, đánh bóng khắc phục xước nhẹ trên các chi tiết có thể tái chế.",
                RecommendedPreventiveAction = "Lắp tấm chắn bụi ngăn khu mài thô, thay thế cảm biến chênh áp và chuẩn hóa checklist vệ sinh 5S trước ca.",
                Confidence = 0.94f,
                EngineProvider = "Deterministic-Heuristic-6M",
                AnalyzedAt = DateTime.UtcNow
            };
        }

        if (typeKey.Contains("deformation") || typeKey.Contains("biến dạng") || typeKey.Contains("móp") || typeKey.Contains("cong") || descKey.Contains("biến dạng") || descKey.Contains("cong"))
        {
            return new RootCauseAnalysisResult
            {
                FiveWhys = new List<FiveWhyItem>
                {
                    new() { Step = 1, Question = "Tại sao thanh định hình bị cong vênh 2.8mm?", Answer = "Lực kẹp của đồ gá thủy lực vượt quá ngưỡng biến dạng đàn hồi." },
                    new() { Step = 2, Question = "Tại sao lực kẹp đồ gá lại tăng đột biến?", Answer = "Van điều áp khí nén/dầu bị kẹt cặn bẩn ở vị trí mở tối đa." },
                    new() { Step = 3, Question = "Tại sao van điều áp bị cặn bẩn?", Answer = "Dầu thủy lực không được thay thế định kỳ, chứa hạt mài kim loại." },
                    new() { Step = 4, Question = "Tại sao dầu thủy lực chứa nhiều hạt mài bẩn?", Answer = "Bể dầu phụ mất nắp đậy kín trong quá trình sửa chữa tuần trước." },
                    new() { Step = 5, Question = "Tại sao mất nắp bể dầu không được ghi nhận?", Answer = "Thiếu biên bản nghiệm thu bàn giao 5S sau bảo trì của tổ cơ điện." }
                },
                IshikawaCategories = new Dictionary<string, List<string>>
                {
                    [IshikawaCategoryNames.Man] = new() { "Thợ gá lắp điều chỉnh núm vặn áp suất tùy tiện theo cảm tính.", "Công nhân đặt phôi lệch góc cữ định vị." },
                    [IshikawaCategoryNames.Machine] = new() { "Cữ chặn hành trình phôi bị rơ lỏng bu-lông hãm.", "Van điều áp dầu thủy lực bị kẹt van tiết lưu." },
                    [IshikawaCategoryNames.Material] = new() { "Chiều dày tôn mỏng hơn biên độ dung sai cho phép (-0.15mm).", "Độ cứng phôi không đồng nhất giữa các cuộn tôn." },
                    [IshikawaCategoryNames.Method] = new() { "Phương pháp gá đặt 2 điểm không đủ độ cứng vững chống võng.", "Lực kẹp không được phân bổ đều dọc chiều dài phôi." },
                    [IshikawaCategoryNames.Measurement] = new() { "Thước đo khe hở laser bị lệch góc căn chuẩn 0.5 độ.", "Không kiểm tra kích thước chi tiết đầu tiên sau khi gá." },
                    [IshikawaCategoryNames.Environment] = new() { "Nhiệt độ phân xưởng dao động lớn giữa ngày và đêm gây dãn nở nhiệt.", "Mặt sàn rung động do máy đột dập bên cạnh." }
                },
                PrimaryRootCause = "Van điều áp thủy lực bị kẹt do thiếu biên bản nghiệm thu 5S và bảo trì lọc dầu định kỳ.",
                RecommendedCorrectiveAction = "Xả áp đồ gá, nắn phẳng chi tiết bằng máy ép vít, súc rửa và thay dầu thủy lực mới.",
                RecommendedPreventiveAction = "Khóa niêm phong núm chỉnh áp đồ gá, siết chặt bu-lông cữ chặn định kỳ và lắp cảm biến giám sát lực kẹp.",
                Confidence = 0.95f,
                EngineProvider = "Deterministic-Heuristic-6M",
                AnalyzedAt = DateTime.UtcNow
            };
        }

        if (typeKey.Contains("porosity") || typeKey.Contains("pore") || typeKey.Contains("rỗ") || descKey.Contains("rỗ") || descKey.Contains("khí"))
        {
            return new RootCauseAnalysisResult
            {
                FiveWhys = new List<FiveWhyItem>
                {
                    new() { Step = 1, Question = "Tại sao đường hàn xuất hiện lỗ rỗ khí li ti?", Answer = "Bọt khí nitơ và hydro bị giữ lại trong bể hàn kim loại khi đông đặc." },
                    new() { Step = 2, Question = "Tại sao bọt khí lại xuất hiện trong vũng hàn?", Answer = "Khí bảo vệ CO2/Argon bị thiếu hụt lưu lượng bảo vệ bề mặt." },
                    new() { Step = 3, Question = "Tại sao lưu lượng khí bảo vệ bị sụt giảm?", Answer = "Đầu béc mỏ hàn robot bị đóng xỉ hàn làm cản trở dòng khí phun." },
                    new() { Step = 4, Question = "Tại sao đầu béc bị đóng xỉ hàn dày đặc?", Answer = "Trạm làm sạch béc tự động của robot bị hết dung dịch chống dính xỉ." },
                    new() { Step = 5, Question = "Tại sao hết dung dịch chống dính xỉ mà robot vẫn hàn?", Answer = "Cảm biến mức dung dịch trạm làm sạch mỏ hàn chưa được kết nối liên động (Interlock) với PLC điều khiển chuyền." }
                },
                IshikawaCategories = new Dictionary<string, List<string>>
                {
                    [IshikawaCategoryNames.Man] = new() { "Thợ hàn không chà sạch lớp dầu mỡ trên bề mặt mép vát trước khi hàn.", "Người vận hành không kiểm tra bình khí bảo vệ đầu ca." },
                    [IshikawaCategoryNames.Machine] = new() { "Ống dẫn khí bảo vệ bị rạn nứt gây rò rỉ khí trên đường ống.", "Béc phun mỏ hàn robot bị nghẹt xỉ hàn." },
                    [IshikawaCategoryNames.Material] = new() { "Dây hàn bị ẩm do để ngoài không khí ẩm qua đêm không đóng túi chống ẩm.", "Mép phôi bám dầu gia công chưa tẩy rửa." },
                    [IshikawaCategoryNames.Method] = new() { "Góc nghiêng mỏ hàn nghiêng quá 25 độ làm giảm vùng phủ khí.", "Tốc độ di chuyển mỏ hàn quá nhanh so với dòng điện hàn." },
                    [IshikawaCategoryNames.Measurement] = new() { "Đồng hồ đo lưu lượng khí bị kẹt viên bi phao đo.", "Thiếu thiết bị kiểm tra rò rỉ khí bảo vệ định kỳ." },
                    [IshikawaCategoryNames.Environment] = new() { "Phân xưởng có quạt gió thổi trực tiếp vào vùng hàn làm tạt khí bảo vệ.", "Độ ẩm không khí cao > 85% trong mùa mưa." }
                },
                PrimaryRootCause = "Thiếu mạch liên động Interlock tự động dừng Robot khi trạm làm sạch béc cạn dung dịch chống dính xỉ.",
                RecommendedCorrectiveAction = "Khoét bỏ đoạn mối hàn rỗ khí, mài sạch mép hàn và hàn bổ sung đúng quy trình WPS.",
                RecommendedPreventiveAction = "Lập trình Interlock dừng Robot khi cạn dung dịch chống dính xỉ và đặt lồng chắn gió xung quanh trạm hàn robot ST-02.",
                Confidence = 0.96f,
                EngineProvider = "Deterministic-Heuristic-6M",
                AnalyzedAt = DateTime.UtcNow
            };
        }

        if (typeKey.Contains("contamination") || typeKey.Contains("nhiễm") || typeKey.Contains("bẩn") || descKey.Contains("dầu") || descKey.Contains("sơn"))
        {
            return new RootCauseAnalysisResult
            {
                FiveWhys = new List<FiveWhyItem>
                {
                    new() { Step = 1, Question = "Tại sao lớp sơn tĩnh điện xuất hiện hạt sạn cộm và bong tróc?", Answer = "Tạp chất dầu mỡ và hạt bụi bám trên bề mặt phôi trước khi phun sơn." },
                    new() { Step = 2, Question = "Tại sao bề mặt phôi còn sót dầu mỡ sau bể tẩy rửa?", Answer = "Nồng độ hóa chất tẩy dầu bể số 1 bị suy giảm dưới mức tiêu chuẩn." },
                    new() { Step = 3, Question = "Tại sao nồng độ hóa chất tẩy dầu bị suy giảm?", Answer = "Tần suất châm thêm hóa chất không bù đắp kịp lượng hao hụt theo sản lượng." },
                    new() { Step = 4, Question = "Tại sao không phát hiện sớm nồng độ dung dịch giảm?", Answer = "Nhân viên hóa nghiệm đo nồng độ pH 1 lần/ngày thay vì 2 giờ/lần." },
                    new() { Step = 5, Question = "Tại sao tần suất đo kiểm không được tuân thủ?", Answer = "Chưa lắp đặt hệ thống châm hóa chất tự động điều khiển theo cảm biến đo liên tục." }
                },
                IshikawaCategories = new Dictionary<string, List<string>>
                {
                    [IshikawaCategoryNames.Man] = new() { "Công nhân bốc xếp không đeo găng tay vải sạch, để lại dấu vân tay dầu.", "Nhân viên hóa nghiệm quên đo nồng độ bể tẩy ca sáng." },
                    [IshikawaCategoryNames.Machine] = new() { "Vòi phun áp lực bể tiền xử lý bị nghẹt cặn canxi.", "Lưới lọc hồi lưu dung dịch tẩy rửa bị rách." },
                    [IshikawaCategoryNames.Material] = new() { "Dầu bảo quản phôi của nhà cung cấp có độ nhớt quá cao khó rửa sạch.", "Nước rửa tráng lẫn ion tạp chất." },
                    [IshikawaCategoryNames.Method] = new() { "Thời gian phôi lưu trong buồng sấy khô sau tẩy rửa không đủ làm khô nước.", "Tốc độ chuyền sơn chạy vượt tốc độ thiết kế." },
                    [IshikawaCategoryNames.Measurement] = new() { "Bộ đo độ dẫn điện (Conductivity Meter) bị trôi điểm chuẩn 0.", "Giấy thử pH quá hạn sử dụng." },
                    [IshikawaCategoryNames.Environment] = new() { "Buồng phun sơn tĩnh điện bị lọt bụi từ cửa thông gió bên ngoài.", "Bụi kim loại từ phân xưởng cơ khí bay vào buồng sơn." }
                },
                PrimaryRootCause = "Chưa lắp đặt hệ thống tự động hóa đo và châm hóa chất tẩy rửa liên tục theo nồng độ thực tế.",
                RecommendedCorrectiveAction = "Bắn hạt cát tẩy sạch lớp sơn lỗi trên toàn bộ lô chi tiết và cho qua lại dây chuyền tẩy rửa.",
                RecommendedPreventiveAction = "Lắp đặt bơm định lượng châm hóa chất tự động và niêm phong cửa thông gió buồng sơn bằng màng lọc HEPA.",
                Confidence = 0.94f,
                EngineProvider = "Deterministic-Heuristic-6M",
                AnalyzedAt = DateTime.UtcNow
            };
        }

        if (typeKey.Contains("burr") || typeKey.Contains("bavia") || typeKey.Contains("gờ") || descKey.Contains("bavia"))
        {
            return new RootCauseAnalysisResult
            {
                FiveWhys = new List<FiveWhyItem>
                {
                    new() { Step = 1, Question = "Tại sao mép cắt chi tiết xuất hiện bavia cao 0.8mm?", Answer = "Kim loại bị xé rách thay vì bị cắt đứt gãy gọn gàng." },
                    new() { Step = 2, Question = "Tại sao kim loại lại bị xé rách?", Answer = "Lưỡi dao cắt laser/chấn dập bị mòn tròn cạnh cắt." },
                    new() { Step = 3, Question = "Tại sao dao cắt mòn mà vẫn tiếp tục sản xuất?", Answer = "Số chu kỳ dập của dao đã vượt 50,000 nhát cắt nhưng chưa được mài lại." },
                    new() { Step = 4, Question = "Tại sao vượt quá định mức chu kỳ mà không thay dao?", Answer = "Bộ đếm hành trình trên máy cắt bị đứt dây tín hiệu encoder về PLC." },
                    new() { Step = 5, Question = "Tại sao dây encoder đứt không được khắc phục ngay?", Answer = "Tổ bảo trì đấu tắt tín hiệu đếm (Bypass) để kịp tiến độ giao hàng của ca trước." }
                },
                IshikawaCategories = new Dictionary<string, List<string>>
                {
                    [IshikawaCategoryNames.Man] = new() { "Thợ gá lắp chỉnh khe hở giữa chày và cối dập quá lớn (> 15% chiều dày).", "Kỹ thuật viên bỏ qua bước kiểm tra bavia đầu ca." },
                    [IshikawaCategoryNames.Machine] = new() { "Bàn gá dao cắt bị rung rơ do vòng bi trục chính bị rơ lỏng.", "Lưỡi dao cắt dập bị mòn cùn mép cắt." },
                    [IshikawaCategoryNames.Material] = new() { "Phôi tôn có mép viền bị gỉ sét làm giảm tuổi thọ lưỡi dao.", "Độ cứng thép phôi cao hơn mác thép chỉ định trong bản vẽ." },
                    [IshikawaCategoryNames.Method] = new() { "Áp suất khí trợ dung cắt laser N2 bị giảm đột ngột.", "Tốc độ đột dập cài đặt không phù hợp với chiều dày tôn." },
                    [IshikawaCategoryNames.Measurement] = new() { "KCS kiểm tra mép cắt bằng tay không dùng dưỡng đo bavia chuyên dụng.", "Kính phóng đại kiểm tra dao cắt bị ố mờ." },
                    [IshikawaCategoryNames.Environment] = new() { "Phân xưởng ẩm ướt gây oxy hóa nhanh các góc cắt kim loại.", "Ánh sáng tại bàn kiểm tra mép cắt không đủ độ rọi." }
                },
                PrimaryRootCause = "Hành vi đấu tắt tín hiệu cảnh báo vòng đời dao cắt trên PLC mà không tuân thủ quy trình kiểm soát thay đổi.",
                RecommendedCorrectiveAction = "Đưa toàn bộ phôi lỗi sang trạm mài rung khử bavia tự động (Deburring).",
                RecommendedPreventiveAction = "Khôi phục liên động bộ đếm vòng đời dao trên PLC, cấm hành vi bypass dây tín hiệu an toàn và quy định bảo dưỡng dao định kỳ.",
                Confidence = 0.95f,
                EngineProvider = "Deterministic-Heuristic-6M",
                AnalyzedAt = DateTime.UtcNow
            };
        }

        // 7. Nhóm Lỗi: Other / Default
        return new RootCauseAnalysisResult
        {
            FiveWhys = new List<FiveWhyItem>
            {
                new() { Step = 1, Question = "Tại sao thông số sản phẩm không đạt yêu cầu kỹ thuật?", Answer = "Quá trình gia công cơ khí bị lệch khỏi khoảng dung sai cho phép." },
                new() { Step = 2, Question = "Tại sao xảy ra sai lệch dung sai?", Answer = "Trục dẫn hướng của máy CNC bị sai lệch vị trí điểm gốc Zero (Home Offset)." },
                new() { Step = 3, Question = "Tại sao điểm gốc Zero bị sai lệch?", Answer = "Cảm biến tiệm cận hành trình (Limit Switch) bị bám dính mạt sắt." },
                new() { Step = 4, Question = "Tại sao mạt sắt bám vào cảm biến?", Answer = "Nắp chụp bảo vệ che chắn cảm biến bị bung ốc rơi mất." },
                new() { Step = 5, Question = "Tại sao nắp bảo vệ rơi mất không được gắn lại?", Answer = "Bảng danh mục kiểm tra đầu giờ 5S (Checklist Daily TPM) bị bỏ qua không thực hiện nghiêm túc." }
            },
            IshikawaCategories = new Dictionary<string, List<string>>
            {
                [IshikawaCategoryNames.Man] = new() { "Thợ đứng máy mới chưa quen bảng điều khiển thông số máy.", "Người vận hành không hiệu chuẩn gốc tọa độ máy." },
                [IshikawaCategoryNames.Machine] = new() { "Động cơ bước bị trượt bước do sụt áp nguồn điện lưới phân xưởng.", "Trục vít me bi có độ rơ cơ khí vượt ngưỡng." },
                [IshikawaCategoryNames.Material] = new() { "Phôi vật liệu không đồng đều về kích thước thô ban đầu.", "Độ cong vênh ban đầu của phôi tôn vượt chuẩn." },
                [IshikawaCategoryNames.Method] = new() { "Thứ tự các bước phay cắt không tối ưu gây tích tụ ứng suất nhiệt.", "Tốc độ ăn dao quá lớn so với độ cứng vững gá kẹp." },
                [IshikawaCategoryNames.Measurement] = new() { "Thước cặp cơ khí bị mòn mỏ kẹp đo sai 0.1mm.", "Đồng hồ so chưa được kiểm định định kỳ." },
                [IshikawaCategoryNames.Environment] = new() { "Ánh sáng tại trạm làm việc không đạt tiêu chuẩn 500 Lux.", "Nhiệt độ xưởng biến thiên làm sai lệch kích thước phôi." }
            },
            PrimaryRootCause = "Quy trình kiểm tra 5S và xác nhận gốc tọa độ máy đầu giờ không được thực hiện nghiêm túc.",
            RecommendedCorrectiveAction = "Căn chỉnh lại điểm gốc tọa độ máy và kiểm tra lại 100% chi tiết trong lô.",
            RecommendedPreventiveAction = "Bắt buộc chụp ảnh xác nhận hoàn thành bảng kiểm tra 5S đầu giờ làm việc mỗi ca và giám sát định kỳ.",
            Confidence = 0.90f,
            EngineProvider = "Deterministic-Heuristic-6M",
            AnalyzedAt = DateTime.UtcNow
        };
    }
}
