# HISA v1.9.15 — Phục hồi ZIP an toàn và cập nhật có thể thử lại

- **Cấu hình & sao lưu → Phục hồi từ ZIP:** chọn ZIP, xem trước ngày và số file, chọn riêng **Dữ liệu ứng dụng (JSON, lịch AI)** hoặc **Excel**. Mặc định KHÔNG ghi đè Excel. Cần xác nhận rõ ràng.
- ZIP v2: kiểm tra SHA-256 manifest, tên file, đường dẫn và cấu trúc JSON/Excel trước khi ghi. ZIP v1 cũ: cho phép phục hồi có cảnh báo **không có checksum và không chứa lịch AI**.
- Ghi các tệp bằng file tạm tại thư mục đích, lưu bản gốc vào thư mục **Restore-Safety-*** trong Backups và tự rollback các tệp đã thay đổi nếu phát sinh lỗi. Chặn ZIP lạ, zip slip, workbook khóa, thiếu tệp chính và dung lượng giải nén bất thường. Phục hồi xong ứng dụng tự đóng để nạp lại dữ liệu.
- **Cập nhật trực tuyến:** tệp .partial được giữ lại khi mất mạng để thử tải tiếp bằng HTTP Range; nếu máy chủ không hỗ trợ Range thì tải lại từ đầu, không nối nhầm dữ liệu. Hiển thị tiến độ và trạng thái xác minh SHA-256. Bộ cài ghi log trong thư mục tạm HISA-Verified-Updates.
- **Bảo vệ dữ liệu:** không ghi API Key Gemini vào ZIP/log; không tự đổi dữ liệu kho khi chỉ kiểm tra cập nhật. Các thử nghiệm phục hồi sử dụng file giả lập cô lập trong Windows CI.
- **Giới hạn:** ZIP v1 không có chứng cứ SHA-256; phục hồi cấu hình từ máy khác có thể dẫn tới đường dẫn Excel cũ; phải kiểm tra đường dẫn nguồn sau khi mở lại. Dù CI cài mới/cài đè thành công, việc cài trên máy người dùng vẫn phụ thuộc UAC, antivirus và trạng thái Excel. Nếu cập nhật lỗi, dùng bộ cài chính thức và log để chẩn đoán.

---

# HISA v1.9.14 — Trạng thái máy kéo và ngày bàn giao thống nhất tại cột N

- Trong **Theo dõi máy kéo**, chọn đúng HĐ, công trình, đơn vị, seri rồi chọn một tình trạng **Chưa làm / Đã làm** trong danh sách xổ xuống; nhấn **Lưu trạng thái vào Excel cột N** để ghi đúng chữ, không điền ngày khi chỉ mới làm xong.
- Cột **N** của **Danh sách sản xuất/MÁY KÉO.xlsx** là vị trí cố định: cột N trống được đặt tiêu đề **Ngày bàn giao**, các cột khác (kể cả cột ngày v1.9.13 ở vị trí cũ) không tự xóa hoặc di chuyển. Nếu cột N đã có tiêu đề khác hay dữ liệu không rõ nguồn, HISA chặn ghi để người dùng kiểm tra; không chèn cột ở nơi khác.
- Khi phiếu xuất được xác nhận **Đã xuất**, chỉ những máy kéo có **HĐ + công trình + đơn vị + seri duy nhất** mới đổi chữ tại N thành **ngày thực tế trên phiếu**. Trạng thái **Đã bàn giao** ở màn hình máy kéo được hiển thị theo phiếu xuất đã chốt, không còn nút tự xác nhận bàn giao thủ công.
- Khi hủy phiếu, HISA phục hồi tình trạng đã làm trước đó (nếu chính HISA đã ghi) hoặc ô gốc; ngày Excel sửa tay không được tùy tiện ghi đè. Từng lần sửa workbook đều tạo bản sao trước khi thay thế, có file receipt và khả năng thử lại khi file khóa.
- Bổ sung regression trên workbook mô phỏng cho chữ **Đã làm**, đổi ngày thực xuất, hủy phiếu/khôi phục chữ, quay về **Chưa làm**, xung đột ô nhập tay, cột N bị chiếm dụng và giữ nguyên cột khác.
- **Lưu ý:** Chưa có công cụ tự chuyển các cột ngày bàn giao cũ sang N. Đối chiếu một lần với file MÁY KÉO.xlsx thực tế trước khi vận hành trên dữ liệu chính.

---

# HISA v1.9.13 — Cột Ngày bàn giao tự động trong danh sách Máy kéo

