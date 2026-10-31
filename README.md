## HISA v1.9.9 · Chọn một thư mục Kho HISA, tự nhận 6 file Excel theo đúng nhóm

```text
Kho HISA/
├── Kho chính/
│   ├── Tổng hợp.xlsx
│   ├── KT tổng hợp.xlsx
│   └── Thành phẩm xuất kho.xlsx
├── CONT ĐI/
└── Danh sách sản xuất/
    ├── MÁY KÉO.xlsx
    ├── TỦ ĐIỆN.xlsx
    └── Đầu cửa car T10.2026.xlsx
```

**Cài đặt một lần:** tại **Cấu hình nguồn dữ liệu → Chọn Kho HISA**, chọn `Kho HISA` (hoặc một trong các thư mục con `Kho chính`, `CONT ĐI`, `Danh sách sản xuất`) rồi kiểm tra 6 đường dẫn tự nhận; bấm **Lưu cấu hình & tải lại**.

- Tự liên kết `Tổng hợp.xlsx`, `KT tổng hợp.xlsx` và `Thành phẩm xuất kho.xlsx` từ **Kho chính**, không chọn riêng từng file.
- **Chỉ** `CONT ĐI` được quét là nguồn kiện đang chờ; không quét bừa `Kho HISA` làm nguồn cont, không đưa ba file sản xuất vào `Tổng hợp.xlsx`.
- Tự phát hiện ba file sản xuất ở **Danh sách sản xuất** để tra cứu theo hợp đồng Gemini/Zalo và theo dõi máy kéo Hưng Yên; chỉ đọc, không tự ghi/di chuyển. Nếu có nhiều file **Đầu cửa car** khác tháng, giữ đường dẫn đã chọn trước; yêu cầu chọn tay khi chưa biết tháng.
- `Đã nhập kho` và `Hàng gửi` vẫn được tổ chức ngang cấp với `CONT ĐI` nếu ứng dụng phân loại phát sinh nguồn, tránh lồng bên trong `CONT ĐI`. 
- Không đổi dữ liệu kho, phiếu bàn giao, lịch sử máy kéo, Gemini API Key hay nội dung Excel. Bản cài Windows kiểm thử hồi quy độc lập và cài đè.

---

## HISA v1.9.8 · Giữ các file Tủ điện, Máy kéo, Đầu cửa car ngoài thư mục Tổng hợp

**Không để ba file sản xuất chung thư mục với `Tổng hợp.xlsx` hoặc trong `CONT ĐI`.** Mỗi file dùng đường dẫn đầy đủ riêng, không bắt buộc chung thư mục với nhau. Ví dụ:

```text
D:\KhoHISA\
├── Kho chính\Tổng hợp.xlsx
├── CONT ĐI\
└── Danh sách sản xuất\
    ├── TỦ ĐIỆN.xlsx
    ├── MÁY KÉO.xlsx
    └── Đầu cửa car T10.2026.xlsx
```

Ba danh sách sản xuất có thể nằm trong **bất kỳ thư mục độc lập nào** trên máy hoặc ổ mạng. Tại **Cấu hình nguồn dữ liệu → Danh sách thiết bị theo hợp đồng**, bấm **Chọn Excel** cho từng file, sau đó **Lưu cấu hình & tải lại**. Việc thay thư mục nguồn `CONT ĐI` hoặc đổi vị trí `Tổng hợp.xlsx` **không tự thay đường dẫn** của ba danh sách. HISA chỉ đọc file nguồn, không tự di chuyển/sao chép hay chỉnh sửa nội dung.

- Khi lưu, nếu file thiết bị nằm cùng thư mục `Tổng hợp.xlsx`, trong `CONT ĐI`, hoặc dùng chung một file cho hai danh sách, HISA hiển thị lỗi rõ và yêu cầu chọn lại file ở thư mục độc lập.
- Để tương thích cấu hình cũ, HISA vẫn có thể mở và hiển thị các file đã liên kết trước đó nhưng cảnh báo ở trang cấu hình; danh sách sản xuất bị đặt nhầm vào thư mục `CONT ĐI` không được đưa vào dữ liệu cont/nhập kho.
- Cả ba nguồn vẫn dùng để tra cứu vật tư theo hợp đồng từ ảnh Gemini và màn hình theo dõi máy kéo gia công Hưng Yên. Danh sách sản xuất không tự xác nhận trạng thái thực nhận hoặc bàn giao.

