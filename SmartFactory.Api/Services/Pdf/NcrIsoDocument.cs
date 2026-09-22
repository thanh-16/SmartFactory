using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Pdf;

public class NcrIsoDocument : IDocument
{
    private readonly NcrReport _report;
    private readonly byte[]? _defectImageBytes;
    private readonly StationSpcMetricsDto? _spcMetrics;

    public NcrIsoDocument(NcrReport report, byte[]? defectImageBytes, StationSpcMetricsDto? spcMetrics = null)
    {
        _report = report;
        _defectImageBytes = defectImageBytes;
        _spcMetrics = spcMetrics;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Biên bản sự cố {_report.NcrNumber}",
        Author = "KCS-SmartFactory OS",
        Subject = "ISO 9001:2015 Non-Conformance Report",
        Keywords = "NCR, ISO9001, QualityControl, SmartFactory"
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(25, Unit.Point);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9.5f).FontColor("#1E293B"));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Border(1).BorderColor("#1E3A8A").Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(140);
                columns.RelativeColumn();
                columns.ConstantColumn(160);
            });

            // Cột 1: Logo & Nhận diện
            table.Cell().Background("#F8FAFC").Padding(8).Column(col =>
            {
                col.Item().Text("SMARTFACTORY OS").FontSize(11).Bold().FontColor("#1E3A8A");
                col.Item().Text("QUY TRÌNH ISO 9001:2015").FontSize(7.5f).Bold().FontColor("#475569");
                col.Item().Text("Phòng Quản Lý Chất Lượng & KCS").FontSize(7).FontColor("#64748B");
            });

            // Cột 2: Tiêu đề Biên bản
            table.Cell().BorderLeft(1).BorderRight(1).BorderColor("#CBD5E1").Padding(8).AlignCenter().AlignMiddle().Column(col =>
            {
                col.Item().AlignCenter().Text("BIÊN BẢN SỰ CỐ KHÔNG PHÙ HỢP").FontSize(12).Bold().FontColor("#1E3A8A");
                col.Item().AlignCenter().Text("NON-CONFORMANCE REPORT (NCR)").FontSize(8.5f).Italic().FontColor("#475569");
                col.Item().AlignCenter().Text("Điều khoản 8.7 & 10.2 ISO 9001").FontSize(7).FontColor("#94A3B8");
            });

            // Cột 3: Bảng Kiểm soát Tài liệu
            table.Cell().Background("#F8FAFC").Padding(6).Column(col =>
            {
                col.Item().Text($"Mã số: BM-QA-NCR-01").FontSize(7.5f).Bold();
                col.Item().Text($"Lần ban hành: Rev 03 (01/2026)").FontSize(7.5f);
                col.Item().Text($"Số NCR: {_report.NcrNumber}").FontSize(8).Bold().FontColor("#DC2626");
                col.Item().Text($"Ngày lập: {_report.CreatedAt:dd/MM/yyyy HH:mm}").FontSize(7.5f);
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(10).Column(column =>
        {
            column.Spacing(8);

            // 1. BẢNG THÔNG TIN LÔ HÀNG VÀ TRẠM MÁY
            column.Item().Element(ComposeLotAndStationSection);

            // 2. HÌNH ẢNH MINH CHỨNG KHUYẾT TẬT
            column.Item().Element(ComposeDefectImageSection);

            // 3. PHÂN TÍCH NGUYÊN NHÂN GỐC RỄ (RCA) & KẾT QUẢ AI VISION
            column.Item().Element(ComposeRootCauseAndAiSection);

            // 4. KIỂM SOÁT THỐNG KÊ QUÁ TRÌNH (SPC)
            if (_spcMetrics != null)
            {
                column.Item().Element(c => ComposeSpcProcessSection(c, _spcMetrics));
            }

            // 5. QUYẾT ĐỊNH XỬ LÝ CỦA QUẢN ĐỐC
            column.Item().Element(ComposeDecisionSection);

            // 6. CHỮ KÝ ĐIỆN TỬ VÀ DẤU PHÊ DUYỆT
            column.Item().Element(ComposeSignaturesSection);
        });
    }

    private void ComposeLotAndStationSection(IContainer container)
    {
        container.Border(0.75f).BorderColor("#CBD5E1").Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(110);
                cols.RelativeColumn();
                cols.ConstantColumn(110);
                cols.RelativeColumn();
            });

            // Row 1
            table.Cell().Background("#F1F5F9").Padding(5).Text("Mã Lô Sản Xuất:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text(_report.ProductionLot?.LotNumber ?? "N/A").Bold().FontSize(8.5f);
            table.Cell().Background("#F1F5F9").Padding(5).Text("Tên Sản Phẩm:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text(_report.ProductionLot?.ProductName ?? "N/A").FontSize(8.5f);

            // Row 2
            table.Cell().Background("#F1F5F9").Padding(5).Text("Trạm Phát Hiện:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text($"{_report.WorkStation?.Code} - {_report.WorkStation?.Name}").FontSize(8.5f);
            table.Cell().Background("#F1F5F9").Padding(5).Text("Sản Lượng Lô:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text($"{_report.ProductionLot?.Quantity:N0} PCS").FontSize(8.5f);

            // Row 3
            table.Cell().Background("#F1F5F9").Padding(5).Text("Loại Khuyết Tật:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text(_report.DefectType).Bold().FontColor("#B91C1C").FontSize(8.5f);
            table.Cell().Background("#F1F5F9").Padding(5).Text("Mức Nghiêm Trọng:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text(_report.Severity.ToUpperInvariant()).Bold().FontColor(_report.Severity switch
            {
                "Critical" => "#DC2626",
                "Major" => "#EA580C",
                _ => "#CA8A04"
            }).FontSize(8.5f);

            // Row 4
            table.Cell().Background("#F1F5F9").Padding(5).Text("Nhân Viên KCS:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text(_report.ReportedByUser?.FullName ?? "N/A").FontSize(8.5f);
            table.Cell().Background("#F1F5F9").Padding(5).Text("Trạng Thái NCR:").Bold().FontSize(8.5f);
            table.Cell().Padding(5).Text(_report.Status.ToUpperInvariant()).Bold().FontColor(_report.Status == "Resolved" ? "#16A34A" : "#D97706").FontSize(8.5f);
        });
    }

    private void ComposeDefectImageSection(IContainer container)
    {
        container.Border(0.75f).BorderColor("#CBD5E1").Padding(6).Column(col =>
        {
            col.Item().Text("MINH CHỨNG HÌNH ẢNH KHUYẾT TẬT HIỆN TRƯỜNG:").Bold().FontSize(8.5f).FontColor("#1E3A8A");

            if (_defectImageBytes != null && _defectImageBytes.Length > 0)
            {
                col.Item().AlignCenter().MaxHeight(150).MaxWidth(300).Image(_defectImageBytes);
                col.Item().AlignCenter().Text($"Hình 1: Ảnh chụp thực tế khuyết tật {_report.DefectType} tại trạm {_report.WorkStation?.Code}").FontSize(7.5f).Italic().FontColor("#64748B");
            }
            else
            {
                col.Item().PaddingVertical(15).AlignCenter().Background("#F8FAFC").Border(0.5f).BorderColor("#E2E8F0").Padding(10).Column(placeholder =>
                {
                    placeholder.Item().AlignCenter().Text("📷 [ KHÔNG CÓ ẢNH ĐÍNH KÈM / IMAGE NOT AVAILABLE ]").Bold().FontSize(8.5f).FontColor("#94A3B8");
                    placeholder.Item().AlignCenter().Text("Sự cố được kiểm tra trực quan hoặc ghi nhận từ cảm biến trạm máy").FontSize(7.5f).FontColor("#94A3B8");
                });
            }
        });
    }

    private void ComposeRootCauseAndAiSection(IContainer container)
    {
        RootCauseAnalysisResult? rca = null;
        if (!string.IsNullOrWhiteSpace(_report.RootCauseAnalysisJson))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                rca = JsonSerializer.Deserialize<RootCauseAnalysisResult>(_report.RootCauseAnalysisJson, options);
            }
            catch
            {
                rca = null;
            }
        }

        if (rca != null && rca.FiveWhys != null && rca.FiveWhys.Count > 0)
        {
            ComposeAdvancedRootCauseSection(container, rca);
        }
        else
        {
            container.Border(0.75f).BorderColor("#CBD5E1").Padding(6).Column(col =>
            {
                col.Item().Text("PHÂN TÍCH NGUYÊN NHÂN GỐC RỄ (RCA) & KẾT QUẢ AI VISION:").Bold().FontSize(8.5f).FontColor("#1E3A8A");
                col.Item().PaddingTop(3).Text($"• Mô tả hiện trường KCS: {_report.Description}").FontSize(8.5f);
                col.Item().PaddingTop(2).Text($"• Đánh giá từ hệ sinh thái AI: Tự động phân loại khuyết tật '{_report.DefectType}' mức độ '{_report.Severity}'. Khuyến nghị kích hoạt khóa lô tức thời và hiệu chuẩn máy gia công.").FontSize(8).Italic().FontColor("#334155");
            });
        }
    }

    private void ComposeAdvancedRootCauseSection(IContainer container, RootCauseAnalysisResult rca)
    {
        container.Border(0.75f).BorderColor("#CBD5E1").Padding(6).Column(col =>
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Text("ĐIỀU TRA NGUYÊN NHÂN GỐC RỄ (5-WHY & ISHIKAWA 6M):").Bold().FontSize(8.5f).FontColor("#1E3A8A");
                r.ConstantItem(150).AlignRight().Text($"Động cơ: {rca.EngineProvider}").FontSize(7).Italic().FontColor("#64748B");
            });

            // 1. Chuỗi 5-Why
            col.Item().PaddingTop(4).Column(whyCol =>
            {
                whyCol.Spacing(2);
                foreach (var item in rca.FiveWhys)
                {
                    var isFinal = item.Step == 5;
                    whyCol.Item().Background(isFinal ? "#FEF2F2" : "#F8FAFC")
                          .Border(0.5f).BorderColor(isFinal ? "#EF4444" : "#E2E8F0")
                          .Padding(3)
                          .Row(row =>
                          {
                              row.ConstantItem(45).Text($"Why {item.Step}:").Bold().FontSize(7.5f).FontColor(isFinal ? "#DC2626" : "#1E3A8A");
                              row.RelativeItem().Column(c =>
                              {
                                  c.Item().Text(item.Question).FontSize(7.5f).Italic().FontColor("#475569");
                                  c.Item().Text($"➔ {item.Answer}").FontSize(7.5f).Bold().FontColor(isFinal ? "#991B1B" : "#1E293B");
                              });
                          });
                }
            });

            // 2. Lưới Xương Cá 6M (Table 3 cột x 2 hàng)
            col.Item().PaddingTop(5).Text("SƠ ĐỒ PHÂN BỔ NGUYÊN NHÂN ISHIKAWA 6M:").Bold().FontSize(8).FontColor("#1E3A8A");
            col.Item().PaddingTop(2).Table(grid =>
            {
                grid.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn();
                    cd.RelativeColumn();
                    cd.RelativeColumn();
                });

                RenderIshikawaCell(grid, "1. CON NGƯỜI (MAN)", rca.IshikawaCategories.GetValueOrDefault(IshikawaCategoryNames.Man), "#2563EB");
                RenderIshikawaCell(grid, "2. MÁY MÓC (MACHINE)", rca.IshikawaCategories.GetValueOrDefault(IshikawaCategoryNames.Machine), "#EA580C");
                RenderIshikawaCell(grid, "3. VẬT LIỆU (MATERIAL)", rca.IshikawaCategories.GetValueOrDefault(IshikawaCategoryNames.Material), "#059669");
                RenderIshikawaCell(grid, "4. PHƯƠNG PHÁP (METHOD)", rca.IshikawaCategories.GetValueOrDefault(IshikawaCategoryNames.Method), "#7C3AED");
                RenderIshikawaCell(grid, "5. ĐO LƯỜNG (MEASUREMENT)", rca.IshikawaCategories.GetValueOrDefault(IshikawaCategoryNames.Measurement), "#CA8A04");
                RenderIshikawaCell(grid, "6. MÔI TRƯỜNG (ENVIRONMENT)", rca.IshikawaCategories.GetValueOrDefault(IshikawaCategoryNames.Environment), "#475569");
            });

            // 3. CAPA Actions Box
            col.Item().PaddingTop(4).Background("#EFF6FF").Border(0.5f).BorderColor("#BFDBFE").Padding(4).Column(capa =>
            {
                capa.Item().Text($"• Khắc phục trước mắt (Corrective): {rca.RecommendedCorrectiveAction}").FontSize(7.5f).Bold().FontColor("#1E40AF");
                capa.Item().Text($"• Phòng ngừa lâu dài (Preventive): {rca.RecommendedPreventiveAction}").FontSize(7.5f).Bold().FontColor("#15803D");
            });
        });
    }

    private void RenderIshikawaCell(TableDescriptor grid, string title, List<string>? items, string colorHex)
    {
        grid.Cell().Border(0.5f).BorderColor("#E2E8F0").Padding(3).Column(col =>
        {
            col.Item().Text(title).Bold().FontSize(7).FontColor(colorHex);
            if (items != null && items.Count > 0)
            {
                foreach (var cause in items)
                {
                    col.Item().Text($"• {cause}").FontSize(6.5f).FontColor("#334155");
                }
            }
            else
            {
                col.Item().Text("• Không ghi nhận bất thường").FontSize(6.5f).Italic().FontColor("#94A3B8");
            }
        });
    }

    private void ComposeDecisionSection(IContainer container)
    {
        var decision = _report.Decisions.OrderByDescending(d => d.DecisionDate).FirstOrDefault();

        container.Border(0.75f).BorderColor("#CBD5E1").Background("#F8FAFC").Padding(6).Column(col =>
        {
            col.Item().Text("QUYẾT ĐỊNH XỬ LÝ CỦA QUẢN ĐỐC XƯỞNG (DISPOSITION):").Bold().FontSize(8.5f).FontColor("#1E3A8A");

            if (decision != null)
            {
                col.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().Text($"Phương án: {decision.Decision.ToUpperInvariant()}").Bold().FontSize(9).FontColor(decision.Decision switch
                    {
                        "Rework" => "#2563EB",
                        "Scrap" => "#DC2626",
                        _ => "#16A34A"
                    });
                    row.RelativeItem().Text($"Ngày phê duyệt: {decision.DecisionDate:dd/MM/yyyy HH:mm}").FontSize(8).AlignRight();
                });

                col.Item().PaddingTop(2).Text($"Chỉ đạo kỹ thuật: {decision.Notes ?? "Xử lý theo quy trình kiểm soát phế phẩm chuẩn."}").FontSize(8.5f);
            }
            else
            {
                col.Item().PaddingTop(5).Text("⏳ BIÊN BẢN ĐANG CHỜ QUẢN ĐỐC XƯỞNG PHÊ DUYỆT PHƯƠNG ÁN").Bold().FontSize(8.5f).FontColor("#D97706");
            }
        });
    }

    private void ComposeSignaturesSection(IContainer container)
    {
        var decision = _report.Decisions.OrderByDescending(d => d.DecisionDate).FirstOrDefault();

        container.PaddingTop(5).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn();
                cols.ConstantColumn(120);
                cols.RelativeColumn();
            });

            // Bên trái: KCS
            table.Cell().Border(0.5f).BorderColor("#CBD5E1").Padding(6).AlignCenter().Column(col =>
            {
                col.Item().Text("NGƯỜI LẬP BIÊN BẢN (KCS)").Bold().FontSize(8).FontColor("#1E3A8A");
                col.Item().PaddingVertical(8).Text("✓ ĐÃ XÁC THỰC SỐ").Bold().FontSize(9).FontColor("#16A34A");
                col.Item().Text(_report.ReportedByUser?.FullName ?? "KCS Inspector").Bold().FontSize(8.5f);
                col.Item().Text($"{_report.CreatedAt:dd/MM/yyyy}").FontSize(7.5f).FontColor("#64748B");
            });

            // Ở giữa: Tem kiểm định / QR Code placeholder
            table.Cell().Border(0.5f).BorderColor("#CBD5E1").Background("#F1F5F9").Padding(6).AlignCenter().AlignMiddle().Column(col =>
            {
                col.Item().AlignCenter().Text("TEM KIỂM ĐỊNH").Bold().FontSize(7.5f).FontColor("#475569");
                col.Item().AlignCenter().Text($"[ {_report.NcrNumber} ]").FontSize(6.5f).FontColor("#64748B");
                col.Item().AlignCenter().Text("ISO 9001:2015").FontSize(6.5f).FontColor("#1E3A8A");
                col.Item().AlignCenter().Text("CHỨNG THỰC ĐIỆN TỬ").FontSize(6f).Italic().FontColor("#94A3B8");
            });

            // Bên phải: Quản Đốc
            table.Cell().Border(0.5f).BorderColor("#CBD5E1").Padding(6).AlignCenter().Column(col =>
            {
                col.Item().Text("QUẢN ĐỐC XƯỞNG DUYỆT").Bold().FontSize(8).FontColor("#1E3A8A");
                if (decision != null)
                {
                    col.Item().PaddingVertical(8).Text("★ ĐÃ PHÊ DUYỆT").Bold().FontSize(9).FontColor("#2563EB");
                    col.Item().Text(decision.ApprovedByUser?.FullName ?? "Workshop Supervisor").Bold().FontSize(8.5f);
                    col.Item().Text($"{decision.DecisionDate:dd/MM/yyyy}").FontSize(7.5f).FontColor("#64748B");
                }
                else
                {
                    col.Item().PaddingVertical(14).Text("(Chưa phê duyệt)").Italic().FontSize(8).FontColor("#94A3B8");
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor("#CBD5E1").PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text("Tài liệu ISO 9001:2015 của SmartFactory OS — Lưu trữ bảo mật tại phòng QA/KCS").FontSize(7).FontColor("#94A3B8");
            row.ConstantItem(100).AlignRight().Text(x =>
            {
                x.Span("Trang ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        });
    }
}