- Khi xác nhận **Đã xuất** trong Phiếu bàn giao, ghi **ngày bàn giao** (ngày trên phiếu) vào dòng **từng seri máy kéo** trong file `Danh sách sản xuất/MÁY KÉO.xlsx`.
- Cột Excel được dùng lại nếu đã có tiêu đề `Ngày bàn giao`, hoặc tự tạo một cột mới ở sau cột cuối đã sử dụng, giữ nguyên các cột và dữ liệu hiện hữu.
- Chỉ cập nhật nếu khớp duy nhất số HĐ, công trình, đơn vị, seri; không tự ghi cho nhiều máy cùng một HĐ, không ghi từ phiếu Chờ xuất hoặc Hồng Yên gia công chưa xuất kho. Seri thiếu, sai công trình hoặc ngày khác được nhập thủ công đều cảnh báo/không ghi đè.
- Lưu bản sao nguyên gốc trước khi viết, Excel atomic save; nếu file khóa, phiếu đã xuất vẫn được giữ nguyên và HISA sẽ thử lại qua đồng bộ. Ngày đã ghi bởi HISA tự đổi nếu ngày phiếu được sửa, hoặc được hoàn tác khi phiếu bị hủy (nếu chưa chỉnh tay).
- Bổ sung file receipt `motor_handover_excel_sync.json` cho theo dõi/hoàn tác, tự đưa vào ZIP sao lưu cùng trạng thái phiếu.
- Bộ regression tạo Excel tạm và kiểm tra ghi ngày, phiếu chưa xuất, nhiều seri, trùng/mất seri, Excel đang khóa, dữ liệu thủ công và chuyển trạng thái hủy. Không có dữ liệu khách hàng nào được sửa trong GitHub Actions.

---

# HISA v1.9.12 — Cấu hình bằng một thư mục duy nhất

- Chỉ cần chọn **thư mục gốc Kho HISA** một lần; HISA tự đọc 3 Excel trong `Kho chính`, chỉ lấy cont trong `CONT ĐI`, đọc 3 Excel thiết bị trong `Danh sách sản xuất`. Không còn nút hoặc ô chọn riêng từng Excel máy kéo/tủ điện/đầu cửa car.
- Hiển thị bảng kiểm tra sáu file vừa nhận diện trước khi lưu. Chặn lưu nếu chọn nhầm thư mục con hoặc thiếu một trong sáu file, thay vì lưu cấu hình lẫn nguồn cũ; không thay đổi Excel.
- Đổi kho bằng cách chọn thư mục gốc khác tự ánh xạ tất cả các đường dẫn trong cùng kho; không giữ nhầm danh sách sản xuất từ kho trước.
- Giữ nguyên các trạng thái công việc máy kéo Hồng Yên, hợp đồng đã gửi làm, ngày hoàn thành/bàn giao, phiếu kho và lịch AI.
- Hồi quy kiểm tra chỉ một nút chọn nguồn, nhận diện sáu file, sai thư mục con, file thiếu, đổi sang kho khác, tránh ghép nhầm dữ liệu cũ, Windows .NET compile và smoke cài mới/cài đè.

---

# HISA v1.9.11 — Danh sách hợp đồng máy kéo gia công Hồng Yên

- Bổ sung bảng **Hợp đồng máy kéo gửi Hồng Yên làm** trực tiếp trong **Theo dõi máy kéo**, hiển thị số HĐ, công trình, đơn vị, tổng số máy và lượng máy ở từng giai đoạn: đang làm, đã làm xong, đã bàn giao, chưa gửi.
- Mặc định lọc **chỉ hợp đồng đã được xác nhận gửi gia công**. Có thể bỏ lọc để tra cứu tất cả hợp đồng trong `MÁY KÉO.xlsx`; không tự đổi trạng thái chỉ từ sự hiện diện trong file hoặc ghi chú.
- Chọn hợp đồng sẽ lọc bảng chi tiết máy kéo theo đúng HĐ + công trình + đơn vị. Từng máy/seri vẫn được xác nhận ngày gửi, ngày hoàn thành, ngày bàn giao và lịch sử riêng. Cùng HĐ khác công trình T1/T2 được giữ tách biệt.
- Chuẩn hóa nhãn **Hồng Yên** cho đơn vị gia công trong khi phần cont vẫn là **Hưng Yên**; không đổi tên file lưu trạng thái nội bộ để đảm bảo bản nâng cấp bảo toàn dữ liệu.
- Kiểm thử bảng tổng hợp hợp đồng, ngày và nhóm trạng thái, bộ lọc mặc định và tính không nhập nhầm kho; đóng gói Windows x64 và cài đè nếu CI đạt.

---

# HISA v1.9.10 — Rà soát hồi quy, sao lưu lịch AI và an toàn theo dõi máy kéo

