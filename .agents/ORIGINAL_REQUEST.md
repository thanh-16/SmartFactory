# Original User Request

## 2026-09-22T19:13:52Z

Triển khai và hoàn thiện bộ kiểm thử tự động toàn diện cho hệ thống KCS-SmartFactory OS (.NET 10, EF Core SQLite), đảm bảo dập tắt toàn bộ lỗi biên, kiểm định bảo mật tải file, tính nguyên tử của giao dịch khóa lô hàng và độ chính xác phân tích Pareto với 100% bài test vượt qua.

Working directory: d:/Vibe coding/SmartFactory
Integrity mode: development

## Requirements

### R1. Khởi tạo & Củng Cố Test Project (SmartFactory.Tests)
Khởi tạo và cấu hình hoàn chỉnh project kiểm thử `SmartFactory.Tests` sử dụng xUnit, FluentAssertions, Moq, Microsoft.EntityFrameworkCore.InMemory và NetArchTest.Rules. Khắc phục dứt điểm mọi lỗi biên dịch hiện hữu giữa các lớp kiểm thử và API DTOs/Entities để `dotnet test` thực thi thông suốt.

### R2. Kiểm Thử Bảo Mật Tải File & Cơ Chế Two-Phase Cleanup
Xây dựng bộ kiểm thử biên cho cơ chế lưu trữ và kiểm định file đính kèm:
- Kiểm tra tính hợp lệ Magic Bytes nhị phân: Chấp thuận file hình ảnh chuẩn (JPEG, PNG, WEBP). Chặn đứng và ném lỗi `InvalidFileFormatException` khi phát hiện file thực thi (.exe), file text hoặc script ngụy tạo đổi đuôi file.
- Kiểm tra giới hạn dung lượng: Ném lỗi `PayloadTooLargeException` đối với mọi file có kích thước vượt quá 5MB.
- Kiểm tra cơ chế bù hoàn (Compensating / Two-Phase Cleanup): Xác minh phương thức `DeleteFile` thực sự xóa sạch tệp vật lý trên đĩa khi giao dịch cơ sở dữ liệu bị hủy hoặc gặp lỗi, ngăn chặn hoàn toàn file rác (orphan files).

### R3. Kiểm Thử Nghiệp Vụ Cốt Lõi (Lot Locking, Idempotency & Exception Handling)
Kiểm thử tính toàn vẹn giao dịch và logic kiểm soát chất lượng KCS:
- Khóa lô nguyên tử (Atomic Lot Locking): Khi ghi nhận sự cố có mức độ `Critical` hoặc `Major`, trạng thái của `ProductionLot` phải lập tức chuyển sang `Locked` và tăng số lượng lỗi. Đối với lỗi `Minor`, lô hàng phải duy trì trạng thái `InProgress`.
- Tính lũy đẳng (Idempotency): Khi Quản đốc cố gắng duyệt lại một biên bản NCR đã ở trạng thái `Resolved`, hệ thống bắt buộc phải từ chối và ném lỗi `ConflictException` (HTTP 409 Conflict).
- Xử lý ngoại lệ chuẩn: Truy vấn hoặc tạo kiểm định trên lô hàng không tồn tại phải ném `NotFoundException` (HTTP 404 Not Found).
- Mở khóa lô hàng: Quyết định `Rework` hợp lệ của Quản đốc phải mở khóa lô hàng từ `Locked` về lại `InProgress` và chuyển biên bản NCR sang `Resolved`.

### R4. Kiểm Thử Báo Cáo Phân Tích Pareto
Kiểm thử thuật toán thống kê lỗi phân xưởng:
- Tỷ lệ tích lũy (Cumulative Percentage) của nhóm phân loại lỗi cuối cùng trong biểu đồ Pareto phải đạt chính xác 100.0%.
- Thứ tự phân loại lỗi phải được sắp xếp giảm dần theo tần suất xuất hiện trước khi tính tỷ lệ phần trăm tích lũy.

### R5. Kiểm Thử Ranh Giới Kiến Trúc (Automated Architecture Testing)
Sử dụng `NetArchTest.Rules` để thiết lập các bài test kiến trúc tự động:
- Đảm bảo các ranh giới tầng kiến trúc không bị vi phạm (Domain/Entities không phụ thuộc Controllers/Services; Services không phụ thuộc trực tiếp Controllers).
- Đảm bảo toàn bộ Controllers kế thừa từ `ControllerBase` và các DTOs tuân thủ tính bất biến hoặc đóng gói.

## Acceptance Criteria

### An Toàn & Bảo Mật File Upload
- [ ] File JPEG/PNG/WEBP hợp lệ vượt qua kiểm tra và trả về relative URL.
- [ ] File giả mạo (MZ header, script, text đổi đuôi sang .jpg/.png) bị từ chối với `InvalidFileFormatException`.
- [ ] File có kích thước > 5MB bị từ chối với `PayloadTooLargeException`.
- [ ] Thao tác dọn dẹp xóa file thực sự xóa tệp vật lý khỏi thư mục lưu trữ mà không ảnh hưởng tới các file ngoài thư mục cho phép (chống Path Traversal).

