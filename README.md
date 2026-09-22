# 🏭 KCS-SmartFactory OS

> **Hệ Thống Số Hóa Quy Trình Kiểm Tra Chất Lượng (KCS) & Xử Lý Sự Cố Nhà Máy Bằng AI**  
> *Đồ án tốt nghiệp / Portfolio chuyên nghiệp — Công nghệ: .NET 10, ASP.NET Core, EF Core SQLite, SignalR Realtime, Tailwind CSS & Docker.*  
> **Kho lưu trữ GitHub:** [https://github.com/thanh-16/SmartFactory](https://github.com/thanh-16/SmartFactory)

---

## 📌 1. Bối Cảnh & Bài Toán Thực Tế

Trong mọi nhà máy sản xuất (cơ khí, nhựa định hình, điện tử, dệt may...), bộ phận KCS (Kiểm Soát Chất Lượng) đối mặt với 3 nỗi đau kinh niên:
1. **Lập biên bản sự cố (NCR) bằng giấy tờ/sổ tay:** Mất 2-4 tiếng mới tới tay Quản đốc $\rightarrow$ Hàng lỗi đã đóng thùng hoặc xuất kho cho khách.
2. **Không phân tích được nguyên nhân gốc rễ:** Không biết lỗi do nhiệt độ khuôn, công nhân sai thao tác hay phôi thép kém chất lượng $\rightarrow$ Lỗi lặp đi lặp lại.
3. **Thiếu liên thông tức thì giữa KCS - Quản Đốc - Kho:** Lô hàng lỗi không bị khóa tức thời $\rightarrow$ Thủ kho vẫn bấm xuất nhầm hàng $\rightarrow$ Bị phạt hợp đồng & mất uy tín.

**KCS-SmartFactory OS giải quyết triệt để vấn đề này:**
- Nhân viên KCS dùng điện thoại chụp ảnh lỗi $\rightarrow$ **AI tự động phân tích & lập biên bản NCR**.
- Hệ thống **tự động khóa lô hàng (Atomic Lot Locking)** trong cơ sở dữ liệu ngay lập tức.
- Bảng điều khiển Quản đốc xưởng **rung chuông & chớp đèn Andon cảnh báo Realtime (SignalR)** mà không cần F5 tải lại trang.
- Quản đốc bấm 1 nút phê duyệt: `Tái chế (Rework)`, `Hủy bỏ (Scrap)`, `Trả NCC (Return)` để mở khóa hoặc xử lý lô hàng.

---

## 🌟 2. Các Tính Năng Nổi Bật (Key Features)

### ① AI Phân Tích & Soạn Biên Bản Tự Động (NCR Auto-Draft)
- Trích xuất loại khuyết tật: *Crack (Nứt nẻ), Scratch (Trầy xước), Deformation (Biến dạng), MissingPart (Thiếu chi tiết)...*
- Đánh giá mức độ nghiêm trọng: `Minor`, `Major`, `Critical` kèm độ tin cậy AI (`AiConfidence`).
- Cơ chế **Fail-Safe Timeout Fallback**: Nếu mạng gián đoạn hoặc AI timeout quá 8 giây, hệ thống tự động gán `Severity = Major` để tự động khóa lô an toàn, không bao giờ để lọt hàng lỗi.

### ② Khóa Lô Hàng Nguyên Tử (Atomic Lot Locking)
- Toàn bộ thao tác tạo biên bản và khóa lô hàng được bọc trong **Database Transaction**.
- Cơ chế **Two-Phase Resource Cleanup**: Nếu commit DB thất bại, hệ thống tự động dọn sạch file ảnh mồ côi trên đĩa cứng.

### ③ Cảnh Báo Quản Đốc Realtime (SignalR Andon Beacon Alert)
- Kết nối WebSocket hai chiều qua `/hubs/factory`.
- Khi có lỗi Major/Critical: Bảng điều khiển Quản đốc phát âm thanh còi báo động Andon (Web Audio API), thanh Beacon đỏ chớp nháy và hiển thị thẻ khẩn cấp.

### ④ Phân Tích Pareto 80/20 & Giám Sát KPI
- Tự động thống kê nhóm lỗi chiếm 80% tần suất xuất hiện.
- Vẽ biểu đồ trực quan động với Chart.js.

### ⑤ Giao Diện Web Đẳng Cấp Nhúng Trong `wwwroot` (All-In-One)
- Chạy trực tiếp từ ASP.NET Core, không cần cài Node.js hay build npm phức tạp.
- Hỗ trợ cả giao diện **KCS Hiện Trường (Mobile View)** và **Trung Tâm Quản Đốc (Desktop View)**.

---

## 🏗️ 3. Kiến Trúc Hệ Thống & Tech Stack

```
SmartFactory/
├── SmartFactory.Api/                  # ASP.NET Core Web API (.NET 10)
│   ├── Controllers/                   # RESTful API Endpoints (RFC 7807)
│   ├── Services/                      # Tầng nghiệp vụ nguyên tử & AI
│   ├── Repositories/                  # EF Core Data Access Layer
│   ├── Models/                        # Entities & Immutable Record DTOs
│   ├── Hubs/                          # SignalR Realtime Hub (/hubs/factory)
│   ├── Middleware/                    # Exception Handling Middleware
│   └── wwwroot/                       # Web UI trực quan (Tailwind CSS, Chart.js)
├── SmartFactory.Tests/                # Dự án kiểm thử tự động xUnit (50/50 Passed)
├── Dockerfile                         # Multi-stage container build (.NET 10)
├── docker-compose.yml                 # 1-Click deployment container
└── SmartFactory.slnx                  # Solution cấu hình hiện đại
```

| Thành phần | Công nghệ sử dụng |
| :--- | :--- |
| **Backend Framework** | ASP.NET Core Web API (.NET 10, C# 13) |
| **Cơ sở dữ liệu** | SQLite WAL Mode (`PRAGMA journal_mode = 'wal'`), EF Core 10 |
| **Realtime Engine** | ASP.NET Core SignalR (WebSockets) |
| **Bảo mật File Upload** | Kiểm tra Magic Bytes nhị phân (JPEG, PNG, WEBP), chống Path Traversal |
| **Frontend UI** | HTML5, Tailwind CSS, Lucide Icons, Chart.js, SignalR JS Client |
| **Testing** | xUnit, FluentAssertions, WebApplicationFactory (50 tests pass 100%) |
| **Container & Cloud** | Docker, docker-compose (sẵn sàng deploy Cloud Run / Internet) |

---

## 🚀 4. Hướng Dẫn Cài Đặt & Chạy (3 Cách Đơn Giản)

### Cách 1: Chạy Trực Tiếp Bằng .NET CLI (Khuyên Dùng)

```bash
# 1. Clone repository
git clone https://github.com/thanh-16/SmartFactory.git
cd SmartFactory

# 2. Khởi chạy hệ thống (tự động tạo DB và nạp Seed Data)
dotnet run --project SmartFactory.Api
```

👉 Mở trình duyệt truy cập: **`http://localhost:5000`** hoặc **`https://localhost:5001`**  
*(Giao diện Web UI trực quan và API Swagger sẽ sẵn sàng phục vụ ngay lập tức)*

---

### Cách 2: Chạy Bằng Docker Compose (1-Click Container)

```bash
# Khởi động toàn bộ ứng dụng trong container
docker compose up -d
```

👉 Mở trình duyệt truy cập: **`http://localhost:5000`**  
Kiểm tra sức khỏe container: **`http://localhost:5000/health`**

---

### Cách 3: Chạy Toàn Bộ Bộ Kiểm Thử (Unit & Integration Tests)

```bash
dotnet test
```
*Kết quả: 50/50 tests Passed (100%), kiểm chứng toàn diện từ Magic Bytes, Race Condition đến SignalR.*

---

## 🎬 5. Kịch Bản Trình Chiếu Demo Phỏng Vấn (3 Phút WOW)

| Thời Gian | Thiết Bị / Màn Hình | Hành Động Trình Diễn |
| :--- | :--- | :--- |
| **0:00 - 0:30** | Laptop (Quản đốc) | Mở tab **"Trung Tâm Quản Đốc"**: Giới thiệu các trạm máy đang chạy (`ST-01`, `ST-02`), lô hàng `LOT-2026-001` đang ở trạng thái xanh `InProgress`, biểu đồ Pareto 80/20. |
| **0:30 - 1:15** | Điện thoại (KCS) | Chuyển sang tab **"Trạm KCS Hiện Trường"**: Chọn Lô `LOT-2026-001` $\rightarrow$ Bấm chọn ảnh nứt gãy chân đế kim loại $\rightarrow$ Bấm nút **"AI Phân Tích & Báo Lỗi"**. |
| **1:15 - 1:45** | **Khoảnh khắc WOW** | Màn hình Quản đốc tự động **rung chuông Andon**, thanh beacon đỏ nhấp nháy, Lô `LOT-2026-001` tức thì chuyển sang màu đỏ **`LOCKED 🔒`**. Thủ kho nhìn thấy sẽ không thể xuất nhầm. |
| **1:45 - 2:30** | Laptop (Quản đốc) | Quản đốc mở modal xem ảnh lỗi phóng to và phân tích nguyên nhân gốc rễ của AI $\rightarrow$ Bấm **"Chấp nhận Tái chế (Rework)"** $\rightarrow$ Lô hàng tự động được giải phóng về trạng thái xanh. |
| **2:30 - 3:00** | Laptop (Giám đốc) | Mở báo cáo Pareto xem cập nhật số liệu lỗi theo thời gian thực và nhật ký Audit Trail. |

---

## 👥 6. Đội Ngũ Phát Triển Multi-Agent

Dự án được điều phối và hoàn thiện thông qua quy trình phát triển đa tác nhân tự trị:
- **`[👑 LEADER]`**: Chief Project Director & Orchestrator điều phối toàn diện.
- **`[📐 PLAN]`**: System Architect thiết kế ERD 6 bảng và API Contracts.
- **`[💻 CODE]`**: Fullstack & Platform Engineer thi công Backend .NET 10, Web UI và Docker.
- **`[🧪 TEST]`**: Head of QA xây dựng 50 bài test xUnit dập phá các kịch bản biên.
- **`[🔍 REVIEW]`**: Security & Performance Auditor thẩm định Zero Placeholder và tối ưu SQLite WAL.

---

## 📄 Bản Quyền & Giấy Phép
Dự án được phát hành mã nguồn mở phục vụ mục đích học tập, nghiên cứu và phỏng vấn kỹ thuật.
Mọi thắc mắc vui lòng đóng góp Pull Request hoặc Issue tại [GitHub Repository](https://github.com/thanh-16/SmartFactory).