- Đã kiểm tra lại luồng đọc nguồn `Kho chính` (Tổng hợp, KT, Thành phẩm), quét `CONT ĐI`, danh sách sản xuất, nhận kiện thực tế, Gemini/Zalo và lập phiếu chờ: không chủ động thay đổi quy trình ghi nhận hàng.
- Khắc phục lỗi **backup ZIP thiếu lịch AI đã duyệt**: bao gồm `ai_schedules/lich-kho-*.json` và tất cả JSON trạng thái gốc, gồm lịch sử máy kéo Hưng Yên, cùng ba workbook Excel. Thêm manifest v2 và SHA-256 kiểm tra tất cả các mục trước khi xuất bản ZIP; không ghi đè bản sao cũ nếu lỗi.
- Sửa ánh xạ trạng thái máy kéo bị đứt khi Excel chèn/dịch chuyển dòng: tự khớp chỉ khi có **seri và định danh vật lý duy nhất**, tuyệt đối không gán lịch sử cho máy trùng seri. Giữ nguyên khóa và JSON trạng thái v1.9.7–v1.9.9.
- Sửa mở lại trang cấu hình nguồn khi file `Tổng hợp.xlsx` tạm thời không tồn tại; vẫn hiện đường dẫn gốc để chỉnh sửa thay vì văng lỗi.
- Tăng kiểm thử ZIP lịch AI và checksum hỏng, trường hợp Excel sắp lại dòng máy kéo, seri trùng và file Excel gốc tạm mất.
- **Chưa giải quyết:** khôi phục ZIP có rollback (issue #11) và thử lại tải/cài đặt thất bại trên máy thực tế (issue #13). Không tự động phục hồi ZIP hay tự biến lịch dự kiến thành hàng đã nhập.

---

# HISA v1.9.9 — Chuẩn hóa Kho chính, CONT ĐI, Danh sách sản xuất

- Chỉ cần chọn **Kho HISA**, HISA nhận diện ba file quan trọng trong `Kho chính`: `Tổng hợp.xlsx`, `KT tổng hợp.xlsx`, `Thành phẩm xuất kho.xlsx`.
- Tự chọn duy nhất `CONT ĐI` cho nguồn quét container, **không** dùng toàn thư mục `Kho HISA` làm nguồn vật tư.
- Tự nhận diện `TỦ ĐIỆN.xlsx`, `MÁY KÉO.xlsx`, `Đầu cửa car T10.2026.xlsx` trong thư mục `Danh sách sản xuất` cùng cấp; cho phép chọn thủ công nếu nhiều file đầu cửa theo tháng.
- Cài đặt nguồn có thể chọn trực tiếp thư mục gốc hoặc ba thư mục con, rồi thấy 6 đường dẫn và các cảnh báo file quan trọng bị thiếu trước khi lưu.
- Phân biệt đúng đọc file sản xuất với dữ liệu cont đã nhập; giữ lịch sử máy kéo gia công Hưng Yên và các đường dẫn sẵn có, không tự di chuyển file.
- Bổ sung kiểm thử fixture 6 workbook trong 3 thư mục, quét cont duy nhất, không suy diễn đầu cửa sai tháng; build, kiểm thử riêng trên Windows và cài đè trước khi phát hành.

---

# HISA Quản lý kho v1.9.8 – Ba danh sách sản xuất phải nằm ngoài thư mục Tổng hợp.xlsx

- Nhấn mạnh trong giao diện rằng file **Tủ điện**, **Máy kéo**, **Đầu cửa car** dùng ba đường dẫn **độc lập**, không cần và không được để chung với thư mục chứa `Tổng hợp.xlsx` hoặc thư mục nguồn `CONT ĐI`.
- HISA **không tự di chuyển, sao chép, chỉnh sửa** file Excel của người dùng. Cảnh báo khi cấu hình file bị trùng hoặc nằm sai thư mục; thay đổi nguồn kho không làm mất đường dẫn thiết bị đã lưu.
- Tránh quét nhầm các danh sách sản xuất thành nguồn cont ở các cài đặt cũ đã đặt file thiết bị dưới thư mục `CONT ĐI`.
- Kiểm thử nguồn thiết bị nằm ngoài kho, thay đổi master path vẫn giữ ba đường dẫn, trường hợp đặt chung thư mục hoặc đặt trong `CONT ĐI`, file trùng tên và không tác động vào dữ liệu Excel.
- Giữ nguyên danh sách máy kéo đã gửi Hưng Yên làm / đã làm xong / đã bàn giao từ v1.9.7, cùng lịch sử theo dõi riêng.

---

# HISA Quản lý kho v1.9.7 — Danh sách máy kéo Hưng Yên gửi làm / đã làm / đã bàn giao

- Thêm màn hình trực tiếp **Máy kéo · gửi / làm / bàn giao** và lối mở từ Quản lý & lịch sử.
- Dữ liệu máy từ MÁY KÉO.xlsx (số HĐ, công trình, đơn vị, loại máy, seri, số lô, số lượng); có bảng lọc theo hợp đồng/seri và bốn nhóm trạng thái.
- Đặt lịch sử riêng cho từng máy kéo: (1) **Đã gửi Hưng Yên làm** → (2) **Đã làm xong** → (3) **Đã bàn giao**, nhập ngày thực tế và ghi chú/chứng từ, yêu cầu xác nhận trước khi lưu. Không cho nhảy bước, đổi ngày ngược hoặc tự suy đoán từ cột DXH.
- JSON trạng thái lưu riêng trong kho tài liệu của HISA với ghi file tạm, tạo .bak trước thay đổi; vẫn hiển thị lịch sử cũ nếu dòng bị xóa khỏi Excel.
- Không thay đổi tệp Excel nguồn, dữ liệu Tổng hợp.xlsx, nhập cont hay phiếu giao hàng; bảo toàn dữ liệu khi cập nhật. CI có regression tests cho tính độc lập của từng seri/công trình, thứ tự các bước, lưu sau khởi động lại, lịch sử và tệp lỗi.
- Bản Windows x64 sử dụng trình cài Inno Setup tiếng Việt với smoke-test cài mới/cài đè.

---

# HISA v1.9.6 — Thêm ba danh sách thiết bị Tủ điện, Máy kéo và Đầu cửa car

- Cấu hình riêng ba đường dẫn Excel bằng nút **Chọn Excel** trong trang Cấu hình nguồn dữ liệu. Giữ cài đặt khi đổi thư mục nguồn hoặc nâng cấp; không trộn với thư mục `CONT ĐI` và file tổng hợp kho.
- Sau khi phân tích Zalo bằng Gemini, chọn HĐ để xem cả **vật tư trong Tổng hợp.xlsx** và **dữ liệu thiết bị từ các danh sách sản xuất**; hiển thị loại thiết bị, công trình/đơn vị, số lượng, mã vật tư, số lô/seri, ngày xuất xưởng, lịch và nguồn Excel. Có nút xem cả ba danh sách tương ứng các HĐ trong ảnh.
- Chỉ đọc các trang danh sách sản xuất được nhận diện đúng mẫu; bỏ qua in tem, danh mục mẫu, hàng mượn và dòng tổng cộng. Dữ liệu sắp xếp theo từng nguồn, không tự gán số lượng hoặc trạng thái đã về.
- Nguồn T1/T2 hoặc công ty không giống ảnh: cảnh báo **chỉ khớp số HĐ**, không tự coi đã cùng công trình và không dùng trực tiếp để lập phiếu.
- Bổ sung regression tests bằng Excel giả cho ba cấu trúc, không lẫn template, mã, seri, ngày, lượng và khả năng mở khi chưa cấu hình. Giữ nguyên an toàn phiên bản trước.

---

# HISA Quản lý kho v1.9.5 — Sửa không tích được ô Zalo, xem hàng hóa hợp đồng

- Sửa checkbox **Cont đã về?**, **Làm phiếu** và màn **Đã về**: có thể tích/bỏ tích ngay lần nhấp chuột đầu tiên, kích thước ô chọn dễ thao tác hơn. Cùng kiểu dùng cho Khách gấp/Cần kiểm tra.
- Sau khi Gemini đọc ảnh, click vào mỗi dòng hợp đồng để mở bảng vật tư **tên hàng, số lượng, ĐVT, ngày cont đi, ngày cont về và trạng thái** bên dưới. Có nút xem **toàn bộ vật tư của các hợp đồng** trong ảnh Zalo.
- Vật tư và số lượng lấy từ **Tổng hợp.xlsx**, không suy đoán từ AI. Đối chiếu chính xác hợp đồng + công trình + đơn vị; số hợp đồng trùng T1/T2 hoặc không khớp dữ liệu sẽ hiển thị cảnh báo, không tự gộp.
- Giữ quy trình duyệt lịch trước nhập kho/lập phiếu và xác nhận từng kiện thực tế trước khi ghi. Không tự đánh dấu đã xuất hoặc đã nhận.
- Thêm regression tests về binding trực tiếp, xem vật tư theo dòng hợp đồng, vật tư nhiều kiện, thiếu HĐ, trường hợp trùng HĐ giữa T1/T2, lượng vật tư và ngày cont, cài mới/cài đè trên Windows.

---

# HISA Quản lý kho v1.9.4 — Đưa Đã nhập kho và Hàng gửi ra ngoài CONT ĐI

- **Sửa đường dẫn lưu:** `CONT ĐI`, `Đã nhập kho` và `Hàng gửi` là ba thư mục **ngang cấp**, không tạo thư mục Đã nhập kho/Hàng gửi ở trong CONT ĐI.
- **Không tự nhận cont:** đồng bộ file Excel nguồn không đồng nghĩa hàng đã về. Chỉ chuyển file qua Đã nhập kho khi tất cả kiện trong file có bản ghi nhận thực tế từ Tổng hợp.xlsx. File còn kiện chờ giữ nguyên.
- **Sửa dữ liệu phiên bản cũ:** di chuyển các file đang nằm nhầm `CONT ĐI\Hàng gửi` sang thư mục ngang cấp. Đối chiếu `CONT ĐI\Đã nhập kho` với dấu vết nhập thực tế; file chưa nhận chuyển lại CONT ĐI. Cảnh báo thay vì ghi đè khi trùng tên hoặc file đang thay đổi.
- **Bảo toàn:** tiếp tục quét nguồn Đã nhập kho cùng cấp để giữ ID vật tư/kiện và tránh mất dữ liệu khi đồng bộ. Chức năng ảnh Zalo/Gemini hai danh sách của v1.9.3 vẫn có mặt.
- **Kiểm thử:** thêm regression fixture Excel tạm kiểm tra nguồn chưa nhận, nhận một phần, thư mục ngang cấp, di chuyển thư mục cũ, trùng tên, ID nguồn bất biến và quét lại; build, self-test, cài mới/cài đè Windows trước khi phát hành.

---

# HISA Quản lý kho v1.9.3 – Hai việc rõ ràng sau khi đọc ảnh Zalo

- **Một màn hình chính:** chọn ảnh → Gemini đọc → duyệt kế hoạch → danh sách (1) Cont dự kiến về và (2) Hợp đồng dự kiến bàn giao, với nút riêng.
- **Cont về:** không coi ngày dự kiến là ngày thực tế; chỉ chọn hợp đồng khi cont đã về, rồi đối chiếu và tự tích *từng kiện thực nhận* ở màn xác nhận. Không tự tích kiện theo AI. Ghi `Tổng hợp.xlsx` chỉ sau xác nhận.
- **Phiếu:** tích hợp đồng ở danh sách bàn giao rồi mở kế hoạch phiếu chờ; cần đối chiếu vật tư và xác nhận trạng thái sau khi giao thực tế.
- Các mục bổ sung hợp đồng thủ công và cảnh báo chi tiết chuyển xuống vùng thu gọn, giữ nguyên tính năng.
- Thêm kiểm thử UI cho hai danh sách, lựa chọn độc lập và không ghi kho ngoài ý muốn; kiểm thử build, cài mới/cài đè Windows, EXE/ZIP và checksum trước khi phát hành.

---

# HISA Quản lý kho v1.9.2 — Khôi phục cấu hình nguồn dữ liệu

- **Khôi phục lối mở trực tiếp:** thanh menu trái có mục **Cấu hình nguồn dữ liệu**. Không cần mở qua Quản lý & lịch sử nữa.
- **Hiển thị chọn nguồn trước Gemini:** nút **Chọn thư mục nguồn** cùng trạng thái nhận diện Excel nằm ngay đầu trang cấu hình; các nút **Lưu cấu hình & tải lại** vẫn hoạt động như trước.
- **Đề xuất đường dẫn an toàn:** nếu cấu hình nguồn chưa có nhưng `Tổng hợp.xlsx` đang sử dụng vẫn tồn tại, hiện thư mục chứa file để người dùng kiểm tra; không tự ghi đè nguồn.
- **Bảo toàn dữ liệu:** không thay đổi thư mục chứa Excel, lịch sử phiếu, quy trình xuất và Gemini API Key khi nâng cấp. Kiểm thử UI Windows và cài đè chạy trong CI trước khi phát hành.

---

# HISA Quản lý kho v1.9.1 — sửa cài đặt và cập nhật Windows

- **Nút “↑ Cập nhật” hiển thị trực tiếp trên thanh công cụ** thay vì chỉ nằm trong Quản lý & lịch sử → Thông tin phần mềm. Khi tải xong bản nâng cấp đã xác minh, nút hiển thị số phiên bản mới.
- **Cập nhật có xác nhận:** kiểm tra GitHub Releases, tải bộ cài chính thức và đối chiếu SHA-256. HISA chỉ khởi chạy nâng cấp sau khi người dùng bấm “Cài bản đã tải”, xác nhận và đóng ứng dụng; không tự cài khi đóng mà chưa được phép.
- **Xử lý lỗi rõ ràng:** bộ cài hiển thị tiến trình, nếu không mở được/UAC bị từ chối hoặc checksum không đúng thì thông báo lỗi; phiên bản cũ và dữ liệu HISA được giữ nguyên.
- **Mở lại phần mềm đúng lúc:** trình cài chỉ tự mở HISA ở chế độ nâng cấp đã được xác nhận, tránh khởi chạy phần mềm ngoài ý muốn trong cài đặt hàng loạt/CI.
- **Cài thủ công:** có nút “Tải bộ cài thủ công” dẫn tới GitHub Releases. Nếu đang dùng HISA v1.8.1 không thấy mục cập nhật, hãy tải bản Setup này, thoát HISA và chạy cài đè; không cần xóa chương trình hay thư mục dữ liệu.
- **Kiểm thử Windows:** thêm kiểm tra cài mới, cài đè cùng AppId, phiên bản tệp đã cài và bảo toàn tệp không thuộc ứng dụng; thêm các bài kiểm tra URL tải bản chính thức, checksum và quyền cho phép cập nhật. Bản phát hành chỉ xuất khi GitHub Actions thành công.

Lưu ý: Cập nhật không tự ghi `Tổng hợp.xlsx`, không thay đổi các phiếu đã lập và không xóa Gemini API Key trong Windows Credential Manager. Windows vẫn có thể yêu cầu quyền quản trị (UAC).

---

# HISA Quản lý kho v1.9.0 – Trung tâm lịch nhập – xuất và hợp đồng phát sinh

## Một màn hình điều hành cho nhập và bàn giao
- Mở lịch từ **ảnh Zalo** qua Gemini hoặc tạo lịch trống rồi **nhập số hợp đồng thủ công**. Hai nguồn có thể bổ sung cho nhau trong cùng lịch đang xem; không tạo dữ liệu vật tư giả.
- Dán nhiều số hợp đồng, mỗi dòng một số. Chọn rõ **Cont dự kiến về Hưng Yên** hoặc **Hợp đồng dự kiến bàn giao**, bổ sung ngày, tên công trình, đơn vị, cont, ghi chú và ưu tiên.
- Tự nhận diện công trình/đơn vị chỉ khi hợp đồng trùng **đúng một danh tính** trong dữ liệu nguồn. Hợp đồng không tìm thấy hoặc trùng số trên nhiều công trình T1/T2 được cảnh báo, cho sửa tay.
- **Phần 1 – Nhập kho:** tích cột *Chọn nhận* theo hợp đồng; chương trình chuyển sang danh sách kiện nguồn đã khớp **ngày cont đi + số cont** để người dùng chọn lại và xác nhận hàng thực nhận. Nhiều kiện cùng HĐ phải chọn riêng; không nhập kho dựa vào ảnh dự kiến.
- **Phần 2 – Bàn giao:** tích cột *Làm phiếu* theo hợp đồng đã duyệt, đối chiếu vật tư và tạo phiếu chờ. Không xác nhận xuất hàng tự động.
- Có nút **Mở lịch đã duyệt** cả khi lịch chỉ gồm phần xuất; các lựa chọn tích nhập/xuất là trạng thái phiên hiện tại, không tự ghi sổ. Thay đổi nội dung lịch cần duyệt lại.
- Sử dụng Gemini API Key cũ để đọc ảnh; nhập tay không phụ thuộc API Key. Giữ nguyên dữ liệu kho, cột ngày cont, serial và cơ chế sao lưu/cập nhật.

## Kiểm thử
- Bộ kiểm thử offline: nhập nhiều mã, nhập trùng, tự nhận diện công trình duy nhất, hợp đồng chưa có nguồn, hai ngày cont đi cho cùng HĐ, nhập tay không ghi Excel, lịch chỉ có phần xuất được mở lại, cảnh báo thủ công được giải quyết sau khi điền đủ thông tin.
- Biên dịch .NET 10 WPF và kiểm thử Windows CI trước khi gộp/đóng gói phát hành.

---

# HISA Quản lý kho v1.8.1 – Sửa lỗi sau rà soát toàn diện

## Các lỗi nghiệp vụ được khắc phục
- **Không nhập nhầm kiện theo lịch AI**: nếu lịch Zalo có một dòng hợp đồng nhưng nguồn `Cont đi Hưng Yên` chứa nhiều kiện khác ngày xuất cont, HISA chỉ cho phép chọn kiện khớp chính xác **ngày cont đi hoặc mã container**. Không còn tự lấy tất cả kiện của hợp đồng khi không xác minh được nguồn.
- **Đối chiếu cả số cont và ngày xuất tại nhà máy**: nếu đã biết cả hai, chúng phải nhất quán; số cont trùng nhưng lịch xuất tại nhà máy khác ngày sẽ bị chặn.
- **Cùng số cont đi nhiều ngày**: nếu nguồn ghi nhiều đợt mà ảnh Zalo không có ngày cont đi, không cho nhập dựa trên phỏng đoán; thông báo **Chưa xác minh ngày cont đi**.
- **Kiện nguồn không có vật tư Hưng Yên**: ngăn ghi nhận nhập kho hoặc báo đã nhập khi không có dòng vật tư phù hợp; phải cập nhật/đối chiếu nguồn trước.
- **Chỉnh lịch AI sau phê duyệt**: sửa ngày dự kiến hoặc dữ liệu bảng sau khi duyệt sẽ hủy trạng thái duyệt ở phiên đang sửa. Không thể dùng nội dung thay đổi chưa được duyệt để chuẩn bị phiếu chờ hay ghi nhận cont thực tế.
- **Ngày cont đi trong tên công trình**: cập nhật lại hậu tố ngày `(dd.MM)` sẽ **thay thế** hậu tố cũ, không lặp thành `(26.10) (27.10)`; vẫn bảo toàn T1/T2, Thang 1/2, (1)/(2).
- **Trò chuyện Gemini có quyền xem dữ liệu**: trợ lý nhận trạng thái chuẩn bị được tính bởi quy tắc HISA thay vì tự suy luận hàng đã sẵn từ lịch dự kiến. Không gửi serial máy kéo hoặc file Excel gốc.

## Phạm vi rà soát & xác minh
- Kiểm tra mã nguồn WPF/.NET 10, luồng AI đọc ảnh – duyệt – nhập cont/phiếu chờ, quản lý Gemini API Key, file Excel, phân biệt kiện, bản phát hành tự cập nhật và các ca regression.
- Bổ sung kiểm thử tự động cho lịch một HĐ nhưng nhiều ngày cont đi, số cont trùng hai ngày, số cont đúng nhưng ngày không khớp, thiếu ngày, đổi hậu tố trong tên công trình và trạng thái chuẩn bị trong Gemini Chat.
- Các kiểm thử Excel chạy trên workbook tạm, không chỉnh sửa `Tổng hợp.xlsx` của người dùng.
- GitHub CI phải biên dịch WPF trên Windows, chạy toàn bộ regression và tạo bộ cài EXE/ZIP trước khi phát hành.

## Giới hạn còn cần thử nghiệm tại máy kho
- Chưa có ảnh chụp giao diện thật trong phiên rà soát này và không thể xác minh thao tác chuột, tỷ lệ DPI/màn hình trên máy của người dùng chỉ bằng CI.
- Chưa có file Excel máy kéo/đầu cửa cabin thực tế để xác minh cấu trúc, serial và quy tắc đối chiếu dữ liệu đó; không kết luận thiếu hàng nếu chưa nhập nguồn.
- Gemini API cần thử bằng khóa riêng trên máy người dùng; CI không có khóa API và không gửi ảnh Zalo thật.
- Hãy sao lưu `Tổng hợp.xlsx` và đối chiếu 2–3 hợp đồng mẫu (cùng số HĐ, khác cont/ngày, T1/T2) trên máy thật trước khi ghi hàng loạt.

---

# HISA Quản lý kho v1.8.0 – Giao diện tối ưu theo công việc kho Hưng Yên

## Gộp menu và loại bỏ thao tác trùng lặp
- Từ 15 mục điều hướng riêng lẻ thành **7 nhóm chính**: Tổng quan, Điều hành xuất hàng, Cont & kiện Hưng Yên, Tra cứu hợp đồng, Trung tâm Gemini AI, Báo cáo & tiện ích, Cấu hình & sao lưu.
- Các tính năng v1.7.0 vẫn được giữ nguyên và mở từ các trung tâm chức năng. Không xóa dữ liệu, phiếu, trạng thái hoặc lịch sử cũ.
- Màn hình **Điều hành xuất hàng** dẫn lần lượt: lịch dự kiến → đối chiếu vật tư → lập phiếu chờ → bàn giao sau xác nhận. Gemini đọc ảnh và nhập hợp đồng phát sinh là các cách tạo lịch trong cùng quy trình.
- Màn **Cont & kiện Hưng Yên** kết hợp lịch cont dự kiến, xác nhận cont về thực tế, nhập kho thủ công, kiểm soát kiện cont đi và kiểm tra kiện cont về. Không tự hiểu lịch dự kiến là đã nhập.
- Màn **Trung tâm Gemini AI** gộp lối vào trò chuyện, đọc ảnh Zalo và cấu hình API Key. Trò chuyện chỉ gửi dữ liệu kho khi người dùng bật quyền chia sẻ.
- Màn **Báo cáo & tiện ích** giữ báo cáo, mẫu phiếu in và thông tin phần mềm; truy cập phiếu bàn giao từ trung tâm xuất hàng.

## Tối ưu trang chủ
- Khi có lịch **hôm nay và ngày mai**, hiển thị cả hai thay vì chỉ một ngày; ưu tiên hợp đồng khẩn và chưa sẵn sàng.
- Hàng dự kiến về kho được hiển thị riêng, ghi rõ chưa phải hàng đã nhập thực tế.
- Thao tác nhanh dẫn tới đúng trung tâm công việc, tránh phải tìm kiếm nhiều mục.
- Bảng nhóm vật tư và phiếu gần đây được thu gọn theo yêu cầu; không mở quá nhiều bảng cùng lúc.
- **Rà soát số hợp đồng trùng** được chuyển khỏi trang chủ vào mục nâng cao của Cont & kiện; trùng HĐ không đồng nghĩa kiện trùng. Giữ nguyên hộp xác nhận trước khi xóa dữ liệu nhập sai và không tự gộp kiện.

## Tương thích và an toàn
- Các màn chức năng và đường dẫn điều hướng nội bộ cũ vẫn hoạt động.
- Giữ nguồn Excel, tồn kho, mã cont, ngày cont đi, T1/T2, số lượng, serial máy kéo, trạng thái phiếu và quy trình sao lưu.
- Gemini API Key vẫn được Windows Credential Manager lưu qua các lần nâng cấp cùng tài khoản.
- Phím tắt: Ctrl+1 Tổng quan, Ctrl+2 Điều hành xuất, Ctrl+3 Cont & kiện, Ctrl+4 Trung tâm Gemini, Ctrl+F Tra cứu, Ctrl+N Tạo phiếu, F5 Đồng bộ.
- Kiểm thử hồi quy xác minh đường dẫn của các mục cũ và logic hiển thị lịch; CI Windows biên dịch, tạo bộ cài tiếng Việt, ZIP và SHA-256.

---

# HISA Quản lý kho v1.7.0 – Trợ lý trò chuyện Gemini AI (Windows x64)

## Trò chuyện trực tiếp trong HISA
- Menu **Trợ lý Gemini AI** mở khung hội thoại tiếng Việt với bong bóng tin nhắn, câu hỏi gợi ý, gửi bằng Enter, Shift+Enter xuống dòng, xóa lịch sử trong phiên và đường dẫn tới Cấu hình.
- Sử dụng **Gemini API Key hiện có** đã lưu tại Windows Credential Manager; không cần nhập lại, không dùng GPT hay OpenAI.
- Hỗ trợ hội thoại nhiều lượt; lưu tối đa 24 tin gần đây trong bộ nhớ ứng dụng, không tạo file hội thoại hay đưa khóa API vào nhật ký.
- Tùy chọn **Cho phép Gemini đọc tóm tắt dữ liệu kho liên quan**, **mặc định TẮT**. Khi bật, chỉ gửi thông tin rút gọn có giới hạn: lịch xuất hôm nay/ngày mai hoặc hợp đồng được hỏi, vật tư theo số HĐ đầy đủ, tình trạng phiếu. Không gửi file Excel gốc, đường dẫn nguồn hoặc serial máy kéo. Chuyển trạng thái quyền chia sẻ sẽ xóa hội thoại trong phiên để tránh dùng lại dữ liệu riêng.
- AI có hướng dẫn nghiệp vụ Hưng Yên: dự kiến về ≠ nhập thực tế; chuẩn bị xong ≠ đã bàn giao; Đã có LX = đã có lịch xuất; cùng số HĐ nhưng khác công trình/cont/ngày cont đi vẫn có kiện riêng; thiếu dữ liệu phải báo Chưa xác nhận.
- AI **chỉ tư vấn**, không thực hiện lệnh ghi Excel, thay đổi tồn kho, tạo phiếu, xác nhận xuất/nhập; mọi thao tác vẫn phải thực hiện và duyệt trong màn hình nghiệp vụ HISA.
- Kết nối Gemini cần Internet và hạn mức API đang còn. Thông báo lỗi HTTP được giải thích mà không hiển thị khóa API.

## Kiểm thử
- Kiểm thử offline trích mã hợp đồng chính xác, rút gọn ngữ cảnh và lịch sử, loại bỏ thông tin đường dẫn và serial, xử lý JSON phản hồi, giới hạn câu hỏi và xử lý nội dung không đáng tin trong dữ liệu kho.
- Windows CI biên dịch .NET 10, chạy regression, đóng gói installer tiếng Việt và ZIP trước khi phát hành.

---

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
