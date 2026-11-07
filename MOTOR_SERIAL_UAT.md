# Nghiệm thu đồng bộ serial máy kéo — Windows 11

Thực hiện trên **bản sao** dữ liệu, tuyệt đối không chạy thử ghi lên file `MÁY KÉO.xlsx` sản xuất.

## Chuẩn bị
- Dùng một bản sao của `MÁY KÉO.xlsx`, liên kết tại **Cấu hình nguồn dữ liệu → Máy kéo**.
- Số hợp đồng nằm ở **cột D**, công trình **cột B**, serial **cột I**.
- Tạo dữ liệu mẫu một HĐ xuất hiện ở hai công trình T1/T2; T1 có một serial và một ô I trống, T2 có serial riêng.
- Cất một bản sao của Excel gốc trước khi thao tác. Đóng Excel khi HISA cần ghi.

## Quy tắc chọn công trình khi ghi serial mới cột I
- [ ] HĐ chỉ có **một công trình** trong MÁY KÉO.xlsx: tra cột D và đề xuất ghi vào đúng ô I trống; không yêu cầu tên công trình/đơn vị trên phiếu phải trùng.
- [ ] HĐ có **T1/T2 hoặc nhiều công trình** trong cột B: dù tên công trình trên phiếu đã giống Excel, **chưa có xác nhận** thì không được ghi serial mới vào I.
- [ ] Chọn công trình trong hộp xác nhận rồi duyệt đúng dòng Excel: chỉ ghi I của công trình đã chọn. Lựa chọn được lưu với phiếu cho lần đồng bộ Đã xuất và thử lại.
- [ ] Chọn bỏ qua xác nhận công trình khi Đã xuất: giữ phiếu và serial đã nhập nhưng việc ghi serial I/ngày K tương ứng tiếp tục ở trạng thái chờ đối chiếu.
- [ ] Khi một HĐ có nhiều dòng máy cùng công trình mà không thể ghép duy nhất máy/serial, không tự chọn ô I; phải kiểm tra dữ liệu và xác nhận riêng.

## Lập phiếu và in
- [ ] Lập phiếu từ đúng số HĐ và công trình T1; serial đã có ở T1 tự hiển thị trên phiếu, serial của T2 không xuất hiện.
- [ ] Khi còn máy chưa có serial, HISA hỏi **Có / Không / Hủy**. Chọn **Không** vẫn xem trước/in được; serial thiếu giữ trống.
- [ ] Chọn **Có** thì hiện bảng serial từng máy. Nếu HĐ xuất hiện ở nhiều công trình, hiện hộp **Chọn công trình**.
- [ ] Chọn T1 và nhập serial bổ sung; hộp duyệt ghi Excel hiển thị đúng công trình, đúng số dòng nguồn/cột I.
- [ ] Nếu công trình trên phiếu khác tên công trình nguồn, hệ thống yêu cầu thêm một lần xác nhận trước khi cho phép dùng lựa chọn thủ công.
- [ ] Chọn **Hủy** không ghi Excel, không ghi sổ xuất, không thay đổi trạng thái phiếu.

## Khi xác nhận Đã xuất / Đã bàn giao

- [ ] Dùng file `MÁY KÉO.xlsx` **bản sao**, trong đó một máy có serial I đã có, một máy cùng công trình có I trống; T2 cùng hợp đồng có serial riêng.
- [ ] Lưu phiếu **Chờ xuất** không ghi thêm serial I và ngày K theo luồng đồng bộ khi xuất.
- [ ] Nhập serial mới trên phiếu và xác nhận **Đã xuất**: serial được ghi vào đúng **I** trống và ngày thực tế trên phiếu vào đúng **K** cùng dòng máy, không ghi nhầm T2.
- [ ] Một lần xác nhận phải tạo **một bản sao lưu Excel** cho cả serial I và ngày K; không tồn tại trường hợp ghi I thành công còn K lỗi.
- [ ] Nếu K chứa công thức, ngày thủ công khác, file đã bị sửa hoặc Excel đang khóa: **không** ghi đè; phiếu phải cảnh báo còn chờ đồng bộ.
- [ ] Đổi tên công trình/thiếu công trình hoặc có nhiều dòng I trống không thể xác định duy nhất: giữ nguyên Excel, hiển thị HĐ/công trình để đối chiếu.
- [ ] Chạy đồng bộ lại: không ghi trùng, không tạo bản sao lưu mới nếu không có thay đổi.
- [ ] Hủy phiếu Đã xuất: chỉ xóa ngày K do HISA đã ghi nếu giá trị chưa bị người dùng thay đổi; **serial vật lý I vẫn được giữ**.

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
