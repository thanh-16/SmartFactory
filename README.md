# 🏭 KCS-SmartFactory OS

> **Hệ thống Số Hóa Quy Trình Kiểm Tra Chất Lượng (KCS) & Xử Lý Sự Cố Nhà Máy Bằng AI**

---

## Mô Tả Ngắn Gọn

Hệ thống giúp **số hóa toàn bộ quy trình kiểm tra chất lượng sản phẩm (KCS)** trong nhà máy sản xuất:
- Nhân viên KCS chụp ảnh sản phẩm lỗi → **AI tự động soạn biên bản sự cố (NCR)** kèm phân tích nguyên nhân gốc rễ.
- Hệ thống **tự động khóa lô hàng** bị lỗi, ngăn xuất kho nhầm cho khách hàng.
- Quản đốc xưởng **nhận cảnh báo tức thì (Realtime)** và ra quyết định xử lý chỉ bằng 1 nút bấm.

---

## Bài Toán Thực Tế

Trong mọi nhà máy sản xuất tại Việt Nam (cơ khí, nhựa, may mặc, bao bì, linh kiện...),
bộ phận KCS phải xử lý quy trình lặp đi lặp lại mỗi ngày:

```
Lấy mẫu kiểm tra → Phát hiện lỗi → Ghi biên bản tay → Báo quản đốc → Chờ duyệt → Xử lý lô hàng
```

### 3 Nỗi Đau Lớn Nhất

| # | Vấn đề | Hậu quả |
|---|--------|---------|
| 1 | Lập biên bản sự cố (NCR) **bằng sổ tay, giấy tờ** | Chậm 2-4 tiếng, quản đốc biết thì lô hàng lỗi đã đóng thùng giao khách |
| 2 | Không biết **nguyên nhân gốc rễ** lỗi do đâu | Máy hỏng? Nguyên liệu kém? Công nhân sai thao tác? → Lỗi lặp lại liên tục |
| 3 | Thông tin **không liên thông** giữa KCS, Quản đốc, Kho | Thủ kho vẫn xuất lô hàng bị lỗi cho khách → Bị phạt hợp đồng |

---

## 4 Tính Năng Cốt Lõi

### ① AI Soạn Biên Bản Sự Cố Tự Động (NCR Auto-Draft)
- KCS chụp ảnh sản phẩm lỗi + gõ 1 dòng mô tả ngắn.
- AI (Gemini Vision) phân tích ảnh & ngữ cảnh → Tự động điền biên bản:
  - Loại lỗi (Crack / Scratch / Deformation / MissingPart)
  - Mức độ nghiêm trọng (Minor / Major / Critical)
  - Phân tích nguyên nhân gốc rễ (Root Cause Analysis)
  - Đề xuất hành động xử lý

### ② Khóa Lô Hàng Tức Thì (Lot Locking)
- Khi biên bản NCR được tạo → Hệ thống tự động khóa lô hàng trong DB.
- Thủ kho mở phần mềm lên sẽ thấy lô này bị `LOCKED 🔒` → Không thể xuất kho nhầm.

### ③ Cảnh Báo Quản Đốc Realtime (SignalR Andon Alert)
- Ngay khi KCS gửi biên bản → Quản đốc nhận thông báo tức thì trên Dashboard (không cần F5).
- Quản đốc xem ảnh lỗi + phân tích AI → Bấm 1 nút ra quyết định:
  - `[Tái chế sửa lại (Rework)]` hoặc `[Hủy bỏ (Scrap)]` hoặc `[Trả nhà cung cấp (Return)]`

### ④ Báo Cáo Pareto & Xuất PDF
- Biểu đồ Pareto 80/20: Chỉ ra loại lỗi nào chiếm nhiều nhất.
- Xuất biên bản NCR thành file PDF chuẩn ISO 9001 để gửi đối tác/khách hàng.

---

## Luồng Vận Hành

```
[KCS phát hiện lỗi]     →  [API nhận ảnh]       →  [Gemini Vision AI]
  Chụp ảnh + Mô tả          Gọi AI phân tích         Trả JSON kết quả
                                                            │
                                                            ▼
[Quản đốc duyệt]        ←  [SignalR đẩy cảnh báo] ←  [Tạo biên bản NCR]
  Ra quyết định xử lý        Realtime < 1 giây          Khóa lô hàng
        │
        ▼
[Ghi Audit Trail + Xuất PDF báo cáo]
```

---

## Các Vai Trò Trong Hệ Thống

| Vai trò | Thiết bị | Làm gì |
|---------|----------|--------|
| **Nhân viên KCS** | Tablet / Điện thoại | Chụp ảnh lỗi, gửi cho AI soạn biên bản, yêu cầu khóa lô |
| **Quản Đốc xưởng** | Máy tính Desktop | Nhận cảnh báo realtime, xem phân tích AI, phê duyệt xử lý |
| **Giám Đốc / Ban QA** | Máy tính Desktop | Xem báo cáo Pareto, thống kê lỗi theo tháng, xuất PDF |

---

## Tech Stack

| Thành phần | Công nghệ |
|------------|-----------|
| Backend | ASP.NET Core Web API (.NET 10, C#) |
| Pattern | Controller → Service → Repository |
| Database | SQLite (EF Core) — dễ mang đi demo, đổi sang SQL Server 1 dòng config |
| Realtime | SignalR (WebSockets) |
| AI | Google Gemini 1.5 Flash Vision API |
| Background Job | BackgroundService / IHostedService |
| Frontend | HTML + Tailwind CSS + JavaScript |
| Export PDF | QuestPDF |

---

## Cơ Sở Dữ Liệu (6 Bảng)

```
WorkStation (Trạm sản xuất)
  │
  └── ProductionLot (Lô hàng sản xuất)
        │
        └── NcrReport (Biên bản sự cố chất lượng)
              │
              ├── DefectImage (Ảnh chụp sản phẩm lỗi)
              │
              └── NcrDecision (Quyết định xử lý của Quản đốc)

AppUser (Người dùng: KCS / Supervisor / Manager)
```

---

## Kịch Bản Demo Phỏng Vấn (3 Phút)

| Thời gian | Hành động | Thiết bị |
|-----------|-----------|----------|
| 0:00 - 0:30 | Mở Dashboard Quản đốc, giới thiệu trung tâm điều hành xưởng | Laptop |
| 0:30 - 1:30 | Rút điện thoại, đóng vai KCS: chọn lô → chụp ảnh lỗi → bấm "AI Phân Tích" → gửi biên bản | Điện thoại |
| 1:30 - 2:00 | **Khoảnh khắc WOW:** Laptop tự động hiện cảnh báo, lô hàng chuyển sang `LOCKED 🔒` | Laptop (tự động) |
| 2:00 - 2:30 | Đóng vai Quản đốc: xem ảnh lỗi + AI phân tích → bấm "Chấp nhận Rework" | Laptop |
| 2:30 - 3:00 | Mở báo cáo Pareto → bấm xuất PDF biên bản NCR | Laptop |

---

## Tác Giả

- **Dự án học tập ASP.NET Core Web API**
- **Mục tiêu:** Portfolio & Đồ án tốt nghiệp

---

## Cài Đặt & Chạy

```bash
# 1. Clone project
cd SmartFactory

# 2. Restore packages
dotnet restore

# 3. Chạy migration tạo database
dotnet ef database update --project SmartFactory.Api

# 4. Khởi chạy hệ thống
dotnet run --project SmartFactory.Api
```