---

## HISA v1.9.7 · Theo dõi máy kéo gửi Hưng Yên gia công và bàn giao

**Quy trình thao tác mới:** Mở **Máy kéo · gửi / làm / bàn giao** từ menu trái. HISA đọc các dòng của file **MÁY KÉO.xlsx** đã liên kết tại Cấu hình nguồn dữ liệu (file chỉ đọc). Mỗi dòng / máy / số seri được đối chiếu riêng với số HĐ, công trình, đơn vị và loại máy.

- Bước 1: Chọn máy kéo → nhập ngày thực tế và ghi chú → **Xác nhận đã gửi Hưng Yên**.
- Bước 2: Sau khi xác minh gia công xong → **Xác nhận đã làm xong**.
- Bước 3: Khi bàn giao có chứng từ → **Xác nhận đã bàn giao**.
- Có lọc nhanh theo số HĐ, công trình, seri; lọc theo trạng thái Chưa gửi / Đã gửi Hưng Yên / Đã làm / Đã bàn giao và xem **Lịch sử dòng chọn**.
- Nếu máy kéo xuất hiện trong Excel không có nghĩa là đã gửi hoặc đã hoàn thành; ghi chú kiểu `DXH` không tự chuyển thành bằng chứng đã giao. Chỉ người thao tác bấm xác nhận mới đổi trạng thái.
- Ngày chuyển giai đoạn không được trước giai đoạn trước hoặc sau ngày hiện tại; không nhảy giai đoạn hay tự quay lại trạng thái đã bàn giao.
- Lưu lịch sử trong `motor_hung_yen_tracking.json` ở thư mục dữ liệu HISA, sao lưu trong ZIP cùng các trạng thái và phiếu, tạo `.bak` mỗi khi thay đổi. Nếu dòng Excel bị xóa, lịch sử vẫn còn để đối chiếu nhưng không cho đổi trạng thái tới khi xác minh nguồn. Không sửa file `MÁY KÉO.xlsx`, `Tổng hợp.xlsx`, trạng thái phiếu hay tồn kho.

**Cài đặt:** Cài đè HISA phiên bản mới, giữ nguyên nguồn máy kéo đã chọn ở v1.9.6. Nếu danh sách trống, vào **Cấu hình nguồn dữ liệu → Danh sách thiết bị theo hợp đồng → Máy kéo** chọn file Excel trên máy rồi lưu cấu hình. Các giai đoạn ban đầu mặc định **Chưa ghi nhận gửi** cho đến khi người vận hành xác nhận.

---

## HISA v1.9.6 · Liên kết danh sách Tủ điện, Máy kéo và Đầu cửa car

Ba file Excel sản xuất tùy chọn **khác** với `Tổng hợp.xlsx` và dữ liệu kho:
- **TỦ ĐIỆN.xlsx:** đọc trang `Lịch tủ điện tổng hợp`, số HĐ, công trình, công ty, seri tủ, điện áp phanh, thông số máy kéo và lịch. Không tự suy ra số lượng nếu cột không có dữ liệu.
- **MÁY KÉO.xlsx:** đọc trang `DS máy kéo`, tên vật tư, số lượng, số lô/seri và thông tin cont. Bỏ qua trang in tem, danh mục máy kéo mẫu và các bảng cho mượn để không nhập trùng.
- **Đầu cửa car T10.2026.xlsx:** đọc các trang có cột `Số HĐ`, `Tên Vật tư` theo từng ngày, ngày xuất xưởng, mã VT, số lượng và đơn vị. Bỏ qua dòng `Tổng` và trang không đúng cấu trúc.

**Thiết lập:** mở **Cấu hình nguồn dữ liệu → Danh sách thiết bị theo hợp đồng**, chọn lần lượt ba tệp rồi bấm **Lưu cấu hình & tải lại**. Sau khi Gemini phân tích ảnh Zalo, chọn từng hợp đồng để thấy danh sách thiết bị bên dưới phần vật tư kho, hoặc bấm **Xem 3 danh sách thiết bị** để xem tất cả hợp đồng nhận diện theo từng nguồn.