### Nghiệp Vụ Lô Hàng & Biên Bản NCR
- [ ] Báo cáo lỗi Critical/Major chuyển trạng thái lô hàng thành "Locked" và cập nhật DefectQuantity.
- [ ] Báo cáo lỗi Minor giữ nguyên trạng thái lô hàng "InProgress".
- [ ] Quản đốc duyệt quyết định trên NCR đã "Resolved" ném `ConflictException` (409 Conflict).
- [ ] Thao tác trên lô hàng không tồn tại ném `NotFoundException` (404 Not Found).
- [ ] Quyết định "Rework" mở khóa lô từ "Locked" sang "InProgress" và chuyển NCR sang "Resolved".

### Báo Cáo Pareto & Kiến Trúc
- [ ] Biểu đồ Pareto có tỷ lệ phần trăm tích lũy của hạng mục cuối cùng đạt đúng 100.0%.
- [ ] Kiến trúc hệ thống tuân thủ các quy tắc ranh giới phân tầng của NetArchTest.

### Tiêu Chuẩn Thực Thi
- [ ] `dotnet build` hoàn thành với 0 errors.
- [ ] `dotnet test` hoàn thành với 100% tests Passed (0 failed, 0 skipped, không dùng assertions rỗng hoặc pass ảo).

## 2026-09-22T19:21:06Z

Xây dựng hệ thống KCS-SmartFactory OS (.NET 10, ASP.NET Core Web API, EF Core SQLite, SignalR Realtime) số hóa quy trình kiểm tra chất lượng sản phẩm, AI Vision phân tích lỗi, khóa lô hàng nguyên tử và cảnh báo Quản đốc xưởng tức thì.

Working directory: d:/Vibe coding/SmartFactory
Integrity mode: development

## Requirements

### R1. Nền Tảng Dữ Liệu & Thiết Kế Kiến Trúc
Xây dựng cơ sở dữ liệu 6 bảng (WorkStation, ProductionLot, NcrReport, DefectImage, NcrDecision, AppUser) với SQLite WAL Mode (PRAGMA journal_mode = 'wal'; busy_timeout = 5000;), cấu hình Fluent API, quan hệ chặt chẽ và Seed Data nhà máy ban đầu.

### R2. Tầng Nghiệp Vụ Nguyên Tử & Khóa Lô Hàng (Lot Locking)
Xây dựng Service Layer với giao dịch nguyên tử (IDbContextTransaction):
- Xác thực Magic Bytes nhị phân (JPEG, PNG, WEBP), chặn file quá 5MB và chống Path Traversal.
- Phân tích ảnh lỗi AI kèm cơ chế Fail-Safe Timeout Fallback (tự động chuyển sang Major khi AI timeout để khóa lô an toàn).
- Khóa lô tức thì (Atomic Lot Locking): Tự động đổi trạng thái ProductionLot sang "Locked" khi phát sinh sự cố Major/Critical.
- Dọn dẹp bù trừ (Two-Phase Cleanup): Xóa sạch file ảnh mồ côi trên đĩa cứng nếu Database transaction bị rollback.
- Quản đốc phê duyệt quyết định xử lý (Rework mở khóa, Scrap hủy lô) với kiểm soát Idempotency chống duyệt 2 lần.

### R3. API Endpoints Chuẩn RESTful & RFC 7807
Triển khai toàn bộ Controller tiếp nhận dữ liệu:
- NcrReportsController (POST /api/ncr-reports/inspect nhận multipart/form-data, GET /api/ncr-reports, GET /api/ncr-reports/{id}).
- NcrDecisionsController (POST /api/ncr-decisions).
- ProductionLotsController, WorkStationsController, DashboardController (GET /api/dashboard/summary, GET /api/dashboard/pareto).
- Mọi phản hồi lỗi 4xx, 5xx phải chuẩn hóa theo RFC 7807 ProblemDetails.

### R4. Realtime Andon Alert & Giao Diện Web UI (wwwroot)
Xây dựng SignalR Hub (/hubs/factory) và giao diện Web trực quan nhúng trong wwwroot:
- Giao diện KCS (Mobile View): Chụp/tải ảnh lỗi, nhập ghi chú, gửi phân tích AI và nhận thông báo lô bị khóa.
- Giao diện Quản Đốc (Desktop View): Kết nối SignalR nhận chuông báo động Andon tức thì khi có lỗi, duyệt phương án bằng 1 click, và biểu đồ Pareto 80/20 trực quan.

### R5. Đóng Gói Docker & Kiểm Thử Tự Động Toàn Diện
- Viết Dockerfile multi-stage build cho .NET 10 và docker-compose.yml khởi chạy trọn gói.
- Dựng dự án SmartFactory.Tests (xUnit, FluentAssertions), dập các kịch bản biên (magic bytes giả mạo, file rỗng, dung lượng > 5MB, concurrency lock, lỗi rollback).

## Acceptance Criteria

