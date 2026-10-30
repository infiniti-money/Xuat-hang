# HISA Quản lý kho — v1.6.1 (Windows 64-bit)

## Kiện Hưng Yên theo ngày cont đi
- **Cùng số hợp đồng nhưng ngày cont đi khác nhau = hai kiện khác nhau**, kể cả khi tên công trình, loại vật tư và số lượng giống nhau.
- Khi xác nhận cont đã về hoặc nhập cont thủ công, thêm hậu tố ngày ngắn **(dd.MM)** sau tên công trình vào trang `chi tiết` của `Tổng hợp.xlsx`. Ví dụ `Công trình A T1 (26.10)` và `Công trình A T1 (27.10)`. Không sửa tên công trình gốc ở nguồn.
- Giữ nguyên T1/T2, Thang 1/2, (1)/(2) trong tên, không lấy ngày gắn thêm làm mã công trình khi đối chiếu. Không thêm hậu tố ngày hai lần.
- Giữ số cont (AA), ID kiện gốc (AB), **ngày cont đi đầy đủ** (AC) dưới dạng cột ẩn trong trang chi tiết, không ghi đè cột đã có dữ liệu khác.
- Nhập kho AI theo từng kiện cần xác nhận cont thực tế đã về; dữ liệu thiếu ngày cont đi được cảnh báo và không được tự nhập. Nhập kho thủ công giữ nguyên kiện cùng HĐ thuộc các ngày cont đi khác nhau, không xóa / thay thế kiện trước đó.
- Kiện đã nhập theo ID nguồn được bỏ qua khi nhập thủ công lại, hoặc từ chối khi thao tác nhập AI; tránh nhập trùng. Dữ liệu cũ chưa có ID/cont nguồn được cảnh báo để đối chiếu trước khi ghi.
- Bộ kiểm thử Excel cô lập xác minh hai ngày 26.10, 27.10, cùng HĐ/cùng số lượng, T1/T2, nhập bằng AI và nhập thủ công, giữ toàn bộ kiện và dữ liệu gốc.

---

# HISA Quản lý kho — v1.6.0 · Giao diện chuyên nghiệp & AI nhập/xuất

## Giao diện Windows mới
- Trang chủ dạng trung tâm điều hành: tiêu đề rõ, thanh điều hướng tối màu, phím tắt, tác vụ Gemini và kế hoạch xuất luôn nằm đầu màn hình.
- Bảng kế hoạch xuất theo ngày ưu tiên trạng thái **Sẵn sàng / Thiếu vật tư / Cần kiểm tra**, không tự xuất kho.
- Bảng dữ liệu thoáng, có sọc xen kẽ, chọn dòng rõ ràng, cuộn ảo cho danh sách lớn, giao diện nhập liệu và màu trạng thái thống nhất.
- Giữ nguyên mô hình Windows .NET 10 WPF, không cần cài thêm ngôn ngữ hay môi trường trên máy người dùng.

## AI hàng dự kiến xuất → tích chọn làm phiếu bàn giao
- Gemini đọc bảng lịch xuất từ ảnh Zalo; mỗi dòng có ô **Làm phiếu**. Tích một hoặc nhiều hợp đồng và bấm **Lập phiếu từ hợp đồng đã tích**.
- HISA đối chiếu số hợp đồng + công trình + công ty trong Tổng hợp.xlsx, chặn nhầm công trình trùng số hợp đồng, lấy đúng kiện và vật tư.
- Mở màn hình kế hoạch phiếu với các dòng được chọn, cho kiểm tra/chỉnh sửa rồi lưu **phiếu chờ xuất**. Không tự đánh dấu Đã xuất.
- Lịch phải được người dùng kiểm tra và duyệt trước khi sinh phiếu.

## AI hàng dự kiến về → cont thực tế đã về → Tổng hợp.xlsx
- Màn **Cont đã về kho (AI)** chọn ngày thực tế và tích các container đã nhận.
- Trước khi ghi, kiểm tra kiện Hưng Yên từ dữ liệu nguồn, hợp đồng/công trình/công ty, tình trạng đã nhập, mã container thiếu và nguồn trùng nhiều cont.
- Sau khi xác nhận, chỉ ghi **các kiện đã chọn** vào trang chi tiết của Tổng hợp.xlsx bằng luồng sao lưu/ghi an toàn sẵn có; không ghi nhầm các công trình cùng mã hoặc suy luận cont dự kiến đã về.
- Mỗi lần ghi đối chiếu lại dữ liệu hiện tại; không ghi nếu kiện đã nhập trong cùng ngày. Các container không chọn vẫn ở trạng thái dự kiến.

