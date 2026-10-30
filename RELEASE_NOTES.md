# Quản lý kho HISA — v1.2.8

Bản phát hành đầu tiên trên GitHub dành cho **Windows 64-bit**. Ứng dụng được đóng gói tự chứa .NET 10, không cần cài .NET hoặc Microsoft Excel để đọc dữ liệu.

## Chức năng chính
- Tra cứu hợp đồng và kiện hàng Hưng Yên; lọc nhiều hợp đồng, công trình, ngày và nhóm vật tư.
- Lập kế hoạch xuất hàng, tạo/in phiếu bàn giao; quản lý trạng thái chờ, đã xuất, tạm hoãn và hủy.
- Kiểm tra và lưu serial máy kéo, ngăn trùng hoặc thiếu serial khi xuất.
- Đọc, đối chiếu, cập nhật bảng Excel, thống kê và báo cáo ngày.
- Tùy chỉnh mẫu in A4/A5, sao lưu dữ liệu và khôi phục đối chiếu lịch sử.

## Tải và chạy
1. Tải `HISA-Kho-Vat-Tu-1.2.8-win-x64.zip` từ mục **Assets** của Release.
2. Giải nén đầy đủ ZIP vào thư mục sử dụng.
3. Mở `HISA Kho Vat Tu.exe`. Ứng dụng có giao diện tiếng Việt và chạy trên Windows x64.
4. Vào **Cấu hình & sao lưu** để chọn đúng đường dẫn nguồn Excel; nên sao lưu workbook thực tế trước khi bắt đầu sử dụng.

Có thể kiểm tra bằng dữ liệu thử với tham số `--demo`, không cần sử dụng dữ liệu kho thực.

## Kiểm thử trước phát hành
Quy trình GitHub Actions chạy trên `windows-latest`, biên dịch .NET 10 Release, kiểm tra phiên bản, chạy `--self-test` với workbook tạm riêng và chỉ xuất bản khi các bước thành công. Bản ZIP phát hành kèm checksum SHA-256 để đối chiếu toàn vẹn.

**Lưu ý:** Không chạy đồng thời ứng dụng cũ và mới để ghi chung workbook Excel. Sao lưu và kiểm tra đường dẫn dữ liệu trước khi xuất hàng thực tế.

Chi tiết hướng dẫn: [README.md](https://github.com/infiniti-money/Xuat-hang/blob/main/README.md).
