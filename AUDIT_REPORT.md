# Báo cáo rà soát · Quản lý kho 1.2.1

Đã rà soát mã nguồn, chạy kiểm tra nghiệp vụ và kiểm tra lại bản `.exe` tự chứa .NET. Kết quả: **220 kiểm tra đạt**, biên dịch không có lỗi/cảnh báo. Bộ kiểm tra tăng từ 143 lên 220 trường hợp. Các thao tác ghi thử dùng workbook và thư mục tạm; không ghi vào workbook thực tế của người dùng.

## Các lỗi đã xác nhận và sửa

| Nhóm | Tình huống trước khi sửa | Kết quả sau sửa |
| --- | --- | --- |
| Sửa phiếu | Tải lại vật tư có thể tạo ID/mã phiếu mới, mất phần tên/ghi chú/trạng thái đang sửa. | Giữ phiếu hiện tại và thông tin đã sửa; chỉ thay danh mục vật tư, cập nhật serial theo máy còn khớp. |
| Nhập số lượng | `1,5` hoặc `1.5` có thể thành `15` do cách đọc dấu hàng nghìn. | Đọc đúng dấu thập phân; hỗ trợ dạng có dấu nhóm đầy đủ, từ chối chuỗi sai. |
| Ô nhập lỗi | Lệnh lưu/in có thể tiếp tục dù ô số lượng chưa hợp lệ, dùng giá trị cũ. | Dừng thao tác và yêu cầu sửa ô lỗi; kiểm tra cả trước khi nhập serial. |
| Lý do trạng thái | Đổi Tạm hoãn sang Hủy có thể mang theo lý do hoãn sang ô hủy. | Giữ riêng nội dung lý do cho từng trạng thái. |
| Vật tư khác | Tùy chọn lấy vật tư khác trong mẫu in chưa có tác dụng. | Có ô Khác và áp dụng mặc định theo mẫu; người dùng có thể chủ động thay đổi. |
| Hợp đồng bỏ chọn | Hợp đồng chỉ còn trong kiện lưu kèm vẫn có thể bị ghi vào sổ xuất, báo đã xuất và báo cáo ngày. | Chỉ hợp đồng có vật tư được chọn xuất/in mới được ghi nhận xuất; hồ sơ kiện gốc vẫn lưu để đối chiếu. |
| Vật tư thêm tay | Thêm vật tư cho HĐ khác chưa cập nhật đầy đủ danh sách hợp đồng/báo cáo. | Bổ sung đúng hợp đồng vào phiếu, sổ và báo cáo; dòng thiếu HĐ chỉ tự điền khi xác định được duy nhất. |
| Ranh giới kiện | Dòng chỉ ghi nhãn HƯNG YÊN, không có vật tư, có thể làm hai kiện bị gộp. | Giữ từng khối kiện riêng. |
| Nhập kho/KT | Các dòng hợp đồng xen kẽ có thể khiến thao tác thay thế xóa cả dòng không liên quan. | Chọn đúng dòng theo hợp đồng/công trình/ngày, xóa một lần và giữ nhãn kiện của các dòng còn lại. |
| Nhận diện nguồn | Một số hợp đồng trùng mã, nhiều công trình, chưa đủ thông tin có thể bị gán vào công trình đầu tiên. | Cảnh báo trường hợp chưa xác định; không ghi đè bảng cont khi nguồn còn mơ hồ hoặc số lượng không hợp lệ. |
| Serial | Serial null gây lỗi; phiếu chờ đang sửa số máy có thể làm việc đồng bộ mọi phiếu bị chặn. | Serial trống được xử lý an toàn; dữ liệu chưa hoàn chỉnh ở phiếu chờ không chặn phiếu hợp lệ khác, nhưng vẫn không được xuất khi thiếu serial. |
| Cảnh báo | Cờ hoãn cũ có thể lấn át trạng thái hiện tại; báo cáo cũ vẫn có thể được dùng khi nguồn mất kết nối. | Ưu tiên trạng thái phiếu hiện tại và kiểm tra lại nguồn khi lập phiếu/kế hoạch. |
| Sao lưu | Ghi đè ZIP có thể lỗi; bản sao có thể không đồng nhất với trạng thái đang ghi. | Ghi vào ZIP tạm dưới khóa đồng bộ; chỉ thay ZIP đích sau khi hoàn tất. Lỗi đọc giữ nguyên bản sao cũ. |
| Xuất báo cáo | Có thể chọn nhầm Tổng hợp.xlsx hoặc sổ xuất làm đích báo cáo. | Chặn ghi báo cáo đè các tệp nguồn/sổ chuẩn đang cấu hình. |
| Mẫu in | Một số lần lưu mẫu từ luồng nền có thể lỗi STA của WPF. | Kiểm tra mẫu đúng luồng và báo rõ bố cục không thể in. |
| Nội dung in | Phiếu riêng theo hợp đồng thiếu ghi chú, mẫu cũ bị lặp số mục, ẩn cột chưa đầy đủ. | In đủ ghi chú và serial; bỏ phần số mục lặp; ẩn cả tiêu đề/cột tương ứng. |
| Phân trang | A5 dùng chữ/lề lớn có thể tràn bảng hoặc chân trang; ngày có thể tách khỏi chữ ký. | Đo dung lượng còn lại của trang, phân trang đúng, giữ ngày/chữ ký cùng trang; từ chối bố cục không khả thi. |
| Độ chính xác in | `0,125` bị làm tròn thành `0,13`, `0,001` có thể thành `0,00`. | Giữ tối thiểu hai chữ số thập phân và giữ thêm phần lẻ cần thiết theo dữ liệu nguồn. |
| Quy trình đóng gói | Có thể đọc nhầm kết quả kiểm tra cũ nếu tiến trình không tạo kết quả mới. | Mỗi lần kiểm tra dùng tệp kết quả mới; chỉ công nhận khi có kết quả đạt và đủ danh sách kiểm tra. |

## Xác minh

- Các kiểm tra hiện có về nhiều hợp đồng, nhiều công trình cùng mã, số có ngoặc/tiền tố BG, kiện Hưng Yên, trạng thái, serial, in và xuất Excel tiếp tục đạt.
- Đã thêm kiểm tra cho các tình huống lỗi trong bảng trên, bao gồm khóa file Excel và sao lưu thất bại.
- Đã dựng và xem bản in A5; có kiểm tra mọi serial vẫn còn sau khi chia trang.
- Quét đọc nguồn thực tế giữ kết quả đối chiếu 123 kiện Hưng Yên, không phát sinh cảnh báo hoặc thêm/thiếu công trình so với bảng cont dùng đối chiếu tại thời điểm kiểm tra.
- Kết quả máy đọc ở `verification\self-test.json`; thông tin bản phát hành và SHA-256 ở `verification\release-1.2.1.json`.

Các workbook thực tế được giữ nguyên trong đợt rà soát này.
