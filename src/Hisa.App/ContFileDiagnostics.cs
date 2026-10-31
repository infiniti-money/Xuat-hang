using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>All archive and receipt regression fixtures use temporary local Excel files.</summary>
public static class ContFileDiagnostics
{
    public static List<string> Run(string testWorkspace)
    {
        var messages=new List<string>();
        void Check(bool ok,string label)
        {
            if(!ok)throw new InvalidOperationException("FAIL: "+label);
            messages.Add(label);
        }
        var root=Path.Combine(testWorkspace,"cont-sibling-sorting-"+Guid.NewGuid().ToString("N"));
        var source=Path.Combine(root,"CONT ĐI");
        var numbered=Path.Combine(source,"26.10");
        var sentNumbered=Path.Combine(source,"27.10");
        Directory.CreateDirectory(numbered);
        Directory.CreateDirectory(sentNumbered);
        var hy=Path.Combine(numbered,"29102026.xlsx");
        var sent=Path.Combine(sentNumbered,"hanggui_001.xlsx");
        var other=Path.Combine(source,"Hanoi.xlsx");
        using(var book=new XLWorkbook())
        {
            var ws=book.AddWorksheet("Kho");
            foreach(var (column,value) in new[]{(1,"Kiện"),(2,"Tên vật tư"),(3,"ĐVT"),(4,"Số lượng")})
                ws.Cell(1,column).Value=value;
            foreach(var (row,marker,material) in new[]{(2,"HƯNG YÊN","Máy kéo M1"),(3,"","Ray dẫn hướng")})
            {
                ws.Cell(row,1).Value=marker;ws.Cell(row,2).Value=material;
                ws.Cell(row,3).Value="Bộ";ws.Cell(row,4).Value=2;
            }
            book.SaveAs(hy);
        }
        using(var book=new XLWorkbook())
        {
            var ws=book.AddWorksheet("Kho");
            ws.Cell(1,1).Value="Kiện";ws.Cell(1,2).Value="Tên vật tư";
            ws.Cell(1,3).Value="ĐVT";ws.Cell(1,4).Value="Số lượng";
            ws.Cell(2,1).Value="HÀ NỘI";ws.Cell(2,2).Value="Tủ điện";
            ws.Cell(2,3).Value="Bộ";ws.Cell(2,4).Value=1;
            book.SaveAs(other);
        }
        File.WriteAllText(sent,"File hàng gửi nguyên bản - không đưa vào kho");
        var master=Path.Combine(root,"Tổng hợp.xlsx");
        using(var book=new XLWorkbook()){book.AddWorksheet("Cont đi Hưng Yên");book.SaveAs(master);}
        var store=new ExcelStore{BackupDirectory=Path.Combine(root,"Backups","Excel")};
        var settings=new AppSettings{MasterPath=master,DataSources=[source]};

        var first=store.ScanSources(settings);
        Check(first.Packages.Count==1 && first.ImportedHungYenFiles.Count==1,
            "Pending Hưng Yên workbook is found without assuming physical arrival");
        Check(first.HangGuiFiles.Count==1 && first.HangGuiFiles[0].Source==sent,
            "Hàng gửi identified by name and excluded from receipt");
        var sourceId=first.Packages[0].Id;
        Check(ContFileOrganizer.ConfirmedCandidates(first.ImportedHungYenFiles,first.Packages,
                  new WorkbookSnapshot()).Count==0 && File.Exists(hy),
            "Unreceived source stays in CONT ĐI despite a successful source scan");
        store.WriteContSummary(master,first.Packages);
        Check(store.ReadMaster(master).ContPackages.Count==1 &&
              store.ReadMaster(master).DetailPackages.Count==0,
            "Source sync writes Cont đi, never a received record");

        Check(ContFileOrganizer.MoveFiles(first.HangGuiFiles,"Hàng gửi").Count==0,
            "Hàng gửi moved into a directory SIBLING of CONT ĐI");
        var sentArchive=Path.Combine(root,"Hàng gửi","27.10","hanggui_001.xlsx");
        Check(File.Exists(sentArchive)&&!File.Exists(sent) &&
              !Directory.Exists(Path.Combine(source,"Hàng gửi")),
            "No Hàng gửi directory is left nested in CONT ĐI");

        var partFile=Path.Combine(numbered,"partial.xlsx");
        var part=new ContFileOrganizer.Candidate(partFile,source,123,42);
        var physical=new[]{
            new Package{Id="pkg-a",SourcePath=partFile},
            new Package{Id="pkg-b",SourcePath=partFile}
        };
        var partialMaster=new WorkbookSnapshot
        {
            ContPackages=[
                new Package{Id="cont-A",Contract="112233",Project="Thang A",Company="HISA"},
                new Package{Id="cont-B",Contract="112233",Project="Thang A",Company="HISA"}
            ],
            DetailPackages=[new Package{OriginPackageId="cont-A"}]
        };
        physical[0].Contract="112233";physical[0].Project="Thang A";physical[0].Company="HISA";
        physical[1].Contract="112233";physical[1].Project="Thang A";physical[1].Company="HISA";
        Check(ContFileOrganizer.ConfirmedCandidates([part],physical,partialMaster).Count==0,
            "One of two identical physical packages received must NOT archive the entire source file");
        partialMaster.DetailPackages.Add(new Package{OriginPackageId="cont-B"});
        Check(ContFileOrganizer.ConfirmedCandidates([part],physical,partialMaster).Count==1,
            "Entire source file becomes eligible only after both physical receipts");

        // Master IDs differ from path-based source IDs. Receipt matching must
        // use strict unique physical signatures rather than equal IDs.
        var masterSnapshot=store.ReadMaster(master);
        masterSnapshot.DetailPackages.Add(new Package{
            OriginPackageId=masterSnapshot.ContPackages.Single().Id
        });
        Check(masterSnapshot.ContPackages.Single().Id!=sourceId,
            "Master Cont đi package ID is NOT the original source-file ID");
        var confirmed=ContFileOrganizer.ConfirmedCandidates(first.ImportedHungYenFiles,
            first.Packages,masterSnapshot);
        Check(confirmed.Count==1 && ContFileOrganizer.MoveFiles(confirmed,"Đã nhập kho").Count==0,
            "Only the simulated confirmed source becomes eligible to leave CONT ĐI");
        var archived=Path.Combine(root,"Đã nhập kho","26.10","29102026.xlsx");
        Check(File.Exists(archived)&&!File.Exists(hy) &&
              !Directory.Exists(Path.Combine(source,"Đã nhập kho")),
            "Đã nhập kho is a SIBLING of CONT ĐI and preserves the date folder");
        Check(ContFileOrganizer.OriginalPath(archived,[source])==hy,
            "Moving to sibling folder preserves original physical package identity");

        var second=store.ScanSources(settings);
        Check(second.Packages.Count==1 && second.Packages[0].Id==sourceId &&
              second.ImportedHungYenFiles.Count==0,
            "Siblings are included in sync; source IDs remain stable and never re-archive");
        store.WriteContSummary(master,second.Packages);
        Check(store.ReadMaster(master).ContPackages.Count==1,
            "Rescanning sibling received folder does not empty old master source entries");
        Check(File.Exists(other),"Non-Hưng Yên file remains untouched");

        // Older builds incorrectly moved not-yet-received files into
        // CONT ĐI/Đã nhập kho. They must return to active CONT ĐI, never
        // be relabeled as physically received without a persisted receipt.
        var legacyArchive=Path.Combine(source,"Đã nhập kho","28.10","29102026.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyArchive)!);
        File.Copy(archived,legacyArchive);
        var legacySent=Path.Combine(source,"Hàng gửi","28.10","hanggui_old.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(legacySent)!);
        File.WriteAllText(legacySent,"old sent workbook");
        var old=store.ScanSources(settings);
        Check(old.Packages.Any(p=>p.SourcePath==legacyArchive),
            "Legacy nested archive is scanned before safe migration");
        var migrated=ContFileOrganizer.RelocateLegacyFolders([source],old.Packages,masterSnapshot);
        Check(migrated.Count==0 && !File.Exists(legacyArchive) &&
              File.Exists(Path.Combine(source,"28.10","29102026.xlsx")),
            "Unconfirmed legacy workbook returns to pending CONT ĐI");
        Check(!File.Exists(legacySent) &&
              File.Exists(Path.Combine(root,"Hàng gửi","28.10","hanggui_old.xlsx")) &&
              !Directory.Exists(Path.Combine(source,"Hàng gửi")) &&
              !Directory.Exists(Path.Combine(source,"Đã nhập kho")),
            "Both legacy Hàng gửi and Đã nhập kho nested directories are removed after safe moves");

        File.WriteAllText(sent,"New sent content");
        var third=store.ScanSources(settings);
        Check(ContFileOrganizer.MoveFiles(third.HangGuiFiles,"Hàng gửi").Count==1 &&
              File.Exists(sent)&&File.ReadAllText(sentArchive).Contains("nguyên bản"),
            "Name collisions never overwrite archived sent-goods files");
        var stale=Path.Combine(numbered,"31102026.xlsx");
        File.WriteAllText(stale,"Before");
        var staleCandidate=ContFileOrganizer.MakeCandidate(stale,[source])!;
        File.AppendAllText(stale,"After");
        Check(ContFileOrganizer.MoveFiles([staleCandidate],"Đã nhập kho").Count==1 &&
              File.Exists(stale),
            "Source changed since scan is never moved");
        return messages;
    }
}