### Tính Toàn Vẹn Nghiệp Vụ & Realtime
- [ ] Gửi ảnh lỗi Major/Critical khiến lô hàng lập tức chuyển sang trạng thái "Locked".
- [ ] Dashboard Quản đốc nhận cảnh báo SignalR Andon thời gian thực (< 1 giây) không cần tải lại trang.
- [ ] Quản đốc duyệt "Rework" mở khóa lô về trạng thái "Released".
- [ ] Rollback DB xóa sạch file ảnh vật lý trên đĩa cứng, không để lại file mồ côi.

### An Toàn & Bảo Mật
- [ ] Chặn 100% file không khớp Magic Bytes thực tế với mã lỗi HTTP 400.
- [ ] Chặn file upload vượt quá 5MB với HTTP 400/413.
- [ ] Cấm duyệt lại biên bản NCR đã Resolved (trả về HTTP 409 Conflict).

### Kiểm Chứng Kỹ Thuật (Hard Verification Gate)
- [ ] dotnet build hoàn thành với Exit Code = 0, không có warning bảo mật nghiêm trọng.
- [ ] 100% test suites xUnit vượt qua thành công.
- [ ] 100% Zero Placeholder: Không có // TODO, /* rest of code */ hay mã giả lập nửa vời.
- [ ] 100% truy vấn đọc sử dụng .AsNoTracking() chống suy giảm hiệu năng và N+1 query.

## 2026-09-22T19:22:31Z

Triển khai và hoàn thiện toàn diện hệ thống KCS-SmartFactory OS (.NET 10, EF Core SQLite) — bao gồm tầng Service, Controllers, File Upload Security (Magic Bytes & Two-Phase Cleanup), Lot Locking, Pareto Analytics, Global Exception Handling và bộ kiểm thử tự động SmartFactory.Tests — đảm bảo 100% build pass và test pass không có placeholder.

Working directory: d:/Vibe coding/SmartFactory
Integrity mode: development

## Requirements

### R1. Hoàn thiện tầng Service & API Endpoints
Triển khai đầy đủ các Service (Inspection, Decision, Dashboard, FileStorage) và Controllers (InspectionsController, DecisionsController, DashboardController) với đăng ký DI, Global Exception Handling Middleware trả đúng mã HTTP (400, 404, 409, 413) và cấu hình DbContext SQLite trong Program.cs.

### R2. File Upload Security với Magic Bytes & Two-Phase Cleanup
File upload phải kiểm tra Magic Bytes nhị phân đầu file (JPEG, PNG, WEBP), từ chối file giả mạo hoặc file vượt quá 5MB. Tên file lưu trữ phải an toàn (GUID) chống Path Traversal. Cơ chế Two-Phase Cleanup phải xóa file vật lý trên đĩa khi giao dịch DB thất bại.

### R3. Nghiệp vụ Lot Locking, Idempotency & Pareto Analytics
Khóa lô nguyên tử khi NCR có mức Critical/Major, mở khóa khi Rework. Từ chối duyệt lại NCR đã Resolved (ConflictException). Pareto phải có tỷ lệ tích lũy cuối cùng đạt đúng 100.0%. Các truy vấn chỉ đọc phải dùng AsNoTracking.

### R4. Bộ kiểm thử tự động SmartFactory.Tests
Tạo project xUnit với FluentAssertions, Moq, NetArchTest.Rules. Viết Unit Tests cho toàn bộ nghiệp vụ: File Upload (Magic Bytes, size limit, cleanup), Lot Locking (Critical/Major/Minor), Idempotency (ConflictException), Pareto (cumulative 100%) và NetArchTest ranh giới kiến trúc.

## Acceptance Criteria

### Build & Test
- [ ] `dotnet build` hoàn thành với 0 errors.
- [ ] `dotnet test` hoàn thành với 100% tests Passed (0 failed, 0 skipped).
- [ ] 0 dòng code placeholder (`// TODO`, `NotImplementedException`, `/* rest of code */`).

### Bảo Mật File Upload
- [ ] File JPEG/PNG/WEBP hợp lệ được chấp nhận.
- [ ] File giả mạo (exe/script đổi đuôi) bị từ chối với InvalidFileFormatException.
- [ ] File > 5MB bị từ chối với PayloadTooLargeException.
- [ ] File vật lý bị xóa khi DB transaction thất bại (Two-Phase Cleanup).

### Nghiệp Vụ
- [ ] Lỗi Critical/Major chuyển lô sang Locked và tăng DefectQuantity.
- [ ] Lỗi Minor giữ nguyên trạng thái InProgress.
- [ ] Duyệt lại NCR đã Resolved trả về ConflictException (409).
- [ ] Quyết định Rework mở khóa lô về InProgress và NCR sang Resolved.
- [ ] Pareto cumulative percentage cuối cùng đạt đúng 100.0%.

### Hiệu Năng & Kiến Trúc
- [ ] 100% truy vấn chỉ đọc áp dụng AsNoTracking.
- [ ] Thao tác I/O chậm (ghi file) nằm ngoài phạm vi DB Transaction.
- [ ] NetArchTest xác minh ranh giới phân tầng kiến trúc.