**An toàn dữ liệu:** chỉ đọc danh sách sản xuất; không tự nhập kho, không ghi `Đã xuất` hay sinh phiếu khi chưa duyệt. Nếu số HĐ trùng nhưng công trình hoặc đơn vị khác nhau, phần mềm hiển thị cảnh báo đối chiếu; không dùng những dòng đó như vật tư đã nhận. Không đưa các file khách hàng lên GitHub.

---

## HISA v1.9.5 · Tích một lần, xem toàn bộ vật tư từ ảnh Zalo

- Hai bảng **Cont dự kiến về** và **Hợp đồng làm phiếu** dùng ô tích thực sự tương tác bằng **một lần nhấp**, không cần nhấp để vào chế độ sửa ô. Màn đối chiếu **từng kiện thực nhận** cũng dùng ô tích một lần.
- Chọn dòng hợp đồng bất kỳ để xem **danh sách vật tư, số lượng, đơn vị, ngày cont đi/ngày thực nhận** từ nguồn Excel ngay bên dưới bảng; có nút **Xem toàn bộ vật tư** của tất cả các HĐ trong lịch Zalo.
- HISA đối chiếu chính xác **số hợp đồng + công trình + công ty** và vẫn giữ riêng từng kiện. Cùng số HĐ có nhiều T1/T2 nhưng thông tin AI chưa rõ sẽ **cảnh báo, không tự lấy gộp hàng**. HĐ thiếu trong Excel hiển thị cảnh báo, không sinh vật tư giả.
- Các lựa chọn nhận cont/chọn phiếu không tự ghi dữ liệu kho. Phải duyệt lịch, kiểm tra từng kiện và xác nhận thực tế trước khi cập nhật `Tổng hợp.xlsx`. Cài đè giữ dữ liệu và Gemini API Key.

---

## HISA v1.9.4 · Phân loại file CONT ĐI an toàn

Cấu trúc thư mục mới, ví dụ khi chọn nguồn `D:\Kho\CONT ĐI`:

```text
D:\Kho\
├── CONT ĐI\          (file kiện chờ về; chưa nhập thực tế)
├── Đã nhập kho\     (file có tất cả kiện đã thực sự ghi vào Tổng hợp.xlsx)
└── Hàng gửi\        (file hàng gửi, không phải nhập kho)
```

- File **Hàng gửi** được đưa sang thư mục cùng cấp với CONT ĐI, vẫn giữ cấu trúc thư mục ngày.
- File **Đã nhập kho** chỉ được chuyển sau khi có dấu vết nguồn từng kiện trong phần chi tiết của `Tổng hợp.xlsx`. Nếu một file có nhiều kiện mà chỉ một số đã nhận, vẫn để nguyên trong CONT ĐI.
- Sau khi nâng cấp, các file do bản cũ đặt nhầm vào `CONT ĐI\Đã nhập kho` được kiểm tra: file có toàn bộ kiện được xác nhận mới chuyển sang thư mục Đã nhập kho cùng cấp, file chưa được xác nhận quay về CONT ĐI. File cũ `CONT ĐI\Hàng gửi` chuyển ra thư mục Hàng gửi cùng cấp. Không ghi đè file trùng tên; hiển thị cảnh báo.
- Khi đồng bộ, HISA vẫn đọc file ở thư mục Đã nhập kho cùng cấp để bảo toàn lịch sử, ID kiện và không làm trống nguồn cont.

---

## HISA v1.9.3 · Hai thao tác từ ảnh Zalo

Chọn ảnh Zalo → **Phân tích với Gemini** → kiểm tra ngày, công trình và hợp đồng → **Lưu kế hoạch đã duyệt**. Sau đó có hai danh sách độc lập:

1. **Cont dự kiến về:** chỉ khi cont *thực tế đã về* mới tích **Cont đã về?** và chọn **Đối chiếu cont đã về → Ghi Excel**. Ở bước tiếp theo, kiểm tra ngày thực nhận và tích **từng kiện** đúng nguồn. Không có kiện nào tự tích nhận. Chỉ ghi vào `Tổng hợp.xlsx` sau xác nhận và sao lưu.
2. **Hợp đồng dự kiến bàn giao:** tích **Làm phiếu** và chọn **Lập phiếu từ hợp đồng đã tích**, kiểm tra vật tư, tạo **phiếu chờ xuất**. Không tự xác nhận Đã xuất.

Các tính năng thêm hợp đồng thủ công và cảnh báo chi tiết được thu gọn bên dưới hai danh sách. Lịch dự kiến không phải bằng chứng hàng thực nhận. Bản cài đè vẫn giữ dữ liệu và Gemini API Key.