## Quy tắc bổ sung: nhiều kiện cùng số hợp đồng
- **Cùng số HĐ nhưng ngày cont đi khác nhau = hai kiện Hưng Yên riêng**, bất kể vật tư/số lượng có giống nhau hay không.
- Khi xác nhận cont đã về, HISA ghi tên công trình trong `Tổng hợp.xlsx` như `Công trình A (26.10)` hoặc `Công trình A (27.10)`, giữ T1/T2, Thang 1/2 và các phần trong ngoặc trước đó.
- Ngày cont đi ISO được lưu ở cột ẩn AC, bên cạnh container (AA) và ID kiện nguồn (AB). Đọc lại Excel không biến hậu tố ngày thành công trình khác; ngăn nhập trùng ID kiện.

- Hai kiện cùng HĐ nhưng vật tư hoặc số lượng khác nhau phải ghi thành **hai kiện riêng**.
- Các tên công trình có T1/T2, Thang 1/Thang 2, (1)/(2) không được gộp kể cả số lượng giống nhau.
- Một HĐ đi hai container được quản lý riêng theo **cont đi + ID kiện nguồn**, không cộng dồn hoặc thay thế kiện còn lại.
- Cont đã về (AI): màn hình chọn theo từng kiện và hiển thị ngày cont đi, vật tư, số lượng; chỉ ghi sau xác nhận thực nhận. Ghi dấu cont và ID kiện vào các cột ẩn AA/AB của trang chi tiết, giữ nguyên nội dung hiển thị A:J.
- Nếu Excel cũ thiếu dấu nhận dạng mà trùng vật tư/số lượng hoặc lịch cont không chứng minh được nguồn, HISA dừng để yêu cầu đối chiếu; không xóa hoặc tự gộp kiện.
- Kiểm thử Excel cô lập gồm hai cont cùng HĐ/số lượng, số lượng khác nhau, T1/T2 và ngăn nhập trùng.

## Bảo mật và tương thích
- Gemini API Key lưu trong Windows Credential Manager, giữ khi nâng cấp cùng tài khoản Windows. Có nút lưu/thay/xóa trong Cấu hình.
- Giữ cấu trúc dữ liệu, phiếu, file Excel, quy tắc serial máy kéo và thư mục ngày như 26.10 / 27.10.
- Regression kiểm tra chọn đúng hợp đồng xuất, chặn ngày không đúng, chặn nguồn container thiếu/trùng và duyệt trước khi ghi.

---

# HISA Quản lý kho v1.5.2 – Gemini AI

- Bỏ kết nối GPT. Chỉ dùng Gemini để phân tích ảnh Zalo và lập lịch dự kiến nhập/xuất kho.
- Chuyển quản lý Gemini API Key vào Cấu hình & sao lưu: Lưu/thay khóa và Xóa khóa (cần xác nhận).
- Lưu khóa bằng Windows Credential Manager, tách khỏi thư mục ứng dụng, giữ khi nâng cấp trên cùng tài khoản Windows.
- Không lưu API Key trong Excel, JSON, bản sao lưu hoặc bộ cài; không tự ghi hàng xuất hay nhập kho từ AI.
- Nâng giao diện màn AI, màn Cấu hình, bổ sung trạng thái kết nối, hướng dẫn theo bước và thẻ số liệu.
- Kiểm thử độc lập lưu/đọc/thay/xóa khóa trên Windows và nghiệp vụ Gemini.

---

# HISA Quản lý kho — v1.5.1 (Windows x64 · sửa kết nối AI)

## Khắc phục lỗi GPT / Gemini
- Phần mềm hiển thị **riêng mã HTTP và hướng dẫn xử lý** khi OpenAI hoặc Gemini không kết nối được (401 khóa API, 403 quyền truy cập, 404 mô hình, 429 hạn mức, 503 máy chủ). Không hiển thị nội dung lỗi thô có thể chứa API Key.
- Thay mô hình mặc định Gemini 2.5 Flash bị giới hạn quyền cho nhiều dự án API mới bằng **gemini-3.5-flash-lite** (hỗ trợ ảnh, JSON và trích xuất bảng).
- Xử lý thông báo khi mô hình trả dữ liệu không đúng định dạng, bị chặn hoặc hết thời gian chờ; giữ lỗi dưới màn AI sau khi đóng hộp thoại.
- Có thể chạy chỉ GPT hoặc chỉ Gemini nếu chỉ nhập API Key của một nhà cung cấp. Phần mềm ghi rõ một AI chưa được kiểm chứng chéo.
- Thêm kiểm thử lỗi HTTP, phân biệt tín dụng và bảo vệ không làm lộ API Key.

## Sử dụng
1. Mở **AI lịch nhập – xuất kho** và chọn ảnh Zalo.
2. Điền API Key OpenAI và/hoặc Google AI Studio. Tài khoản ChatGPT Plus không phải API Key, hạn mức tính riêng.
3. Nếu có lỗi, đọc dòng thông báo riêng GPT/Gemini để kiểm tra quyền API, billing, mô hình hoặc mạng. **Không gửi API Key hay ảnh chứa thông tin nhạy cảm lên nơi công khai.**

