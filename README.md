# Quản lý kho — C# / .NET 10 · phiên bản 1.3.0

**Người tạo:** Trịnh Phương Tân  
**SĐT:** 0867899987  
**Email:** infinitimoney@icloud.com

## Bộ cài đặt Windows tiếng Việt và cập nhật tự động · phiên bản 1.3.0

- Vào [GitHub Releases](https://github.com/infiniti-money/Xuat-hang/releases/latest) và tải `HISA-Kho-Vat-Tu-1.3.0-Setup.exe`. Bộ cài sử dụng tiếng Việt và mặc định cài vào `C:\\Program Files\\HISA Kho Vat Tu`. Có biểu tượng trên Desktop và Start Menu, hỗ trợ gỡ cài.
- Giao diện chính hiển thị logo HISA. Phần mềm tự kiểm tra GitHub Releases khi mở, tải và xác minh bộ cài (SHA-256) trong nền; khi đóng ứng dụng sẽ nâng cấp yên lặng rồi mở lại.
- Chế độ `/VERYSILENT` ẩn trình hướng dẫn cài đặt nhưng **không thể bỏ qua cửa sổ UAC của Windows** khi nâng cấp ứng dụng cài trong Program Files. Nếu không cấp quyền quản trị, phiên bản cũ và dữ liệu vẫn giữ nguyên.
- Tài liệu ứng dụng: **Thư viện → Tài liệu → HISA Kho Vat Tu → APP KHO → data**.
- Sao lưu ZIP: **Thư viện → Tài liệu → HISA Kho Vat Tu → Backups**; các bản sao Excel trước khi ghi nằm trong thư mục **Backups → Excel**.
- Gỡ phần mềm hoặc nâng cấp không xóa dữ liệu và bản sao lưu trong Thư viện. Khi nâng cấp từ bản cũ, các file dữ liệu tìm thấy tại vị trí cũ sẽ được chuyển sang nếu file đích chưa tồn tại.

## Bản sửa lỗi 1.2.1

Đã rà soát và sửa lỗi tải lại phiếu, đọc số lượng, hợp đồng bị bỏ chọn vẫn bị ghi xuất, thay thế các dòng Excel xen kẽ, serial rỗng, sao lưu và phân trang A5. Bản đóng gói đạt 220 kiểm tra. Xem chi tiết trong [AUDIT_REPORT.md](AUDIT_REPORT.md).

Ô **Xuất / in** ở từng dòng là danh sách vật tư được bàn giao. Hợp đồng không còn dòng nào được chọn sẽ không bị ghi nhận là đã xuất; toàn bộ kiện gốc vẫn lưu trong hồ sơ để đối chiếu. Số lượng chấp nhận `1,5` hoặc `1.5`; ô nhập không hợp lệ phải được sửa trước khi lưu hoặc in.

## Logo và serial máy kéo · phiên bản 1.2.0

Logo Hisa Elevator do người dùng cung cấp được giữ nguyên, nhúng vào thanh bên và trang Thông tin phần mềm. Biểu tượng của cửa sổ và file `.exe` dùng cùng logo, có các kích thước 16–256 pixel. File chạy đã chứa ảnh và icon; không cần giữ ảnh ở thư mục Downloads để sử dụng. Nguồn ảnh và bộ tạo icon C# được giữ ở `src\Hisa.App\Assets` và `tools\LogoBuilder`.

Khi phiếu có **máy kéo đang chọn** và bạn xác nhận **Đã xuất**, ứng dụng tự mở bảng **Serial máy kéo xuất kho** trước khi ghi xuất hàng. Bảng hiển thị số hợp đồng, công trình, model máy và số thứ tự từng máy. Nếu một dòng có số lượng 2 thì phải nhập 2 serial. Có thể dán nhiều serial theo thứ tự, mỗi dòng một serial, hoặc nhập trực tiếp từng ô.

- Không xác nhận xuất khi thiếu serial, trùng serial trong phiếu hoặc serial đã xuất theo phiếu khác đang Đã xuất. Cảnh báo ghi rõ phiếu/hợp đồng liên quan. Serial được giữ dưới dạng chữ, bao gồm số 0 đầu và dấu gạch nối.
- Phiếu chờ có thể nhập serial trước bằng **Serial máy kéo · nhập / sửa**. Dữ liệu chỉ được ghi khi lưu phiếu; đóng bảng bằng Quay lại không đổi phiếu. Những phiếu cũ không có serial vẫn đọc được; khi xuất hoặc lưu lại ở trạng thái Đã xuất, cần bổ sung serial máy kéo.
- Khi sửa số lượng/model hoặc bỏ dòng khỏi danh sách bàn giao, bảng serial được dựng lại theo đúng danh sách đang chọn. Không tự chuyển serial sang model khác.
- Trong **Phiếu bàn giao**, dùng **Xem serial máy kéo** hoặc tìm bằng serial ở ô tìm kiếm để tra cứu. Bản in phiếu, bản in báo cáo ngày và báo cáo Excel có kèm serial.
- `Thành phẩm xuất kho.xlsx` ghi serial theo từng hợp đồng ở cột Ghi chú. `XUẤT HÀNG\Sổ theo dõi xuất hàng.xlsx` có bảng **Serial máy kéo**, một dòng mỗi máy, cùng mã phiếu/ngày/trạng thái/công trình. Phiếu hủy được giữ trong bảng serial với đúng trạng thái để tra vết, và được gỡ khỏi danh sách xuất thực tế theo quy tắc hiện có.

## Các chức năng cập nhật 1.1.0

- **Thông tin phần mềm** trên thanh bên hiển thị đúng tên, người tạo, số điện thoại và email; có nút sao chép liên hệ.
- **Mẫu phiếu in → Tạo mẫu phiếu mới** tạo một mẫu có mã riêng. Có thể chọn ba bố cục, sửa nội dung, tên cột, chữ ký, khổ giấy, cỡ chữ và căn lề; **Sửa mẫu / căn lề** chỉnh mẫu đã lưu. Có thể tạo hoặc sửa mẫu ngay trên màn hình lập phiếu.
- **Phiếu bàn giao → Tạo phiếu / Sửa phiếu** cho chỉnh danh sách vật tư, đơn vị, số lượng, ghi chú, ngày và mẫu in. Xem trước/in trước khi lưu. Khi đổi hợp đồng hoặc bộ lọc, cần tải lại vật tư để tránh lưu nhầm danh sách cũ.
- Có đủ bốn trạng thái **Chờ xuất – Đã xuất – Tạm hoãn – Đã hủy**, chuyển bằng các nút trong danh sách hoặc ngay khi sửa phiếu. Tạm hoãn/hủy cần lý do; lịch sử thời điểm và lý do được lưu, có nút **Lịch sử trạng thái** để tra cứu.
- Ngay khi nhập số hợp đồng, vùng cảnh báo kiểm tra mã thiếu dữ liệu, đã xuất, đã bàn giao, đã có phiếu chờ, đang tạm hoãn hoặc đã hủy. **Xem toàn bộ cảnh báo** hiển thị công trình, mã phiếu, ngày và lý do. Trước khi lưu hoặc đánh dấu đã xuất, ứng dụng kiểm tra lại lịch sử hiện tại. Sửa phiếu không báo trùng với chính phiếu đó.
- Phiếu chờ/phiếu đã in chỉ được cảnh báo là **đã có phiếu bàn giao**; chỉ trạng thái **Đã xuất** hoặc lịch sử bàn giao phù hợp mới xác nhận đã xuất/bàn giao. Các công trình trùng số hợp đồng được xét riêng.

Các kiểm tra phiên bản sử dụng dữ liệu tạm và không ghi vào workbook thực tế.

Ứng dụng cửa sổ Windows viết bằng **C#, .NET 10 và WPF**. Dữ liệu Excel được đọc trực tiếp bằng Open XML; các thao tác ghi dùng ClosedXML. Chương trình không cần Python, máy chủ web hay Microsoft Excel để hoạt động.

## Mở phần mềm

Nhấp đúp **`publish\HISA Kho Vat Tu.exe`** trong thư mục HISA APP. Bản phát hành đã kèm .NET cho Windows 64 bit.

Trong **Cấu hình & sao lưu**, chọn một thư mục nguồn. Ứng dụng quét Excel trong thư mục đó và tự nhận diện `Tổng hợp.xlsx`, `KT tổng hợp.xlsx`, `Thành phẩm xuất kho.xlsx` trong thư mục nguồn hoặc các thư mục cha gần đó. Dữ liệu ứng dụng được lưu ngoài mã nguồn tại `APP KHO\data`; dữ liệu cũ được sao chép khi chuyển phiên bản và không bị xóa.

Vào **Cấu hình & sao lưu** để kiểm tra đường dẫn. Nếu sử dụng bản HISA cũ đồng thời, hai bản có thể cùng ghi các workbook chung; nên thoát bản cũ trước khi cập nhật dữ liệu hoặc xuất hàng bằng bản mới.

## Chức năng

- **Tổng quan kho:** nút truy cập theo kiện, theo hợp đồng; thống kê sáu nhóm vật tư; phiếu chờ, hoãn, đã xuất, hủy.
- **Tra cứu hợp đồng:** nhập một hoặc nhiều mã, mỗi dòng hoặc dấu phẩy/chấm phẩy một mã. Tìm chính xác; có tìm thêm theo công trình/công ty, ngày cont về và ngày xuất. Chọn nhiều nhóm hoặc từ khóa vật tư theo điều kiện khớp ít nhất một từ khóa/nhóm.
- **Kiện Hưng Yên:** mỗi khối HƯNG YÊN riêng trong Excel là một kiện. Hai khối giống số hợp đồng/ngày/công trình vẫn giữ thành hai kiện. Nhấp đúp để xem toàn bộ vật tư.
- **Tạo phiếu bàn giao:** nạp vật tư theo mã hợp đồng; chọn dòng in, sửa số lượng/ghi chú, thêm dòng, xem trước và lưu chờ hoặc ghi nhận đã xuất. Giữ đầy đủ danh sách vật tư của các kiện đã chọn trong hồ sơ phiếu, gồm cả dòng không in.
- **Kế hoạch xuất hàng:** nhập nhiều số hợp đồng một lần rồi tích từng công trình; mỗi công trình có một phiếu chờ và mã riêng. Mã thiếu dữ liệu được báo riêng; có thể chủ động chọn tạo phiếu trống để chờ bổ sung, không thể đánh dấu đã xuất khi chưa có vật tư.
- **Phiếu bàn giao:** lọc chờ, hoãn, đã xuất, hủy; sửa, in lại, chuyển từng phiếu sang đã xuất/hoãn/hủy hoặc đưa về chờ. Lý do và thời điểm được lưu. Khi hoãn, hợp đồng được gắn cảnh báo; khi trạng thái thay đổi, cảnh báo từ phiếu được cập nhật.
- **Cont đã nhập kho:** nhập nhiều mã và ngày cont về, đối chiếu số lượng/vật tư với KT. Mã khớp được ghi dù còn mã chưa có nguồn; ngày khác và các công trình khác được giữ nguyên. Nhập lại cùng hợp đồng/công trình/ngày thay thế đúng nhóm để tránh nhân đôi.
- **Đối chiếu KT:** đồng bộ ngày cont về và vật tư của các công trình có trong KT vào trang `chi tiết`; giữ các công trình khác và các trang không liên quan. Có bản sao lưu trước khi ghi.
- **Hoãn / hủy lấy hàng:** ghi trạng thái cho nhiều mã, kèm công trình và lý do. Cảnh báo được đưa vào tra cứu và kiểm tra trước khi lưu phiếu.
- **Mẫu phiếu in:** sửa hoặc sao chép mẫu nội bộ và mẫu riêng từng hợp đồng. Chỉnh nội dung, tên cột, chữ ký, tiền tố mã phiếu, A4/A5, chiều giấy, phông/cỡ chữ, lề và căn chỉnh. Bản in tự phân trang, lặp tiêu đề và có mã phiếu/số trang. Dùng **Microsoft Print to PDF** trong hộp thoại in nếu cần PDF.
- **Báo cáo ngày:** số phiếu chờ/hoãn/đã xuất/hủy, vật tư từng phiếu, tổng sáu nhóm, cảnh báo hợp đồng, phiếu hoãn còn chờ và lịch sử xuất theo sổ. Có xem trước/in và xuất Excel. Ngày báo cáo là ngày kế hoạch/ngày xuất của phiếu; trạng thái thể hiện hiện tại.
- **Sao lưu:** tạo ZIP chứa cấu hình, lịch sử, mẫu phiếu và các workbook chuẩn. Bản sao trước khi ứng dụng ghi Excel nằm trong `.hisa-backups` cạnh workbook.

## Quy tắc dữ liệu

Nguồn vật tư tra cứu/lập phiếu là **Tổng hợp.xlsx**. Trang `Cont đi Hưng Yên` lấy từ danh mục cont; trang `chi tiết` theo dõi ngày cont về và danh mục nhận thực tế. Khi một kiện có cả hai nguồn, danh sách đã nhập ở `chi tiết` được ưu tiên. Các công trình cùng mã hợp đồng nhưng khác tên/công ty vẫn có hồ sơ riêng. Mã có `(1)`, `(2)` được giữ phân biệt; tiền tố BG được bỏ, số 0 đầu/định dạng số được đối chiếu thống nhất; mã thập phân được giữ đúng giá trị số.

Khi **Đã xuất**, phần mềm cập nhật `Thành phẩm xuất kho.xlsx` theo mẫu có sẵn và `XUẤT HÀNG\Sổ theo dõi xuất hàng.xlsx`, gồm đơn, vật tư bàn giao, toàn bộ kiện và kế hoạch/trạng thái. Mỗi dòng do ứng dụng ghi có mã liên kết phiếu. Cập nhật lại không ghi trùng; hủy/đưa về chờ chỉ gỡ các dòng của phiếu liên quan. Các dòng lịch sử khác được giữ nguyên. Nếu Excel khóa file, phiếu vẫn được lưu, hiển thị cảnh báo chờ ghi sổ và tự thử lại.

Số **hợp đồng**, **công trình**, **kiện** và **dòng vật tư** là bốn chỉ tiêu riêng. Một hợp đồng có thể thuộc nhiều nhóm vật tư/trạng thái nên không cộng số ở các nhóm để tính tổng hợp đồng. Mã không có kiện Hưng Yên được báo riêng; lịch sử xuất không được dùng để dựng thêm vật tư.

## Tốc độ và tự cập nhật

Ứng dụng mở dữ liệu lưu nhanh trước, sau đó kiểm tra Excel trong nền. Bộ đọc chỉ lấy các trang/cột cần thiết, lưu chỉ mục theo hợp đồng và chỉ đọc lại các file cont đã thay đổi. Các bảng dùng ảo hóa dòng/cột. Mặc định kiểm tra nguồn mỗi 10 giây; thay đổi cont cập nhật trang cont, thay đổi KT cập nhật trang chi tiết. Lần mở đầu chỉ đọc workbook hiện có; không tự viết lại ngay khi khởi động. Có thể tắt từng loại đồng bộ trong cấu hình.

Kiểm tra trên bản sao dữ liệu thực tế khoảng 16.600 dòng: đọc và ghép dữ liệu khoảng 2 giây trên máy hiện tại; tốc độ thực tế còn phụ thuộc ổ đĩa, OneDrive và độ lớn workbook. Cập nhật/ghi Excel chạy nền, có chỉ báo tiến độ. Khi nguồn đọc lỗi/rỗng, ứng dụng giữ dữ liệu đã có và hiện cảnh báo thay vì xóa bảng tổng hợp.

Phím tắt: **Ctrl+F** tra cứu, **Ctrl+N** phiếu mới, **F5** cập nhật dữ liệu.

## Mã nguồn và đóng gói

Giữ toàn bộ thư mục `HISA APP` để tiếp tục sửa. Các phần chính:

| Tệp | Chức năng |
| --- | --- |
| `src\Hisa.App\MainWindow.xaml*`, `Theme.xaml` | Giao diện Windows và thao tác người dùng |
| `Models.cs` | Mô hình, chuẩn hóa số HĐ và từ khóa vật tư |
| `WarehouseService.cs` | Chỉ mục, tra cứu/báo cáo, lịch sử, đồng bộ nền |
| `ExcelStore.cs` | Đọc/ghi và đối chiếu workbook |
| `PrintService.cs`, `TemplateEditorWindow.cs` | Phân trang, xem trước/in và sửa mẫu |
| `SelfTests.cs`, `ExcelDiagnostics.cs` | Kiểm tra nghiệp vụ bằng dữ liệu tạm |
| `data` | Cấu hình, phiếu, mẫu, trạng thái và bộ nhớ đệm |

Máy sửa/biên dịch cần **.NET SDK 10**. Chạy `BUILD.ps1` để tạo lại bản tự chứa vào `publish`. Mở file `src\Hisa.App\Hisa.App.csproj` bằng Visual Studio hoặc IDE hỗ trợ C#.

Kiểm tra không đụng workbook thực tế:

```powershell
& '.\publish\HISA Kho Vat Tu.exe' --self-test --output "$env:TEMP\hisa-csharp-selftest.json"
```

Mở cửa sổ với dữ liệu thử riêng:

```powershell
& '.\publish\HISA Kho Vat Tu.exe' --demo
```

Kết quả kiểm tra và đo tốc độ bản phát hành được lưu trong `verification`.
