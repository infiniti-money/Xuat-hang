namespace Hisa.App;

/// <summary>Builds an editable draft report exclusively from explicitly checked AI dispatch rows.</summary>
public static class AiHandoverSelection
{
    public static ContractReport Build(ContractReport report, AiScheduleDocument schedule,
        IReadOnlyList<AiScheduleLine> selected)
    {
        if (string.IsNullOrWhiteSpace(schedule.ApprovedAt))
            throw new InvalidOperationException("Hãy kiểm tra và lưu lịch Gemini đã duyệt trước khi lập phiếu.");
        if (!report.DataAvailable)
            throw new InvalidOperationException("Tổng hợp.xlsx chưa sẵn sàng. Cập nhật nguồn Excel trước khi lập phiếu.");
        if (selected.Count == 0)
            throw new InvalidOperationException("Tích chọn ít nhất một hợp đồng ở phần Hàng dự kiến xuất kho.");
        if (selected.Any(x => !schedule.Dispatches.Contains(x)))
            throw new InvalidOperationException("Chỉ các dòng HÀNG DỰ KIẾN XUẤT KHO mới được lập phiếu.");

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
            if (sameContract.Count > 0)
                throw new InvalidOperationException("HĐ " + ai.Contract + " · " + ai.Project +
                    " không khớp chính xác công trình/công ty trong Tổng hợp.xlsx. " +
                    "Hãy sửa dòng AI hoặc tra cứu hợp đồng; không tự chọn nhầm công trình.");

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
