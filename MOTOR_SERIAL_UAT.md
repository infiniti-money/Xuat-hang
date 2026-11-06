# Nghiệm thu đồng bộ serial máy kéo — Windows 11

Thực hiện trên **bản sao** dữ liệu, tuyệt đối không chạy thử ghi lên file `MÁY KÉO.xlsx` sản xuất.

## Chuẩn bị
- Dùng một bản sao của `MÁY KÉO.xlsx`, liên kết tại **Cấu hình nguồn dữ liệu → Máy kéo**.
- Số hợp đồng nằm ở **cột D**, công trình **cột B**, serial **cột I**.
- Tạo dữ liệu mẫu một HĐ xuất hiện ở hai công trình T1/T2; T1 có một serial và một ô I trống, T2 có serial riêng.
- Cất một bản sao của Excel gốc trước khi thao tác. Đóng Excel khi HISA cần ghi.

## Lập phiếu và in
- [ ] Lập phiếu từ đúng số HĐ và công trình T1; serial đã có ở T1 tự hiển thị trên phiếu, serial của T2 không xuất hiện.
- [ ] Khi còn máy chưa có serial, HISA hỏi **Có / Không / Hủy**. Chọn **Không** vẫn xem trước/in được; serial thiếu giữ trống.
- [ ] Chọn **Có** thì hiện bảng serial từng máy. Nếu HĐ xuất hiện ở nhiều công trình, hiện hộp **Chọn công trình**.
- [ ] Chọn T1 và nhập serial bổ sung; hộp duyệt ghi Excel hiển thị đúng công trình, đúng số dòng nguồn/cột I.
- [ ] Nếu công trình trên phiếu khác tên công trình nguồn, hệ thống yêu cầu thêm một lần xác nhận trước khi cho phép dùng lựa chọn thủ công.
- [ ] Chọn **Hủy** không ghi Excel, không ghi sổ xuất, không thay đổi trạng thái phiếu.

## Ghi ngược Excel
- [ ] Chỉ sau khi xác nhận, ô **I** đang trống và đúng dự án nhận serial mới.
- [ ] Serial đã có ở ô I khác, ngày xuất **cột K**, các công thức và ghi chú ở cột khác không thay đổi.
- [ ] File sao lưu Excel được tạo trước khi thay thế file nguồn.
- [ ] HĐ giống nhau nhưng công trình T1/T2 khác nhau không được ghi chéo, kể cả khi model khác tên.
- [ ] Nếu số ô I trống không tương ứng với số serial cần ghi, không tự đoán dòng; hiển thị cảnh báo.
- [ ] Khi nguồn thay đổi sau bản xem trước, xác nhận cũ không được phép ghi xuống file nguồn.
- [ ] Serial trùng với công trình/HĐ khác phải bị từ chối.

## Xác nhận Đã xuất
- [ ] Chọn **Không** tại câu hỏi nhập thêm serial vẫn có thể xuất hàng sau khi xác nhận rõ ràng; phiếu ghi nhận serial chưa có để bổ sung sau.
- [ ] Với serial có thật, phiếu xuất/bàn giao và bản xem trước A4 in đúng các serial đó.
- [ ] Không tự suy ra một máy cụ thể từ serial còn trống để ghi ngày bàn giao sai dòng cột K.
- [ ] Trạng thái **Chờ xuất** không làm thay đổi sổ xuất hay ngày bàn giao.
- [ ] Không tự ghi Excel chỉ vì người dùng mở bảng serial, chọn công trình hoặc in phiếu.

## Kiểm thử tự động
Trên PowerShell 7:

```powershell
git fetch origin
git switch fix/motor-serial-project-selection
git pull --ff-only origin fix/motor-serial-project-selection
.\BUILD.ps1 -Test
(Get-Content .\verification\self-test.json -Raw -Encoding UTF8 | ConvertFrom-Json) |
  Select-Object success,@{Name='Checks';Expression={$_.checks.Count}}
```

**Cổng gộp/Release:** Windows CI xanh, các trường hợp nghiệp vụ trên bản sao đạt, kiểm thử in và cài đè đạt; mới quyết định gộp `main` và tăng phiên bản.
