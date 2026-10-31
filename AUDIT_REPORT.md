# BÁO CÁO RÀ SOÁT TIẾP THEO · HISA v1.9.15 → v1.9.16 · 31/10/2026

**Phạm vi:** kiểm tra mã GitHub và kết quả Windows CI, ZIP restore, JSON schema, GitHub updater, nguồn Excel, Gemini credential. Không sử dụng dữ liệu kho thật.

| Ưu tiên | Phát hiện trong v1.9.15 | Khắc phục v1.9.16 / giới hạn |
|---|---|---|
| Cao | Tệp cài đặt trong cache sai SHA-256 được xóa khi luồng File.OpenRead còn mở; Windows có thể khóa việc xóa và làm kẹt cập nhật. | Đóng luồng trước khi xóa; thêm thử nghiệm cache chuẩn và bị hỏng. |
| Cao | Khôi phục có thể chấp nhận JSON đúng cú pháp nhưng sai mô hình (ví dụ slips.json là đối tượng thay vì danh sách), khiến HISA lỗi khi nạp dữ liệu. | Validate kiểu dữ liệu đã biết trước khi thay đổi tệp, kiểm thử JSON sai kiểu và mục null. |
| Trung bình | Tên đường dẫn Excel trong ZIP chỉ phân biệt chữ hoa/thường ở một số bước, tạo lỗi restore với ZIP cũ. | Nhất quán không phân biệt hoa/thường khi đếm, lựa chọn và ánh xạ workbook. |
| Trung bình | Nút kiểm tra cập nhật chưa thể hiện trực tiếp chức năng thử lại khi mạng gián đoạn. | Đổi nhãn "Kiểm tra / Thử lại"; tiếp tục xác minh SHA-256 và tải phần còn thiếu. |
| Cần kiểm chứng máy thật | GitHub Actions không thử UAC bị từ chối, antivirus, tốc độ mạng thực tế hay file Excel của người dùng. | Giữ issue #13 mở; yêu cầu log lỗi khi tái hiện. |

**Nguyên tắc:** Không tự sửa nguồn Excel khi kiểm tra cập nhật; bản phục hồi chỉ thực hiện sau khi chọn nhóm, xem trước, xác nhận; lưu bản sao an toàn và rollback. API Key Gemini thuộc Windows Credential Manager, không xuất ra ZIP. CI tự động không thay thế thử nghiệm trên máy thật.

---

# BÁO CÁO RÀ SOÁT TOÀN DIỆN · HISA v1.9.9 → v1.9.10 · 31/10/2026

**Phạm vi:** repo `infiniti-money/Xuat-hang`, WPF/.NET 10, cài đặt và updater GitHub Releases, bảo vệ JSON/ZIP, file `Tổng hợp.xlsx` / `KT tổng hợp.xlsx` / `Thành phẩm xuất kho.xlsx`, thư mục `CONT ĐI` / `Đã nhập kho` / `Hàng gửi`, Gemini/Zalo, phiếu chờ/đã bàn giao, Tủ điện/Máy kéo/Đầu cửa car, lịch máy kéo Hưng Yên.

## Phát hiện xác minh được và xử lý

