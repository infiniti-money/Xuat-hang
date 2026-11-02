namespace Hisa.App;

/// <summary>Offline bulk-checkbox tests; never imports or modifies an Excel workbook.</summary>
public static class PlannedArrivalBulkDiagnostics
{
    public static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("FAIL: " + label);
            checks.Add(label);
        }
        var ready = "Đủ nguồn · chờ xác nhận đã về";
        var rows = new List<PlannedArrivalRow>
        {
            new() { Contract = "1001", Status = ready, SourcePackageId = "physical-1" },
            new() { Contract = "1002", Status = ready, SourcePackageId = "duplicate" },
            new() { Contract = "1002", Status = ready, SourcePackageId = "duplicate" },
            new() { Contract = "1003", Status = "Đã nhập kiện này", SourcePackageId = "old", Selected = true },
            new() { Contract = "1004", Status = ready, SourcePackageId = "" },
            new() { Contract = "1005", Status = "Cần duyệt lại từ ảnh", SourcePackageId = "review" }
        };
        var count = PlannedArrivalWorkflow.SelectAllEligibleForReview(rows);
        Check(count == 1 && rows[0].Selected &&
              rows.Skip(1).All(x => !x.Selected),
            "Select-all marks only unique verified source packages, not duplicates, stale or unreviewed rows");
        Check(rows[3].Status == "Đã nhập kiện này",
            "Bulk preview leaves already-imported source status unchanged");
        Check(PlannedArrivalWorkflow.SelectAllEligibleForReview(rows) == 1,
            "Repeated select-all is idempotent and only changes in-memory checkboxes");
        return checks;
    }
}
