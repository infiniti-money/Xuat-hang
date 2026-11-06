# Nghiệm thu Windows 11 — HISA Xuất hàng

> Áp dụng cho nhánh `dev/hisa-win11-quality-gates`. **Không sửa Excel sản xuất** trong quá trình kiểm thử.
> Kết quả GitHub Actions chỉ chứng minh build / kiểm thử tự động, **không thay thế UAT trên Windows 11**.

## 1. Cổng build an toàn

Trên máy Windows 11 tiếng Việt với PowerShell 7 và .NET 10:

```powershell
git fetch origin
git switch dev/hisa-win11-quality-gates
git pull --ff-only origin dev/hisa-win11-quality-gates
.\BUILD.ps1 -Test
(Get-Content .\verification\self-test.json -Raw -Encoding UTF8 | ConvertFrom-Json).success
```

**Đạt khi:** `dotnet publish` không có warning, `self-test.json` được tạo mới và `success = True`.
Nếu build hoặc self-test thất bại: không gộp và không tạo Release chính thức.

## 2. Cổng giao diện Windows 11

- [ ] Mở `publish\HISA Kho Vat Tu.exe --demo`, không báo lỗi khởi động.
- [ ] **Cài đặt → Hệ thống → Màn hình → Tỷ lệ**: kiểm tra lần lượt 100%, 125% và 150% ở màn hình chính.
- [ ] Di chuyển cửa sổ giữa các màn hình có tỷ lệ khác nhau: không bị mờ chữ, mất nút, bảng bị cắt hoặc dialog nằm ngoài màn hình.
- [ ] Thu nhỏ, phóng to và thay đổi kích thước cửa sổ; kiểm tra trang Tổng quan, Cont, Phiếu xuất, Mây Mây, Cấu hình và Xem trước in.
- [ ] Nhấn **☾ Tối** / **☀ Sáng**, đóng và mở lại HISA: lựa chọn được giữ, không đổi dữ liệu kho hoặc trạng thái micro.
- [ ] Kiểm tra độ tương phản chữ, nền, bảng, ô nhập, hội thoại và vùng thao tác ở cả hai chế độ. **Mọi trang chuyên biệt chưa được chứng nhận tương thích chế độ tối cho đến khi UAT này đạt.**
- [ ] Phím Tab tới được các nút, không mất khung đánh dấu và không bị cắt văn bản dài.

## 3. Cổng nghiệp vụ xuất hàng (chỉ dùng file mẫu/bản sao)

- [ ] Đọc ảnh lịch Zalo mẫu: tách riêng dự kiến cont về và dự kiến xuất; chưa đánh dấu Đã nhập/Đã xuất.
- [ ] Nhận dạng `HƯNG YÊN`, `Hưng Yên`, `HUNG YEN` như cùng một địa điểm.
- [ ] Cùng số HĐ nhưng khác công trình `T1/T2`, thang `1/2`, hậu tố ngoặc: không trộn kiện.
- [ ] Quét Excel Container, đối chiếu ngày/mã cont/kiện thực nhận; không nhập trùng.
- [ ] Lập phiếu **Chờ xuất**; xem trước và in A4; chưa ghi sổ xuất trước xác nhận.
- [ ] Đối chiếu serial máy kéo đúng HĐ/công trình/model và vị trí cột I; từ chối ghi nhầm ô hoặc ghi đè công thức.
- [ ] Chỉ khi xác nhận **Đã xuất** mới cập nhật sổ và cột K ngày xuất thực tế; thử hủy/khôi phục trên dữ liệu mẫu.
- [ ] Đảm bảo dữ liệu Excel nguồn có bản sao lưu và khôi phục được.

## 4. Cổng Mây Mây / Gemini (bằng thiết bị, API và quyền thật của người dùng)

- [ ] Nếu không có Gemini API Key: thông báo rõ ràng, không gọi TTS với khóa rỗng và không bật micro.
- [ ] Sau khi cấp quyền rõ ràng, bấm **Nói với Mây** và chuyển qua trang Cont/Phiếu: không tạo cửa sổ mới, micro tiếp tục đúng phiên đã chủ động bật.
- [ ] Thu nhỏ HISA xuống khay hệ thống, kiểm tra biểu tượng trạng thái; thao tác **Dừng micro Mây** dừng thu âm ngay.
- [ ] Thu hồi quyền micro hoặc thoát HISA phải giải phóng micro/loa; không tự bật khi khởi động hoặc mở lại.
- [ ] Không log Gemini API Key; không gửi nguyên workbook hoặc serial ngoài phạm vi được cho phép.
- [ ] Mây chỉ đề xuất phiếu **Chờ duyệt**; không tự ghi Excel hoặc xác nhận xuất hàng.

## 5. Cổng bộ cài và phát hành

- [ ] Windows Actions trên PR **xanh**, gồm build warning-free, tự kiểm thử, Inno Setup và kiểm tra cài mới/cài đè.
- [ ] Tải artifact PR, kiểm tra đủ Setup, ZIP, SHA-256 và `self-test.json`.
- [ ] Cài thử trên máy Windows 11 (không đè dữ liệu sản xuất), UAC hoạt động đúng.
- [ ] Cài đè giữ được cấu hình, dữ liệu kho, mẫu phiếu, Windows Credential Manager; kiểm thử rollback từ bản sao lưu.
- [ ] Kiểm tra mở/đóng Mây Mây, luồng ảnh Zalo, serial và in sau khi cài đặt.
- [ ] Chỉ sau khi tất cả cổng đạt mới cân nhắc nâng version, gộp `main` và tạo Release chính thức.

**Ghi nhận nghiệm thu:** ngày/giờ, phiên bản Windows, độ phân giải, tỷ lệ DPI, tên máy thử nghiệm (không đưa API Key vào log), kết quả từng mục và lỗi có thể tái hiện.
