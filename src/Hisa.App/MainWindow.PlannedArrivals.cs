using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowPlannedArrivals(bool onlyMarkedContracts=false)
    {
        _page = "planned-arrivals";
        // A changed but unapproved on-screen schedule must never be used to record real arrivals.
        var approved = _aiApproved && _aiDocument is { ApprovedAt: { Length: > 0 } }
            ? _aiDocument : AiScheduleWorkspace.LatestApproved(_service.DataDirectory);
        if(onlyMarkedContracts && (approved==null || approved.Arrivals.All(x=>!x.SelectedForArrival)))
        {
            Notify("Không có hợp đồng phần cont về nào được tích chọn. Mở màn lịch để chọn đúng hợp đồng trước.");
            return;
        }
        if (approved is null)
        {
            SetPage("Cont đã về kho",
                "Từ lịch Gemini đã duyệt đến cập nhật thực tế trong Tổng hợp.xlsx",
                Card(Stack(
                    T("CHƯA CÓ LỊCH CONT ĐƯỢC DUYỆT", 18, true),
                    T("Hãy đọc ảnh Zalo bằng Gemini, kiểm tra và lưu lịch dự kiến trước. " +
                      "Không tự ghi nhập kho từ ngày dự kiến.", 12, false, Muted),
                    Row(B("Đọc ảnh Zalo với Gemini", () => Navigate("ai-schedule"), true),
                        B("Nhập kho thủ công", () => Navigate("arrival"))))));
            return;
        }
        var date = DateInput(DateTime.Today.ToString("yyyy-MM-dd"));

        var feedback = T("Chọn đúng ngày cont THỰC TẾ đã về và tích TỪNG kiện hàng đã nhận.", 12, false, Muted);
        var rows = new List<PlannedArrivalRow>();
        var grid = Table(rows,
            ("Mã cont", "Container", 140),
            ("Số hợp đồng", "Contract", 130),
            ("Công trình", "Project", -1),
            ("Công ty", "Company", 165),
            ("Kiện số", "PackageNo", 80),
            ("Ngày cont đi", "SourceContDay", 125),
            ("Tên vật tư · số lượng", "MaterialSummary", 290),
            ("Trạng thái đối chiếu", "Status", 230),
            ("Chi tiết", "Notes", 245));
        grid.IsReadOnly = false;
        grid.CanUserAddRows = false;
        // The second confirmation screen must also accept one-click selection.
        grid.Columns.Insert(0,OneClickCheckColumn("Đã về","Selected",83));
        void preview()
        {
            if (Day(date).Length == 0)
            {
                feedback.Text = "Chọn ngày cont thực tế về kho để đối chiếu.";
                return;
            }
            rows = onlyMarkedContracts
                ? AiScheduleWorkspace.SelectedArrivalPreview(approved,_service.Snapshot,Day(date))
                : PlannedArrivalWorkflow.Preview(approved,_service.Snapshot,Day(date));
            // Never automatically mark source packages as received: the planned date is only a forecast.
            grid.ItemsSource = rows;
            var ready = rows.Count(x => x.Status == "Đủ nguồn · chờ xác nhận đã về");
            feedback.Text = $"{rows.Count} kiện từ lịch đã duyệt · {ready} kiện đủ nguồn · " +
                $"{rows.Count - ready} kiện cần kiểm tra. " +
                (onlyMarkedContracts
                    ? "Đã lọc theo HĐ tích ở ảnh Zalo. Chưa kiện nào được tự đánh dấu đã về: hãy tự chọn từng kiện thực nhận."
                    : "Tích riêng từng KIỆN đã về THỰC TẾ (dù trùng số hợp đồng).");
        }
        date.SelectedDateChanged += (_, _) => preview();
        var actions = Row(
            B("↻  Đối chiếu lại", preview),
            B("Xem tất cả kiện trong lịch",()=>ShowPlannedArrivals()),
            B("Bỏ chọn", () =>
            {
                foreach (var r in rows) r.Selected = false;
                grid.Items.Refresh();
            }),
            A("✓  Xác nhận cont đã về · Ghi Tổng hợp.xlsx", async () =>
            {
                Commit(grid);
                if (Day(date).Length == 0 || date.SelectedDate is null)
                    throw new InvalidOperationException("Hãy chọn ngày cont thực tế về kho.");
                var marked = rows.Where(x => x.Selected).ToList();
                if (marked.Count == 0)
                    throw new InvalidOperationException("Tích vào ô 'Đã về' cho container thực tế đã nhận.");
                await _service.RefreshAsync(false);
                var selected = PlannedArrivalWorkflow.SelectVerified(marked, approved,
                    _service.Snapshot, Day(date));
                if (!Ask("XÁC NHẬN HÀNG ĐÃ VỀ KHO HƯNG YÊN\n\n" +
                    "Ngày thực tế: " + Rules.Day(Day(date)) + "\n" +
                    "Container: " + string.Join(", ", marked.Select(x => x.Container).Distinct()) + "\n" +
                    "Hợp đồng: " + marked.Select(x=>Rules.Contract(x.Contract)).Distinct().Count() + "\n" +
                    "Kiện Hưng Yên: " + selected.Count + "\n" +
                    "Dòng vật tư nguồn: " + selected.Sum(x => x.Items.Count) + "\n\n" +
                    "Ghi vào trang chi tiết của Tổng hợp.xlsx (sao lưu trước khi ghi). " +
                    "Những hợp đồng chưa tích không được nhập. Tiếp tục?")) return;
                var output = await _service.RecordVerifiedArrivalAsync(selected,
                    date.SelectedDate.Value, false);
                Notify($"Đã nhập {output.Contracts} HĐ · {output.Packages} kiện · " +
                       $"{output.Lines} dòng vào Tổng hợp.xlsx.\n" +
                       string.Join("\n", output.Warnings));
                ShowPlannedArrivals();
            }, true)
        );
        var explanation = Card(Stack(
            T(onlyMarkedContracts?"CONT ĐÃ VỀ · HĐ ĐƯỢC CHỌN TỪ LỊCH":"CONT HÀNG ĐÃ VỀ KHO",18,true),
            T("Lịch Gemini chỉ dự báo cont. Chỉ khi thực nhận mới tích đúng từng KIỆN và xác nhận ghi Tổng hợp.xlsx. " +
              "HISA chỉ chuyển file sang Đã nhập kho (NGANG CẤP CONT ĐI) nếu toàn bộ kiện trong file đã nhận; file còn kiện chờ vẫn giữ trong CONT ĐI.",
                12, false, Muted),
            Row(Field("Ngày cont thực tế về", date),
                T("Mỗi kiện có ID riêng · không gộp T1/T2, số lượng hay cont đi khác nhau", 12, true, Brush("#21847D"))),
            feedback,
            actions
        ));
        SetPage("Cont đã về kho",
            "Tích container thực tế đã nhận · đối chiếu HĐ/kiện · cập nhật Tổng hợp.xlsx",
            Layout(explanation, grid));
        preview();
    }
}