Không có API Key nào được lưu trong cấu hình hoặc đóng gói EXE. Bản vá không chỉnh sửa workbook, phiếu bàn giao hay dữ liệu kho thực tế.

---

# HISA Quản lý kho — v1.5.0 (Windows x64 · bản thử nghiệm)

## Kiểm tra sẵn sàng xuất hàng theo lịch

- Tổng quan ưu tiên lịch hôm nay nếu đã có, hoặc ngày mai. Không có lịch sẽ hỏi nhập ảnh Zalo hay hợp đồng phát sinh.
- Màn hình **Sẵn sàng xuất hàng** nạp phần xuất của lịch AI đã duyệt và nhập tay hợp đồng phát sinh, lý do tùy chọn, phân biệt kho thành phẩm/xuất trực tiếp.
- Đối chiếu kiện Hưng Yên theo hợp đồng, công trình, đơn vị, tên vật tư đầy đủ và số lượng; phân biệt 700/750. Chưa cập nhật không bị xem là thiếu.
- Lưu số lượng thực tế đã chuẩn bị; máy kéo kiểm tra serial theo số lượng trước khi đánh dấu sẵn sàng.
- Không tự bàn giao khi chuẩn bị xong, không tự ghi nhận Đã xuất hay chỉnh Excel gốc.
- File trạng thái riêng: APP KHO/data/dispatch_readiness.json. Chưa tự kết nối file theo dõi máy kéo/đầu cửa cabin vì chưa có mẫu thực tế.

## Kiểm thử

Có thêm kiểm thử độc lập: phân biệt quy cách, thiếu dữ liệu, serial máy kéo, duyệt AI đúng phần xuất và lưu/đọc trạng thái. CI Windows chạy biên dịch .NET 10, tự kiểm tra và đóng gói. Trên nhánh thử nghiệm chỉ tạo build artifact; GitHub Release chính thức chỉ có sau khi hợp nhất main và CI thành công.

---

# HISA Quản lý kho — v1.4.0 AI (Windows 64-bit)

## AI đọc lịch nhập – xuất kho từ ảnh Zalo

- Màn hình **AI lịch nhập – xuất kho**: chọn một ảnh JPG/PNG/WEBP đã tải từ Zalo, nhập API Key OpenAI và Gemini; hai dịch vụ phân tích qua Internet và đối chiếu kết quả.
- Nhận dạng **hai bảng tách biệt**: hàng dự kiến nhập kho Hưng Yên theo container, ngày xuất container, đơn vị, số hợp đồng, công trình, ghi chú và hàng dự kiến xuất kho theo ngày.
- **"Đã có LX" = "Đã có lịch xuất"**, không phải đã có tài xế, cũng không phải đã xuất hàng. Ghi chú trống = **chưa xác nhận lịch xuất**. "Khách cần gấp" được đánh dấu để ưu tiên.
- Đối chiếu mã hợp đồng với nguồn HISA và giữa hai bảng; nếu GPT/Gemini có kết quả khác nhau, hàng được gắn **Cần kiểm tra**, không tự đoán dữ liệu còn thiếu.
- Người dùng **sửa và duyệt từng hàng**, lưu kế hoạch riêng dưới dạng JSON vào thư mục dữ liệu tại máy. Có thể nạp mã hợp đồng xuất vào màn hình kế hoạch hiện hữu và chỉ tạo phiếu chờ sau khi thao tác xác nhận riêng.
- **Không tự ghi sổ Excel, không ghi nhận đã xuất, không điều xe**, không lấy dữ liệu từ Zalo trực tiếp; người dùng tải ảnh và chọn thủ công.
- API Key chỉ giữ trong phiên làm việc, không lưu vào JSON, workbook hoặc cài sẵn trong EXE. Ảnh được gửi tới nhà cung cấp AI khi bấm phân tích; cần có Internet và API Key riêng (có thể phát sinh phí API).

## Cài đặt

Nếu CI Windows đạt, tệp phát hành sẽ là HISA-Kho-Vat-Tu-1.4.0-Setup.exe, bản ZIP Windows x64 và SHA-256. Các phiên bản 1.3.x được nâng cấp tại chỗ; dữ liệu Excel, cấu hình và lịch sử phiếu được giữ nguyên.

## Kiểm thử

Bộ tự kiểm tra có thêm ca phân tích bảng kép, so sánh hai AI, trạng thái LX, cảnh báo hợp đồng thiếu và round-trip JSON. Kết nối API thực tế cần người dùng nhập khóa riêng và kiểm tra với ảnh mẫu của đơn vị.

---

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
