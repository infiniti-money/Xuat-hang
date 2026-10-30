# HISA Quản lý kho — v1.3.1 (Windows 64-bit)

## Tính năng mới
- **Cập nhật thủ công:** mở **Thông tin phần mềm → Kiểm tra cập nhật**; tải bản mới có kiểm tra SHA-256 và có thể bấm **Cài bản đã tải**. Cập nhật tự động/yên lặng khi đóng phần mềm vẫn được duy trì.
- **Xuất máy kéo thiếu serial:** hiển thị xác nhận Có/Không; chỉ khi xác nhận mới cho ghi nhận Đã xuất khi còn thiếu serial. Các ô serial thiếu giữ trống để bổ sung sau. Serial đã nhập vẫn được kiểm tra trùng và không chuyển nhầm công trình.
- **Phân loại file trong CONT ĐI:** các tệp có tên chứa `hanggui` được đưa vào `CONT ĐI\\Hàng gửi`. Tệp có kiện Hưng Yên chỉ được chuyển sang `CONT ĐI\\Đã nhập kho` sau khi cập nhật và đọc lại Tổng hợp.xlsx thành công. Các file không phù hợp được giữ nguyên.
- **Giữ nguyên tên các thư mục ngày:** như `26.10`, `27.10` và những thư mục con gốc. Ví dụ `CONT ĐI\\26.10\\29102026.xlsx` → `CONT ĐI\\Đã nhập kho\\26.10\\29102026.xlsx`. Thư mục ngày ban đầu cũng không bị xóa/đổi tên.
- Khi trùng tên file trong thư mục đích, phần mềm không ghi đè bản lưu cũ; tệp chuyển mới có hậu tố riêng. File đã lưu vẫn được đọc lại nhằm tránh mất dữ liệu sau các lần đồng bộ.

## Bộ cài
Tải `HISA-Kho-Vat-Tu-1.3.1-Setup.exe` trong Assets để cài tiếng Việt vào `C:\\Program Files\\HISA Kho Vat Tu`. Có icon trên Desktop và menu Start. Phiên bản ZIP tự chứa cũng được cung cấp.

## Dữ liệu và sao lưu
Dữ liệu, cấu hình và phiếu vẫn ở **Thư viện → Tài liệu → HISA Kho Vat Tu → APP KHO → data**. Sao lưu ZIP và bản sao Excel ở **Thư viện → Tài liệu → HISA Kho Vat Tu → Backups**. Cập nhật hay gỡ cài đặt không xóa các thư mục dữ liệu này.

**Lưu ý:** Nâng cấp yên lặng không thể bỏ qua xác nhận UAC của Windows khi cài trong Program Files. Hãy đóng các tệp Excel đang sử dụng trước khi cập nhật nguồn.

## Chất lượng phát hành
GitHub Actions chạy trên Windows, biên dịch .NET, kiểm thử nghiệp vụ bằng workbook tạm (bao gồm phân loại file, thư mục `26.10` và `27.10`), biên dịch bộ cài Inno Setup và phát hành file cùng mã SHA-256 khi toàn bộ bước đạt.

---

# Ghi chú phiên bản 1.3.0 (Windows 64-bit)

## Cài đặt tiếng Việt
- Tải `HISA-Kho-Vat-Tu-1.3.0-Setup.exe` tại **Assets**, chạy để cài đặt theo mặc định vào `C:\\Program Files\\HISA Kho Vat Tu`.
- Bộ cài bằng tiếng Việt, có biểu tượng HISA ngoài Desktop và trong menu Start, hỗ trợ gỡ cài đặt.
- Phần mềm kèm .NET 10; không cần cài .NET hay Microsoft Excel để đọc file Excel.
- Có thêm bản ZIP tự chứa dành cho người muốn chạy không cần cài đặt.

## Tự cập nhật
- Ứng dụng kiểm tra GitHub Releases khi mở, tải bộ cài phiên bản cao hơn trong nền.
- Chỉ chấp nhận bản phát hành chính thức có bộ cài và SHA-256 khớp, đồng thời đối chiếu digest do GitHub cung cấp (nếu có).
- Khi người dùng đóng chương trình bình thường, bộ cài được chạy bằng chế độ `/VERYSILENT`, không hiện trình hướng dẫn và mở lại ứng dụng sau khi hoàn tất.
- **Windows vẫn có thể yêu cầu xác nhận UAC** vì ứng dụng được cài trong Program Files. Không bỏ qua cơ chế bảo mật này.
- Nếu mất Internet, sai checksum, hoặc người dùng từ chối quyền quản trị, dữ liệu và phiên bản đang dùng không bị thay đổi.

## Dữ liệu và sao lưu trong Thư viện → Tài liệu
- `Tài liệu\\HISA Kho Vat Tu\\APP KHO\\data` — trạng thái, phiếu, lịch sử, cấu hình.
- `Tài liệu\\HISA Kho Vat Tu\\Backups` — file ZIP sao lưu.
- `Tài liệu\\HISA Kho Vat Tu\\Backups\\Excel` — tự tạo bản sao trước khi ghi Excel.
- Dữ liệu cũ được đưa sang vị trí mới nếu chưa tồn tại bản mới; không ghi đè hồ sơ hoặc bản sao cũ.
- Gỡ cài đặt và cập nhật không có lệnh xóa thư mục tài liệu. Workbook thực tế vẫn nằm ở đường dẫn nguồn do người dùng chọn.

## Tính năng chính
Tra cứu hợp đồng, kiện Hưng Yên, nhập kho, đối chiếu KT, lập phiếu bàn giao, quản lý serial máy kéo, xuất Excel, mẫu in A4/A5, báo cáo ngày và sao lưu.

## Xác minh bản phát hành
GitHub Actions dùng Windows để biên dịch .NET 10, chạy bộ kiểm thử bằng workbook tạm, đóng gói ZIP và biên dịch bộ cài Inno Setup tiếng Việt. Chỉ khi kiểm thử hoàn tất mới tạo Release. Các tệp tải xuống có SHA-256 riêng.

Xem thêm tại [README.md](https://github.com/infiniti-money/Xuat-hang/blob/main/README.md).
