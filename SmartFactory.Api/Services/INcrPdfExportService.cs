using System.Threading;
using System.Threading.Tasks;

namespace SmartFactory.Api.Services;

/// <summary>
/// Dịch vụ kết xuất biên bản sự cố chất lượng (NCR) theo định dạng PDF chuẩn ISO 9001:2015
/// </summary>
public interface INcrPdfExportService
{
    /// <summary>
    /// Tạo file PDF dạng mảng nhị phân từ dữ liệu biên bản NCR
    /// </summary>
    /// <param name="ncrReportId">Mã ID biên bản NCR</param>
    /// <param name="ct">CancellationToken hủy tác vụ</param>
    /// <returns>Mảng byte của file PDF hoàn chỉnh</returns>
    Task<byte[]> GenerateNcrPdfAsync(int ncrReportId, CancellationToken ct = default);
}