---

### Khôi phục chọn nguồn dữ liệu · HISA v1.9.2

- Chọn ngay **Cấu hình nguồn dữ liệu** từ thanh menu bên trái. Mục **Nguồn dữ liệu Excel → Chọn thư mục nguồn** hiện ở đầu trang, trước Gemini AI.
- Nếu chưa có thư mục nguồn được lưu nhưng `Tổng hợp.xlsx` hiện tại tồn tại, giao diện đề xuất thư mục của tệp đó mà không tự thay đổi cấu hình.
- Nhấn **Lưu cấu hình & tải lại** sau khi chọn thư mục; đường dẫn nguồn và các cài đặt đồng bộ chỉ thay đổi khi lưu. Không xóa dữ liệu Excel hoặc API Key.


### Cài đặt và cập nhật phiên bản 1.9.1

- Mở HISA → nút **↑ Cập nhật** trên thanh công cụ → **Kiểm tra cập nhật** → **Cài bản đã tải**. Bản tải xuống phải khớp SHA-256; chỉ cài khi người dùng xác nhận.
- Đối với HISA 1.8.1 hoặc bản cũ không hiện nút cập nhật: mở [GitHub Releases](https://github.com/infiniti-money/Xuat-hang/releases/latest), tải tệp `HISA-Kho-Vat-Tu-<phiên-bản>-Setup.exe`, đóng HISA và chạy bộ cài đè lên phiên bản hiện có.
- Nếu Windows hiện yêu cầu UAC, chọn Đồng ý để cài trong Program Files. Nếu từ chối, chương trình hiện tại không bị xóa. Không cần gỡ cài đặt trước và **không xóa thư mục Tài liệu\\HISA Kho Vat Tu**.
- Trong trường hợp bộ cài lỗi, giữ lại phiên bản đang sử dụng và tải lại từ GitHub Releases; không sao chép đè file EXE riêng lẻ trong Program Files.

# Quản lý kho — C# / .NET 10 · phiên bản 1.9.0

**Người tạo:** Trịnh Phương Tân  
**SĐT:** 0867899987  
**Email:** infinitimoney@icloud.com

## Bộ cài đặt Windows tiếng Việt và cập nhật tự động · phiên bản 1.3.0

- Vào [GitHub Releases](https://github.com/infiniti-money/Xuat-hang/releases/latest) và tải `HISA-Kho-Vat-Tu-1.9.0-Setup.exe`. Bộ cài sử dụng tiếng Việt và mặc định cài vào `C:\\Program Files\\HISA Kho Vat Tu`. Có biểu tượng trên Desktop và Start Menu, hỗ trợ gỡ cài.
- Giao diện chính hiển thị logo HISA. Phần mềm tự kiểm tra GitHub Releases khi mở, tải và xác minh bộ cài (SHA-256) trong nền; khi đóng ứng dụng sẽ nâng cấp yên lặng rồi mở lại.
- Chế độ `/VERYSILENT` ẩn trình hướng dẫn cài đặt nhưng **không thể bỏ qua cửa sổ UAC của Windows** khi nâng cấp ứng dụng cài trong Program Files. Nếu không cấp quyền quản trị, phiên bản cũ và dữ liệu vẫn giữ nguyên.
- Tài liệu ứng dụng: **Thư viện → Tài liệu → HISA Kho Vat Tu → APP KHO → data**.
- Sao lưu ZIP: **Thư viện → Tài liệu → HISA Kho Vat Tu → Backups**; các bản sao Excel trước khi ghi nằm trong thư mục **Backups → Excel**.
- Gỡ phần mềm hoặc nâng cấp không xóa dữ liệu và bản sao lưu trong Thư viện. Khi nâng cấp từ bản cũ, các file dữ liệu tìm thấy tại vị trí cũ sẽ được chuyển sang nếu file đích chưa tồn tại.


### Quy tắc phân biệt kiện Hưng Yên khi ghi Tổng hợp.xlsx
- **Ngày cont đi khác nhau luôn là hai kiện riêng**, kể cả khi cùng số hợp đồng, công trình, chủng loại và số lượng vật tư.
- Khi ghi vào trang `chi tiết` của `Tổng hợp.xlsx`, giữ nguyên tên công trình và nối ngày cont đi dạng ngắn trong ngoặc: `Công trình A T1 (26.10)`, `Công trình A T1 (27.10)`. Không ghi lặp ngày nếu tên đã có đúng hậu tố đó.
- Hai cột ẩn AA/AB ghi số cont và mã kiện nguồn; cột ẩn **AC ghi ngày cont đi đầy đủ yyyy-MM-dd** để phân biệt cả khác năm có cùng ngày/tháng. Ba cột đều kiểm tra trước khi ghi, không ghi đè dữ liệu người dùng.
- Ngày cont về và ngày cont đi được quản lý riêng; không tự chuyển hàng dự kiến về thành hàng đã về.


**Mỗi kiện vật lý là một bản ghi riêng**, không gộp theo số hợp đồng:
- Cùng số HĐ nhưng **khác số lượng hoặc danh mục vật tư** → giữ các kiện riêng.
- Cùng HĐ, cùng số lượng nhưng tên công trình có **T1 / T2, Thang 1 / Thang 2 hoặc (1) / (2)** → các kiện riêng.
- Cùng HĐ, cùng vật tư và số lượng nhưng **cont đi khác nhau** → các kiện riêng, có ID kiện nguồn và số cont để tránh ghi đè.
- Ảnh Gemini chỉ là lịch dự kiến: khi cont về thực tế, chọn đúng **từng kiện**, đối chiếu ngày cont đi / số cont / số HĐ / công trình và xác nhận mới ghi vào chi tiết của `Tổng hợp.xlsx`.
- Bản mới ghi mã cont và ID kiện nguồn vào hai cột ẩn **AA / AB** của trang `chi tiết`, giữ nguyên vùng hiển thị A:J. Nếu các cột ẩn đã có dữ liệu của ứng dụng khác, HISA dừng để tránh ghi đè.
- Những nguồn không xác định được kiện thuộc cont nào được cảnh báo và **không tự đoán**. Các dòng lịch chưa xác nhận không được nhập kho.

## HISA v1.9.0 – Lịch nhập/xuất từ ảnh Zalo hoặc số hợp đồng nhập tay

Mở **Trung tâm Gemini AI → Đọc lịch Zalo / Nhập hợp đồng**. Đây là một màn hình chung cho **hai danh sách độc lập**: (1) cont dự kiến về kho Hưng Yên và (2) hợp đồng dự kiến bàn giao. Các số HĐ có trên ảnh vẫn được Gemini đọc; những số phát sinh chưa có trên ảnh có thể **dán nhiều dòng hoặc thêm thủ công vào cùng lịch**, không cần API Key khi nhập tay.

- **Tạo lịch bằng tay** khi chưa có ảnh; **Mở lịch đã duyệt** để tiếp tục phiên trước, kể cả lịch chỉ có hợp đồng xuất.
- Phần **Thêm số hợp đồng thủ công**: chọn nhập kho hay bàn giao, dán một mã mỗi dòng, nhập công trình/đơn vị nếu biết, số cont và ngày cont đi đối với hàng về, ghi chú và ưu tiên. Nếu khớp đúng một danh tính trong nguồn, HISA tự điền thông tin; nếu cùng HĐ nhiều T1/T2 hoặc chưa có nguồn, giữ trạng thái **Chưa xác nhận**, không đoán sai công trình.
- Hai đợt cùng số HĐ nhưng khác cont/ngày cont đi là **hai dòng lịch riêng**. Khi nhận thật, chọn HĐ ở cột **Chọn nhận**, sau đó đối chiếu **từng kiện vật lý**, ngày cont đi và mã cont trên màn hình xác nhận cuối; chỉ ghi `Tổng hợp.xlsx` sau khi người dùng duyệt.
- Ở **Phần 2 – Xuất kho**, tích cột **Làm phiếu** trên HĐ ảnh Zalo hoặc nhập tay; kiểm tra dữ liệu hợp đồng, vật tư/serial máy kéo, rồi tạo **phiếu chờ xuất**, chưa chuyển thành Đã xuất.
- Sau bất kỳ chỉnh sửa ngày, thông tin nguồn hoặc thêm HĐ, lịch phải **duyệt lại trước khi ghi hoặc lập phiếu**; chuyển trạng thái chọn không được hiểu là phê duyệt tự động.
- **Đã có LX** vẫn là *đã có lịch xuất*, không phải đã có lái xe, đã bàn giao hay đã xuất hàng.
- Chức năng nhập tay vẫn hoạt động khi Gemini không có khóa API; Gemini API Key hiện có không bị thay đổi, các file Excel nguồn không bị chỉnh sửa chỉ vì người dùng nhập lịch.

## HISA v1.8.0 – Giao diện tập trung theo quy trình thực tế

**6 mục chính thay cho menu nhiều mục trùng:** Tổng quan · Điều hành xuất hàng · Cont & kiện Hưng Yên · Tra cứu hợp đồng · Trung tâm Gemini AI · Quản lý & lịch sử.

- **Quản lý & lịch sử** gom truy cập **Trạng thái hợp đồng (hoãn/hủy)**, **Phiếu bàn giao**, **Lịch sử xuất & báo cáo**, **Cấu hình & sao lưu**, **Mẫu phiếu in** và **Thông tin & cập nhật** vào một màn hình. Nút Phiếu bàn giao trong Điều hành xuất hàng vẫn là lối tắt tới **cùng danh sách phiếu**, không tạo bản sao dữ liệu.
- Các thao tác ghi kho, xác nhận xuất và xóa trạng thái vẫn nằm trong màn nghiệp vụ với bước xác nhận. Không di chuyển hoặc gộp vật lý file Excel, JSON hay dữ liệu Windows Credential Manager khi thay menu.
- Bản giao diện này **không thêm chức năng phục hồi ZIP**; chờ bản phục hồi có kiểm thử an toàn riêng.

1. Mở HISA → thấy lịch dự kiến hôm nay/ngày mai nếu có, kiện cần chuẩn bị và cont dự kiến về. Nếu chưa có lịch, phần mềm hỏi anh nhập ảnh Zalo hoặc nhập thủ công.
2. Vào **Điều hành xuất hàng** → nạp lịch AI đã duyệt hoặc thêm HĐ phát sinh → đối chiếu chuẩn bị vật tư → lập phiếu chờ → chỉ xác nhận sau bàn giao.
3. Vào **Cont & kiện Hưng Yên** → xem lịch cont đi / dự kiến về → tích đúng kiện thực tế về → duyệt ghi Tổng hợp.xlsx; có lối nhập cont thủ công và rà soát kiện trùng số HĐ nâng cao.
4. Vào **Trung tâm Gemini AI** để trò chuyện hoặc đọc ảnh Zalo. Chỉ gửi tóm tắt dữ liệu kho khi bật cho phép; khóa Gemini lưu ở Cấu hình, không nhập lại khi nâng cấp.
5. Vào **Báo cáo & tiện ích** để xem báo cáo, phiếu và in mẫu. Các tính năng cũ không bị xóa, chỉ gom điều hướng.

Giữ nguyên quy tắc: cùng HĐ nhưng khác cont, ngày cont đi, số lượng, quy cách hoặc T1/T2 là các kiện độc lập. Chưa cập nhật dữ liệu không tự coi là thiếu; chưa có phiếu xác nhận không coi là đã xuất.

## Trợ lý Gemini AI trực tiếp trong phần mềm (v1.7.0)

Mở **Trợ lý Gemini AI** từ thanh điều hướng hoặc nút trên Tổng quan kho để đặt câu hỏi bằng tiếng Việt. Dùng Gemini API Key đã lưu trong **Cấu hình & sao lưu**; không có kết nối GPT. Có thể hỏi cách làm phiếu, quy tắc phân biệt kiện, dự kiến xuất và tra cứu vật tư.

- **Mặc định chỉ gửi câu hỏi và lịch sử trò chuyện** đến Gemini qua Internet.
- Tích **Cho phép Gemini đọc tóm tắt dữ liệu kho liên quan** khi cần hỏi dữ liệu thực tế. HISA chỉ gửi một bản tóm tắt có giới hạn; câu hỏi có mã HĐ đầy đủ mới được đính kèm vật tư của HĐ đó. Không gửi file Excel nguyên bản, đường dẫn và serial. Bật/tắt chế độ này bắt đầu phiên chat mới.
- Lịch sử trò chuyện chỉ nằm trong bộ nhớ đang chạy, bấm **Xóa hội thoại** để làm mới. Không có lưu hội thoại hoặc ghi dữ liệu kho từ AI.
- Gemini chỉ hỗ trợ tư vấn; **không tự nhập kho, trừ tồn, ghi nhận Đã xuất hoặc lưu phiếu bàn giao**. Anh duyệt mọi bước trong các màn hình chức năng hiện hữu.
- Muốn dùng chat cần Internet và Gemini API Key còn hiệu lực/hạn mức.

## Bản vá v1.6.1: tách kiện theo ngày cont đi

- Hưng Yên nhận hàng đi hai ngày **26.10** và **27.10** thì quản lý thành hai kiện riêng ngay cả khi trùng số HĐ, công trình và số lượng.
- Trong `Tổng hợp.xlsx` → `chi tiết`: tên công trình sẽ có hậu tố ngày cont đi: `Công trình A T1 (26.10)`, `Công trình A T1 (27.10)`.
- Ngày cont đi đầy đủ được giữ ở cột ẩn AC; số container và mã kiện nguồn lần lượt ở AA và AB. Vẫn có thể tra cứu theo tên công trình gốc mà không bị nhầm do hậu tố ngày.
- Cả nhập theo ảnh Gemini đã duyệt và **nhập cont thủ công** đều không thay thế kiện khác ngày; không nhập kho tự động từ lịch dự kiến.
- Gặp nguồn cũ có khả năng trùng kiện nhưng chưa có mã cont hoặc ID nguồn, phần mềm yêu cầu kiểm tra trước khi ghi.

## HISA v1.6.0 – Giao diện điều hành và nhập cont thực tế

- Mở ứng dụng: trang **Tổng quan kho** hiển thị lịch xuất theo ngày, số hợp đồng sẵn sàng/thiếu vật tư/cần kiểm tra.
- **Hàng dự kiến xuất kho từ Gemini**: kiểm tra và lưu lịch, tích ô **Làm phiếu** ở từng hợp đồng cần xuất, bấm **Lập phiếu từ hợp đồng đã tích**. HISA đối chiếu đúng số HĐ + công trình + công ty, sau đó mở phiếu chờ cho anh kiểm tra và lưu. Không tự ghi đã xuất.
- **Hàng dự kiến về kho**: sau khi cont về thực tế, vào **Cont đã về kho (AI)**, chọn ngày thực nhận, tích các container, xem kiện nguồn từ trang Cont đi Hưng Yên, xác nhận ghi trang chi tiết của **Tổng hợp.xlsx**. Không tự ghi nhập khi chỉ mới có lịch.
- Giao diện WPF mới gồm thanh điều hướng chuyên nghiệp, bảng dữ liệu dễ đọc, các thẻ tình trạng, tìm kiếm và thao tác nhanh; không cần cài thêm Python/Node.
- Gemini API Key vẫn lưu an toàn trong Windows Credential Manager ở mục **Cấu hình & sao lưu**, có thể lưu/thay/xóa.

## Gemini AI – nhập khóa một lần, lưu sau cập nhật (v1.5.2)

- HISA **chỉ sử dụng Gemini**, bỏ GPT/OpenAI khỏi màn hình phân tích ảnh Zalo.
- Vào **Cấu hình & sao lưu → Gemini AI**, dán API Key, bấm **Lưu / thay Gemini API Key**. Khóa lưu trong Windows Credential Manager của tài khoản Windows hiện tại, không nằm trong thư mục cài đặt hoặc tệp Excel/JSON/backup và không bị xóa khi nâng cấp cùng tài khoản.
- Màn hình AI chỉ cần chọn ảnh Zalo rồi bấm **Phân tích ảnh với Gemini**. Đọc hai phần hàng dự kiến về Hưng Yên và lịch dự kiến xuất kho, tự cảnh báo dòng cần xác minh. Duyệt trước khi lưu, không tự ghi xuất/nhập kho.
- Để thu hồi khóa: vào **Cấu hình & sao lưu → Xóa Gemini API Key**, xác nhận. Chỉ khóa bị xóa, không xóa dữ liệu kho.
- Giao diện mới bổ sung quy trình bốn bước, thẻ số liệu tổng quan, màn hình cấu hình rõ ràng và thông báo lỗi dễ hiểu.
- **Lưu ý:** Khóa tồn tại qua cập nhật HISA nhưng không chuyển tự động sang tài khoản Windows hoặc máy tính khác. Nếu xóa tài khoản Windows thì phải nhập lại.
- Kết nối Gemini yêu cầu Internet và hạn mức API hợp lệ. Khóa chỉ được gửi đến Google qua HTTPS khi anh chủ động phân tích ảnh.

## Sẵn sàng xuất hàng theo ngày (v1.5.0 thử nghiệm)

- Mở HISA: Tổng quan hiện kế hoạch hôm nay nếu có, nếu không thì kế hoạch ngày mai. Khi chưa có lịch, giao diện hỏi nhập ảnh Zalo hoặc thêm hợp đồng phát sinh.
- Vào **AI lịch nhập – xuất kho**, đọc ảnh và duyệt. Vào **Sẵn sàng xuất hàng**, chọn ngày, bấm **Nạp lịch AI đã duyệt**. Chỉ lấy phần lịch xuất; không coi container dự kiến về là hàng đã nhập.
- Bổ sung hợp đồng phát sinh bằng tay, lý do **tùy chọn**, chọn **Kho thành phẩm** hoặc **Xuất trực tiếp khách**.
- Kiểm tra từng kiện Hưng Yên theo số hợp đồng, công trình, tên vật tư đầy đủ có quy cách (700/750 là hai dòng khác nhau), số lượng và đơn vị. Nếu dữ liệu chưa cập nhật, không suy đoán đủ hoặc thiếu.
- Nhập số lượng thực tế đã chuẩn bị, máy kéo phải có serial tương ứng mới báo sẵn sàng. Vật tư đã chuẩn bị vẫn giữ ở kho Hưng Yên **chờ đến lịch** và cần phiếu bàn giao được xác nhận riêng.
- Ghi nhận tại APP KHO/data/dispatch_readiness.json, không tự ghi Excel gốc, lịch sử xuất hoặc trừ tồn kho.
- Giới hạn thử nghiệm: chưa tự động đồng bộ file máy kéo/đầu cửa cabin độc lập do chưa có mẫu. Cần rà soát số lượng đã chuẩn bị khi có nhiều lần xuất cùng hợp đồng.

## AI đọc ảnh bảng lịch kho (v1.4.0 – giao diện cũ, đã thay bằng Gemini v1.5.2)

1. Mở **AI lịch nhập – xuất kho** trên thanh điều hướng; tải ảnh chụp bảng Excel từ Zalo về máy rồi chọn JPG/PNG/WEBP.
2. Nhập API Key OpenAI và Gemini (cung cấp bởi tài khoản dịch vụ API, không phải mật khẩu ChatGPT). Nhập cả hai để AI kiểm tra chéo; nếu một API lỗi, phần mềm có cảnh báo.
3. Bấm **GPT + Gemini · Phân tích ảnh**; xem **PHẦN 1 · NHẬP KHO** theo từng container và **PHẦN 2 · XUẤT KHO** riêng. Kiểm tra ngày nhập kho, ngày xuất container, mã hợp đồng, công trình, ghi chú và đơn vị.
4. Sửa bất kỳ hàng nào cần thiết, kiểm tra cảnh báo, chọn **Lưu kế hoạch đã duyệt**. Tệp JSON được lưu riêng tại Documents/HISA Kho Vat Tu/APP KHO/data/ai_schedules. Việc duyệt này **không cập nhật sổ xuất hoặc tồn kho**.
5. Bấm **Nạp các hợp đồng xuất vào kế hoạch** để chuyển mã sang màn hình lập phiếu chờ cũ, đối chiếu vật tư và xác nhận riêng.

Quy tắc nghiệp vụ: **Đã có LX = đã có lịch xuất**, không phải tên lái xe, không phải hàng đã xuất. Ô ghi chú trống = chưa xác nhận lịch xuất. Hợp đồng trùng nhưng khác công trình không được tự động gộp; khác nhau giữa hai AI được đưa vào danh sách kiểm tra. Không tự động ghi ngày về kho dựa trên lịch dự kiến. Ghi chú “Khách cần gấp” được giữ lại.

Chế độ AI yêu cầu Internet, API Key riêng và có thể tính phí theo nhà cung cấp. API Key chỉ được giữ trong bộ nhớ trong phiên chạy; không lưu file, không đưa vào bản sao lưu và không được nhúng trong bản phát hành công khai. Ảnh chỉ được gửi khi người dùng chủ động bấm phân tích. HISA không tự đọc tin nhắn Zalo hoặc yêu cầu đăng nhập Zalo.

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
