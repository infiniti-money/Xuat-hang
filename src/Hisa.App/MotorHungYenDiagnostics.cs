using System.IO;

namespace Hisa.App;

/// <summary>Isolated, file-backed tests. No user workbook, real shipping or stock mutation.</summary>
public static class MotorHungYenDiagnostics
{
    public static List<string> Run(string workspace)
    {
        var checks=new List<string>();
        void Check(bool ok,string label)
        {
            if(!ok)throw new InvalidOperationException("Motor tracker regression failed: "+label);
            checks.Add("Motor tracker: "+label);
        }
        var dir=Path.Combine(workspace,"motor-hy-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var row1=new EquipmentListReader.Row
        {
            Category="Máy kéo",Contract="05492026",Project="Công trình A",
            Company="Công ty A",Material="Máy kéo Montanary MGX80",
            Quantity=1,Serial="85893",Lot="Lô 01234",Sheet="DS máy kéo",SourceRow=123
        };
        var row2=new EquipmentListReader.Row
        {
            Category="Máy kéo",Contract="05492026",Project="Công trình A",
            Company="Công ty A",Material="Máy kéo Montanary MGX80",
            Quantity=1,Serial="85894",Lot="Lô 01234",Sheet="DS máy kéo",SourceRow=124
        };
        var row3=new EquipmentListReader.Row
        {
            Category="Máy kéo",Contract="05492026",Project="Công trình B",
            Company="Công ty A",Material="Máy kéo Montanary MGX80",
            Quantity=1,Serial="85893",Lot="Lô 01234",Sheet="DS máy kéo",SourceRow=125
        };
        var key=MotorHungYenStore.MachineKey(row1);
        Check(key!=MotorHungYenStore.MachineKey(row2) &&
              key!=MotorHungYenStore.MachineKey(row3),
              "Same contract remains separate by serial and project");
        var store=new MotorHungYenStore(dir);
        var before=store.List([row1,row2,row3]);
        Check(before.Count==3 && before.All(x=>x.State==MotorHungYenStore.Pending),
              "Excel rows never auto-mark sent, completed or handed over");
        var chosen=before[0];
        var d1=DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd");
        var d2=DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd");
        var d3=DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
        bool Rejected(Action action)
        {
            try{action();return false;}
            catch(InvalidOperationException){return true;}
        }
        Check(Rejected(()=>store.Advance(chosen,MotorHungYenStore.HandedOver,d1,"")) &&
              Rejected(()=>store.Advance(chosen,MotorHungYenStore.Completed,d1,"")),
            "Cannot skip sent-to-Hưng-Yên stage");
        store.Advance(chosen,MotorHungYenStore.Sent,d1,"Phiếu gửi máy kéo");
        var sent=store.List([row1,row2,row3]);
        Check(sent[0].State==MotorHungYenStore.Sent && sent[0].SentDay==d1 &&
              sent[1].State==MotorHungYenStore.Pending && sent[2].State==MotorHungYenStore.Pending,
            "Mark sent is per-machine and never marks sibling same-contract rows");
        Check(Rejected(()=>store.Advance(sent[0],MotorHungYenStore.HandedOver,d2,"")),
            "Cannot hand over before completion");
        Check(Rejected(()=>store.Advance(sent[0],MotorHungYenStore.Completed,d1==d2?d2:"1900-01-01","")),
            "Cannot backdate completion before Hưng Yên receipt");
        store.Advance(sent[0],MotorHungYenStore.Completed,d2,"Hưng Yên hoàn thành");
        var completed=store.List([row1,row2,row3]);
        Check(completed[0].CompletedDay==d2 &&
              completed[0].State==MotorHungYenStore.Completed,
            "Completion date and status are recorded separately");
        store.Advance(completed[0],MotorHungYenStore.HandedOver,d3,"Bàn giao có chứng từ");
        var reopened=new MotorHungYenStore(dir);
        var loaded=reopened.List([row1,row2,row3]);
        Check(loaded[0].State==MotorHungYenStore.HandedOver &&
              loaded[0].SentDay==d1 && loaded[0].CompletedDay==d2 &&
              loaded[0].HandoverDay==d3 &&
              reopened.HistoryFor(key).Count==3,
              "Dates, documents, transition order and audit trail survive app restart");
        Check(Rejected(()=>reopened.Advance(loaded[0],MotorHungYenStore.Sent,d3,"")),
            "Cannot silently reset handed-over machine to sent");
        var movedRow=new EquipmentListReader.Row
        {
            Category=row1.Category,Contract=row1.Contract,Project=row1.Project,
            Company=row1.Company,Material=row1.Material,Quantity=row1.Quantity,
            Serial=row1.Serial,Lot=row1.Lot,Sheet=row1.Sheet,SourceRow=986
        };
        var resequenced=reopened.List([row2,row3,movedRow]);
        Check(resequenced.Count==3 &&
            resequenced.Single(x=>x.Serial==row1.Serial&&x.Project==row1.Project).Key==key &&
            resequenced.Single(x=>x.Serial==row1.Serial&&x.Project==row1.Project)
                .State==MotorHungYenStore.HandedOver,
            "Sorting or inserting an Excel row preserves the same motor's status and entire audit history");
        var duplicateSerial=new EquipmentListReader.Row
        {
            Category=movedRow.Category,Contract=movedRow.Contract,Project=movedRow.Project,
            Company=movedRow.Company,Material=movedRow.Material,Quantity=movedRow.Quantity,
            Serial=movedRow.Serial,Lot=movedRow.Lot,Sheet=movedRow.Sheet,SourceRow=987
        };
        var ambiguous=reopened.List([row2,row3,movedRow,duplicateSerial]);
        Check(ambiguous.Count==5 && ambiguous.Count(x=>x.State==MotorHungYenStore.HandedOver)==1 &&
              ambiguous.Count(x=>x.Serial==row1.Serial&&x.PresentInSource)==2 &&
              ambiguous.Where(x=>x.Serial==row1.Serial&&x.PresentInSource)
                  .All(x=>x.State==MotorHungYenStore.Pending),
            "Duplicate physical serial in Excel does not give another row someone else's handed-over status");
        var old=reopened.List([row2,row3]);
        Check(old.Count==3 && old.Single(x=>x.Key==key).SourceStatus=="Không còn trong Excel",
              "If Excel row disappears, historical handover remains visible, not deleted");
        Check(Rejected(()=>reopened.Advance(old.Single(x=>x.Key==key),
            MotorHungYenStore.Completed,d3,"")),
            "Cannot mutate a historic row that no longer exists in source");
        var json=Path.Combine(dir,"motor_hung_yen_tracking.json");
        Check(File.Exists(json)&&File.Exists(json+".bak"),
            "Atomic JSON tracking includes a fallback backup after subsequent updates");
        var bytes=File.ReadAllText(json);
        File.WriteAllText(json,"{broken source");
        var corrupt=new MotorHungYenStore(dir);
        bool error=false;
        try{_ = corrupt.List([row1]);}catch(InvalidDataException){error=true;}
        Check(error && File.ReadAllText(json)=="{broken source",
            "Damaged status file is rejected, never overwritten or reset");
        File.WriteAllText(json,bytes);
        var restored=new MotorHungYenStore(dir);
        Check(restored.List([row1,row2,row3])[0].State==MotorHungYenStore.HandedOver,
            "Restored local history retains handed-over stage");
        return checks;
    }
}
