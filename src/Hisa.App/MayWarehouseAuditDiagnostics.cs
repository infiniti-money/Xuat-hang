using System.IO;

namespace Hisa.App;

/// <summary>
/// Verifies HISA cross-file reconciliation and Zalo select-all with entirely
/// local synthetic records. No Gemini API, live Excel files or writes required.
/// </summary>
public static class MayWarehouseAuditDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool good,string name)
        {
            if(!good)throw new InvalidOperationException("Mây audit: "+name);
            checks.Add("Mây audit: "+name);
        }
        var doc=new AiScheduleDocument
        {
            ArrivalDate="2026-11-02",DispatchDate="2026-11-03",
            Arrivals=[
                new AiScheduleLine{Contract="56062026",Project="T1",Company="A",
                    PlannedDate="2026-11-02"},
                new AiScheduleLine{Contract="13482026",Project="T2",Company="B",
                    PlannedDate="2026-11-02"}
            ],
            Dispatches=[
                new AiScheduleLine{Contract="56062026",Project="T1",Company="A",
                    PlannedDate="2026-11-03",HasExportSchedule=true},
                new AiScheduleLine{Contract="13482026",Project="T2",Company="B",
                    PlannedDate="2026-11-03",HasExportSchedule=null}
            ]
        };
        Check(AiScheduleBulkSelection.SetAll(doc,true,true)==2 &&
              doc.Arrivals.All(x=>x.SelectedForArrival) &&
              doc.Dispatches.All(x=>!x.SelectedForHandover),
            "Select all incoming contracts does not change dispatch selection");
        Check(AiScheduleBulkSelection.SetAll(doc,false,true)==2 &&
              doc.Dispatches.All(x=>x.SelectedForHandover),
            "Select all outgoing contracts works independently");
        Check(AiScheduleBulkSelection.SetAll(doc,true,false)==2 &&
              doc.Arrivals.All(x=>!x.SelectedForArrival) &&
              doc.Dispatches.All(x=>x.SelectedForHandover),
            "Uncheck incoming does not clear chosen outgoing");
        Check(string.IsNullOrWhiteSpace(doc.ApprovedAt) &&
              doc.Arrivals.All(x=>x.PlannedDate=="2026-11-02"),
            "Bulk selecting a draft neither approves arrival nor changes planned dates");
        var root=Path.Combine(Path.GetTempPath(),"may-audit-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var machineRows=new List<EquipmentListReader.Row>
            {
                new(){Category="Máy kéo",Contract="56062026",Project="T1",
                    Company="A",Material="Máy kéo 15 kW",Serial="MOTOR-001",
                    MotorColumnK="2026-11-01",Sheet="Máy kéo",SourceRow=3},
                new(){Category="Máy kéo",Contract="56062026",Project="T2",
                    Company="A",Material="Máy kéo 20 kW",Serial="MOTOR-002",
                    MotorColumnK="Chưa làm",Sheet="Máy kéo",SourceRow=4}
            };
            var machines=new MotorHungYenStore(root).List(machineRows);
            var status=MayWarehouseAudit.MotorsFor(doc.Dispatches[0],machines);
            Check(status.Count==2 && status[0].Serial.Length>0 &&
                  status.Any(x=>x.DispatchDate=="2026-11-01"),
                "Compare actual export day from Excel K with serial for matching contract");
            Check(status.Count(x=>x.Verification.StartsWith("Khớp"))==1 &&
                  status.Count(x=>x.Verification.StartsWith("CẦN KIỂM TRA"))==1,
                "Same contract with different T1/T2 is flagged, not silently treated as the same machine");
            Check(status.Any(x=>x.WorkStatus=="Chưa làm"),
                "Machine still in production is not shown as completed");
            var projection=MayWarehouseAudit.BuildReadOnlyProjection("Máy kéo HĐ 56062026",
                new AppSettings(),new WorkbookSnapshot(),
                new EquipmentListReader.Catalog(machineRows,[]),machines);
            Check(projection.Contains("MOTOR-001") &&
                  projection.Contains("2026-11-01") &&
                  projection.Contains("MÁY KÉO.xlsx"),
                "Read-only Gemini context includes exact queried serial, verified day and configured-file coverage");
            var noContract=MayWarehouseAudit.BuildReadOnlyProjection("Hôm nay có gì?",
                new AppSettings(),new WorkbookSnapshot(),
                new EquipmentListReader.Catalog(machineRows,[]),machines);
            Check(!noContract.Contains("MOTOR-001"),
                "Unscoped conversation never uploads detailed serials");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return checks;
    }
}
