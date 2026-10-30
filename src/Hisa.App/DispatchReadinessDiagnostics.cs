using System.IO;

namespace Hisa.App;

// Isolated read-only business-rule verification; no user's Excel/workbooks are modified.
public static class DispatchReadinessDiagnostics
{
    public static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool good, string name)
        {
            if (!good) throw new InvalidOperationException("FAIL: Dispatch readiness: " + name);
            checks.Add("Dispatch readiness: " + name);
        }

        var root = Path.Combine(Path.GetTempPath(), "hisa-ready-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DispatchReadinessStore(root);
            var state = store.Load();
            const string day = "2026-10-31";
            DispatchReadinessStore.AddManual(state, day, "56062026", "Công trình A", "Công ty A",
                "Kho thành phẩm", "", "");
            var same = DispatchReadinessStore.AddManual(state, day, "56062026", "Công trình A", "Công ty A",
                "Kho thành phẩm", "Khách cần gấp", "");
            Check(state.Plans.Count == 1 && same.Reason == "Khách cần gấp", "Manual duplicate is updated, reason optional");

            var snapshot = new WorkbookSnapshot
            {
                Packages =
                [
                    new Package
                    {
                        Contract = "56062026", Project = "Công trình A", Company = "Công ty A",
                        Items =
                        [
                            new MaterialLine {Contract="56062026",Project="Công trình A",Company="Công ty A",Material="Đầu cửa tầng 700",Unit="Bộ",Quantity=1},
                            new MaterialLine {Contract="56062026",Project="Công trình A",Company="Công ty A",Material="Đầu cửa tầng 750",Unit="Bộ",Quantity=1},
                            new MaterialLine {Contract="56062026",Project="Công trình A",Company="Công ty A",Material="Máy kéo M1",Unit="Bộ",Quantity=2}
                        ]
                    }
                ]
            };
            var first = DispatchReadinessStore.Evaluate(state, snapshot, day).Single();
            Check(first.Materials.Count == 3 && first.Status == "Chưa cập nhật đầy đủ",
                "Missing preparation data is not falsely treated as shortage");
            var width700 = first.Materials.Single(x => x.Material == "Đầu cửa tầng 700");
            var width750 = first.Materials.Single(x => x.Material == "Đầu cửa tầng 750");
            Check(width700.MaterialKey != width750.MaterialKey, "700 and 750 remain separate identities");
            DispatchReadinessStore.UpdatePrepared(state, width700, 1, "");
            var second = DispatchReadinessStore.Evaluate(state, snapshot, day).Single();
            Check(second.Materials.Single(x => x.Material == "Đầu cửa tầng 750").Prepared == null,
                "Preparing 700 does not prepare 750");
            DispatchReadinessStore.UpdatePrepared(state, width750, 1, "");
            var motor = second.Materials.Single(x => x.Material == "Máy kéo M1");
            DispatchReadinessStore.UpdatePrepared(state, motor, 2, "");
            Check(DispatchReadinessStore.Evaluate(state,snapshot,day).Single().Status == "Cần kiểm tra serial",
                "No motor serial means not ready");
            bool duplicateBlocked = false;
            try { DispatchReadinessStore.UpdatePrepared(state, motor, 2, "SER-01\nSER-01"); }
            catch (InvalidOperationException) { duplicateBlocked = true; }
            Check(duplicateBlocked, "Duplicate serial blocked");
            DispatchReadinessStore.UpdatePrepared(state, motor, 2, "SER-01\nSER-02");
            Check(DispatchReadinessStore.Evaluate(state,snapshot,day).Single().Status == "Sẵn sàng bàn giao thành phẩm",
                "All confirmed quantities and motor serials make plan ready, without export");
            var doc = new AiScheduleDocument
            {
                DispatchDate = day, ArrivalDate = day, ApprovedAt = DateTimeOffset.Now.ToString("O"),
                Arrivals = [new AiScheduleLine { Contract="CONT-ONLY",Project="Container",PlannedDate=day }],
                Dispatches = [new AiScheduleLine { Contract="13482026",Project="Công trình B",PlannedDate=day, Urgent=true }]
            };
            Check(DispatchReadinessStore.ImportApproved(state, doc, day) == 1 &&
                state.Plans.Count == 2 && !state.Plans.Any(x=>x.Contract=="CONT-ONLY"),
                "Import only dispatches from approved AI, preserve manual plans");
            store.Save(state);
            var reopened = store.Load();
            Check(reopened.Plans.Count == 2 && reopened.Prepared.Count == 3,
                "Separate readiness JSON survives reopening without Excel writes");
            Check(DispatchReadinessStore.Evaluate(reopened,snapshot,"2026-11-01").Count == 0,
                "Plan remains scoped to selected day");
            return checks;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
