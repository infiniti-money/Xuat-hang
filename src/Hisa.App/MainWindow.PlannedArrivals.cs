using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowPlannedArrivals()
    {
        _page = "planned-arrivals";
        // A changed but unapproved on-screen schedule must never be used to record real arrivals.
        var approved = _aiApproved && _aiDocument is { ApprovedAt.Length: > 0 }
            ? _aiDocument : PlannedArrivalWorkflow.LoadLatestApproved(_service.DataDirectory);
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
        var useKt = new CheckBox { Content = "Ưu tiên vật tư KT nếu đối chiếu đúng công trình", IsChecked = true };
        var feedback = T("Chọn đúng ngày cont THỰC TẾ đã về và các dòng cần nhập kho.", 12, false, Muted);
        var rows = new List<PlannedArrivalRow>();
        var grid = Table(rows,
            ("Mã cont", "Container", 140),
            ("Số hợp đồng", "Contract", 130),
            ("Công trình", "Project", -1),
            ("Công ty", "Company", 165),
            ("Kiện nguồn", "PackageCount", 88),
            ("Ngày dự kiến", "PlannedDate", 125),
            ("Trạng thái đối chiếu", "Status", 235),
            ("Chi tiết", "Notes", 270));
        grid.IsReadOnly = false;
        grid.CanUserAddRows = false;
        grid.Columns.Insert(0, new DataGridCheckBoxColumn
        {
            Header = "Đã về",
            Binding = new Binding("Selected")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = 70
        });
        void preview()
        {
            if (Day(date).Length == 0)
            {
                feedback.Text = "Chọn ngày cont thực tế về kho để đối chiếu.";
                return;
            }
            rows = PlannedArrivalWorkflow.Preview(approved, _service.Snapshot, Day(date));
            grid.ItemsSource = rows;
            var ready = rows.Count(x => x.Status == "Đủ nguồn · chờ xác nhận đã về");
            feedback.Text = $"{rows.Count} dòng cont trong lịch AI đã duyệt · {ready} dòng đủ kiện nguồn · " +
                $"{rows.Count - ready} dòng cần kiểm tra. Chỉ tích những dòng đã về THỰC TẾ.";
        }
        date.SelectedDateChanged += (_, _) => preview();
        var actions = Row(
            B("↻  Đối chiếu lại", preview),
            B("Tích các dòng đủ nguồn", () =>
            {
                foreach (var r in rows) r.Selected = r.Status == "Đủ nguồn · chờ xác nhận đã về";
                grid.Items.Refresh();
            }),
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
                    "Hợp đồng: " + marked.Count + "\n" +
                    "Kiện Hưng Yên: " + selected.Count + "\n" +
                    "Dòng vật tư nguồn: " + selected.Sum(x => x.Items.Count) + "\n\n" +
                    "Ghi vào trang chi tiết của Tổng hợp.xlsx (sao lưu trước khi ghi). " +
                    "Những hợp đồng chưa tích không được nhập. Tiếp tục?")) return;
                var output = await _service.RecordVerifiedArrivalAsync(selected,
                    date.SelectedDate.Value, useKt.IsChecked == true);
                Notify($"Đã nhập {output.Contracts} HĐ · {output.Packages} kiện · " +
                       $"{output.Lines} dòng vào Tổng hợp.xlsx.\n" +
                       string.Join("\n", output.Warnings));
                ShowPlannedArrivals();
            }, true)
        );
        var explanation = Card(Stack(
            T("CONT HÀNG ĐÃ VỀ KHO", 18, true),
            T("Lịch Gemini chỉ dự báo container. Khi xe đã về kho thực tế, tích đúng container rồi " +
              "xác nhận để cập nhật trang chi tiết của Tổng hợp.xlsx. Không ghi nhập chỉ vì có lịch.",
                12, false, Muted),
            Row(Field("Ngày cont thực tế về", date), useKt),
            feedback,
            actions
        ));
        SetPage("Cont đã về kho",
            "Tích container thực tế đã nhận · đối chiếu HĐ/kiện · cập nhật Tổng hợp.xlsx",
            Layout(explanation, grid));
        preview();
    }
}
