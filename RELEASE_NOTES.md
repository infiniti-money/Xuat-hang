# HISA Quản lý kho — v1.3.0 (Windows 64-bit)

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
