namespace Hisa.App;

/// <summary>Builds an editable draft report exclusively from explicitly checked AI dispatch rows.</summary>
public static class AiHandoverSelection
{
    // Only explicit numeric subproject markers are used as a hard conflict.
    // Ordinary project names and addresses may differ between OCR and Excel.
    private static string ExplicitTower(string? project)
    {
        var code = Rules.ProjectCode(project);
        var towerIndex = code.LastIndexOf("|T:", StringComparison.Ordinal);
        if (towerIndex < 0) return "";
        var tower = code[(towerIndex + 3)..];
        if (tower.Length > 0) return tower;
        var parentIndex = code.IndexOf("|P:", StringComparison.Ordinal);
        if (parentIndex < 0) return "";
        var parent = code[(parentIndex + 3)..towerIndex];
        var digits = parent.TrimStart('T');
        return digits.Length > 0 && digits.All(char.IsDigit) ? digits : "";
    }

    public static ContractReport Build(ContractReport report, AiScheduleDocument schedule,
        IReadOnlyList<AiScheduleLine> selected)
    {
        // No separate schedule-approval step: an explicit checkbox is enough
        // to prepare an unsaved draft, but source identity must still be exact.
        // A physical delivery is authorized only by the regular slip editor.
        if (!report.DataAvailable)
            throw new InvalidOperationException("Tổng hợp.xlsx chưa sẵn sàng. Cập nhật nguồn Excel trước khi lập phiếu.");
        if (selected.Count == 0)
            throw new InvalidOperationException("Tích chọn ít nhất một hợp đồng ở phần Hàng dự kiến xuất kho.");
        if (selected.Any(x => !schedule.Dispatches.Contains(x)))
            throw new InvalidOperationException("Chỉ các dòng HÀNG DỰ KIẾN XUẤT KHO mới được lập phiếu.");
        if(selected.Any(x=>x.NeedsReview))
            throw new InvalidOperationException("Có hợp đồng đang cần kiểm tra dữ liệu ảnh Zalo. "+
                "Sửa dòng thiếu/sai số HĐ, công trình, đơn vị hoặc ngày trước khi lập phiếu.");

        var candidates = report.Rows.Concat(report.NoMaterialMatch).ToList();
        foreach (var row in candidates.Concat(report.Missing)) row.Selected = false;
        var selectedCount = 0;
        var matches = new HashSet<string>();
        foreach (var ai in selected)
        {
            var contract = Rules.Contract(ai.Contract);
            if (contract.Length == 0 || string.IsNullOrWhiteSpace(ai.Project) ||
                string.IsNullOrWhiteSpace(ai.Company))
                throw new InvalidOperationException("Dòng lịch xuất thiếu số HĐ, công trình hoặc công ty. " +
                    "Sửa lại trong bảng AI trước khi chọn làm phiếu.");
            if (!string.Equals(ai.PlannedDate, schedule.DispatchDate, StringComparison.Ordinal))
                throw new InvalidOperationException("HĐ " + ai.Contract +
                    " có ngày xuất khác ngày lịch đang duyệt. Hãy kiểm tra lại ảnh và ngày dự kiến.");

            var identity = Rules.Identity(ai.Contract, ai.Project, ai.Company);
            var exact = candidates.Where(x => x.Identity == identity).ToList();
            if (exact.Count > 1)
                throw new InvalidOperationException("HĐ " + ai.Contract +
                    " trùng công trình/công ty trong nguồn dữ liệu; kiểm tra trước khi tạo phiếu.");
            if (exact.Count == 1)
            {
                exact[0].Selected = true;
                if (matches.Add(identity)) selectedCount++;
                continue;
            }

            var sameContract = candidates.Where(x => Rules.Contract(x.Contract) == contract).ToList();
            // Zalo OCR often expands a customer name into its street address,
            // while the Excel master keeps a shorter project/company label.
            // For ONE authoritative source identity, the contract number is
            // sufficient; the draft receives the original Excel fields.
            // Different explicit T1/T2 (or numbered subprojects) still block.
            if (sameContract.Count == 1)
            {
                var source = sameContract[0];
                var aiTower = ExplicitTower(ai.Project);
                var sourceTower = ExplicitTower(source.Project);
                if (aiTower.Length > 0 && sourceTower.Length > 0 &&
                    aiTower != sourceTower)
                    throw new InvalidOperationException("HĐ " + ai.Contract +
                        " có thang/công trình (" + aiTower + ") khác dữ liệu Excel (" +
                        sourceTower + "). Kiểm tra lại đúng T1/T2 trước khi lập phiếu.");
                source.Selected = true;
                if (matches.Add(source.Identity)) selectedCount++;
                report.Warnings.Add("HĐ " + source.Contract +
                    ": tự đối chiếu theo số hợp đồng duy nhất. Dùng công trình/đơn vị " +
                    "từ Tổng hợp.xlsx thay cho tên OCR từ Zalo.");
                continue;
            }
            if (sameContract.Count > 1)
                throw new InvalidOperationException("HĐ " + ai.Contract +
                    " có nhiều công trình/đơn vị trong Tổng hợp.xlsx: " +
                    string.Join("; ",sameContract.Take(6).Select(x => x.Project +
                        " / " + x.Company)) +
                    ". Hãy chọn đúng T1/T2 hoặc công trình; không tự gộp kiện.");

            var missing = report.Missing.SingleOrDefault(x => Rules.Contract(x.Contract) == contract);
            if (missing == null)
                throw new InvalidOperationException("Không tìm thấy HĐ " + ai.Contract +
                    " trong kết quả tra cứu; vui lòng tải lại nguồn.");
            missing.Project = ai.Project;
            missing.Company = ai.Company;
            missing.Selected = true; // explicit selection makes a waiting slip with no invented material
            if (matches.Add(identity)) selectedCount++;
        }
        if (selectedCount == 0)
            throw new InvalidOperationException("Chưa có dòng lịch xuất hợp lệ được tích chọn.");
        return report;
    }
}