| Ưu tiên | Hạng mục | Hiện trạng và tác động | Xử lý ở v1.9.10 |
| --- | --- | --- | --- |
| **Cao** | ZIP sao lưu lịch AI | `WarehouseService.BackupAsync` trước đây chỉ thêm JSON trực tiếp dưới `native_data`, không bao gồm thư mục `ai_schedules`; lịch đã duyệt sẽ không có trong bản sao | Thêm `ai_schedules/lich-kho-*.json`, manifest v2 và SHA-256 mỗi entry, kiểm tra lại ZIP trước khi thay bản cũ; regression cố tình sửa nội dung entry phải bị phát hiện |
| **Cao** | Lịch sử máy kéo mất liên kết | `MotorHungYenStore.MachineKey` gồm **số dòng Excel**, khiến chèn hoặc sắp lại dòng máy đã gửi/làm/bàn giao bị hiển thị như máy mới chưa ghi nhận | Giữ khóa cũ, khôi phục ánh xạ theo duy nhất HĐ + công trình + công ty + loại máy + số lô + seri; không tự khớp thiếu seri hoặc seri/định danh bị trùng; thêm regression |
| **Trung bình** | Cấu hình nguồn Excel | `HisaFolderLayout.PreferredSelection` có thể ném `FileNotFoundException` khi cấu trúc vẫn có thư mục nhưng `Tổng hợp.xlsx` biến mất; ảnh hưởng khả năng mở Cấu hình để sửa | Giữ nguyên đường dẫn cấu hình cho người dùng chỉnh, không làm văng màn hình |
| **Đang mở** | Phục hồi ZIP | Chưa có luồng chọn tệp ZIP, kiểm tra bản sao, rollback và phục hồi an toàn | Giữ issue [#11](https://github.com/infiniti-money/Xuat-hang/issues/11) mở; v1.9.10 không tự kích hoạt thao tác nguy cơ ghi đè |
| **Đang mở** | Tải/cài cập nhật gián đoạn | Có báo cáo user bị dừng tải và lỗi nâng cấp; v1.9.9 xác minh SHA-256 nhưng chưa xác nhận giải pháp trên thiết bị thật | Giữ issue [#13](https://github.com/infiniti-money/Xuat-hang/issues/13) mở; cần log UAC/Inno Setup và thử lỗi mạng/Windows thực tế |

## Các bất biến nghiệp vụ đã đối chiếu trong mã nguồn và bộ kiểm thử

- Chọn **Kho HISA** phải ánh xạ `Kho chính` tới ba workbook, `CONT ĐI` chỉ là nguồn kiện, `Danh sách sản xuất` chỉ đọc, không trộn bảng thiết bị thành vật tư đã nhập.
- Lịch **dự kiến cont về** từ ảnh Gemini không tự ghi trạng thái nhập kho; chỉ chọn đúng kiện thực nhận/ngày thực tế rồi xác nhận mới ghi chi tiết.
- Tích **Làm phiếu** chỉ chuẩn bị phiếu chờ, không tự xác nhận **Đã xuất**. Hợp đồng cùng mã nhưng công trình T1/T2, cont và ngày đi khác nhau phải được đối chiếu chính xác.
- Phân loại `Đã nhập kho` chỉ khi đầy đủ bằng chứng kiện thực nhận; **Hàng gửi** ở ngang cấp `CONT ĐI`. Không tự ghi đè file có tên trùng.
- Không đưa Gemini API Key vào ZIP; khóa thuộc Windows Credential Manager, người dùng sẽ phải cấu hình lại trên máy hoặc tài khoản Windows khác.

## Giới hạn xác minh

Kiểm tra mã GitHub + bộ regression CI Windows dùng **workbook tạm**, không thao tác trực tiếp các file Excel thực tế và không gọi Gemini API thật. Những điều chưa được xác minh: chạy GUI trên màn hình/DPI Windows của người dùng, độ tin cậy mạng khi cập nhật, file Excel bị khóa trong sản xuất, quyền/antivirus/UAC và thực tế ánh xạ tất cả file tháng. Kết quả CI phản ánh kiểm thử tự động, **không phải chứng nhận không còn lỗi**.

Báo cáo này là phần bổ sung của audit cũ; không thay thế các ghi nhận lịch sử bên dưới.

---

# Báo cáo rà soát HISA v1.8.1 – 30/10/2026

**Phạm vi:** mã nguồn GitHub `infiniti-money/Xuat-hang`, WPF/.NET 10, dữ liệu Excel/phiếu, luồng cont Hưng Yên, lịch Zalo Gemini, xác nhận nhập–xuất, Gemini Chat/credential, updater và Windows GitHub Actions.

**Mức độ xác minh:** đọc mã và kiểm thử tự động trên Windows CI. Đây không phải chứng nhận đã thử nghiệm mọi thao tác GUI trên máy tính thực tế hoặc mọi biến thể workbook của đơn vị.

## Lỗi đã tìm thấy và sửa trong bản vá 1.8.1

| Mức độ | Nhóm | Mô tả | Xử lý |
| --- | --- | --- | --- |
| Nghiêm trọng | Nhập cont từ AI | Một hợp đồng chỉ xuất hiện ở một container trên ảnh, nhưng các kiện cùng HĐ trong Excel có nhiều ngày cont đi. Khi không có nguồn khớp, mã cũ có thể gán toàn bộ kiện sang cont đó. | Chặn gán theo HĐ; bắt buộc khớp mã container hoặc ngày cont đi đã xác minh. |
| Nghiêm trọng | Cont và ngày không khớp | Nếu nguồn đã có số cont, mã cũ chỉ đối chiếu cont, bỏ qua ngày cont đi khác với ảnh. | Cả hai thuộc tính phải khớp khi cả hai được cung cấp. |
| Cao | Cùng cont nhiều đợt | Nguồn có một mã cont lặp qua nhiều ngày mà ảnh không có ngày cont đi. | Hiển thị trạng thái Chưa xác minh ngày cont đi, không cho ghi Excel. |
| Cao | Kiện nguồn rỗng | Một kiện có mã HĐ nhưng không còn dòng vật tư Hưng Yên có thể được xem là đủ nguồn và ghi nhận một kiện đã nhập dù không có dòng Excel. | Chặn ở bước xem trước và kiểm tra ngay trước khi ghi Excel. |
| Cao | Ngày duyệt AI | Sửa ngày dự kiến sau khi lưu duyệt không hủy duyệt phiên, có thể dùng lịch chưa được duyệt để tạo phiếu. | Chỉnh ngày hoặc nội dung bảng đều làm mất phê duyệt, phải duyệt lại. |
| Trung bình | Tên công trình | Ghi lại một kiện sau khi sửa ngày có thể thêm nhiều hậu tố `(dd.MM)`. | Chuẩn hóa chỉ một hậu tố cuối và giữ dấu T1/T2 hoặc số thang. |
| Trung bình | Gemini Chat | AI nhìn thấy kế hoạch nhưng thiếu trạng thái chuẩn bị từ bộ kiểm tra HISA, có thể suy luận không chính xác. | Gửi trạng thái đã đối chiếu, không đưa serial hoặc nguồn Excel gốc. |

## Quy tắc an toàn được giữ
- Lịch **dự kiến** không đồng nghĩa hàng **đã về** hay **đã xuất**.
- Phân biệt kiện theo **số HĐ + công trình/Thang/T1/T2 + vật tư/số lượng + mã cont + ngày cont đi**.
- Không gộp hoặc ghi đè kiện khác ngày cont đi; khi thiếu bằng chứng phải yêu cầu xác minh.
- Cần người dùng xác nhận trước khi ghi `Tổng hợp.xlsx` hoặc lập phiếu, và xác nhận riêng khi thực sự bàn giao.
- Khóa Gemini lưu qua các lần nâng cấp trong Windows Credential Manager, có thể xóa tại Cấu hình; mặc định Gemini Chat không đọc dữ liệu kho.

## Các giới hạn chưa thể chứng thực bằng CI
- Hiển thị giao diện thực tế khi thay đổi độ phân giải, DPI và phiên Windows; chưa nhận được ảnh chụp màn hình từ ứng dụng đang chạy.
- Đọc và đối chiếu workbook máy kéo có serial và đầu cửa cabin của đơn vị (chưa được cung cấp bản mẫu thực).
- Đọc ảnh Zalo và kiểm tra kết quả Gemini trên API Key, mạng và hạn mức thực tế.
- Không thử nghiệm trực tiếp với workbook gốc của người dùng để tránh thay đổi dữ liệu thật.

## Kiểm thử & phát hành
Bộ kiểm thử hồi quy Windows theo `.github/workflows/windows-release.yml` chạy với workbook tạm; kết quả và trạng thái phát hành được xem trực tiếp tại GitHub Actions của nhánh/PR tương ứng. Chỉ gộp và phát hành bản 1.8.1 sau khi CI đạt.

---

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
