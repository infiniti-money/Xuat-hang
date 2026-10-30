using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>File sorting regression fixtures are entirely isolated under the temporary test root.</summary>
public static class ContFileDiagnostics
{
    public static List<string> Run(string testWorkspace)
    {
        var messages=new List<string>();
        void Check(bool ok,string label) {if(!ok)throw new InvalidOperationException("FAIL: "+label);messages.Add(label);}
        var root=Path.Combine(testWorkspace,"cont-file-sorting-"+Guid.NewGuid().ToString("N"));
        var source=Path.Combine(root,"CONT ĐI");
        var numbered=Path.Combine(source,"26.10");var sentNumbered=Path.Combine(source,"27.10");
        Directory.CreateDirectory(numbered);Directory.CreateDirectory(sentNumbered);
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
            var ws=book.AddWorksheet("Kho");ws.Cell(1,1).Value="Kiện";ws.Cell(1,2).Value="Tên vật tư";ws.Cell(1,3).Value="ĐVT";ws.Cell(1,4).Value="Số lượng";
            ws.Cell(2,1).Value="HÀ NỘI";ws.Cell(2,2).Value="Tủ điện";ws.Cell(2,3).Value="Bộ";ws.Cell(2,4).Value=1;
            book.SaveAs(other);
        }
        File.WriteAllText(sent,"File hàng gửi nguyên bản - không đưa vào kho");
        var master=Path.Combine(root,"Tổng hợp.xlsx");
        using(var book=new XLWorkbook()) {book.AddWorksheet("Cont đi Hưng Yên");book.SaveAs(master);}
        var store=new ExcelStore{BackupDirectory=Path.Combine(root,"Backups","Excel")};
        var settings=new AppSettings{MasterPath=master,DataSources=[source]};
        var first=store.ScanSources(settings);
        Check(first.Packages.Count==1 && first.ImportedHungYenFiles.Count==1,
            $"Detect Hưng Yên workbook and keep non-Hưng Yên file unclassified (packages={first.Packages.Count}, importCandidates={first.ImportedHungYenFiles.Count}, warnings={string.Join(" | ",first.Warnings)})");
        Check(first.HangGuiFiles.Count==1 && first.HangGuiFiles[0].Source==sent,
            "Recognize hanggui file by filename without parsing it as stock");
        var priorId=first.Packages[0].Id;
        store.WriteContSummary(master,first.Packages);
        Check(store.ReadMaster(master).ContPackages.Count==1,"Read master successfully before archiving file");
        Check(ContFileOrganizer.MoveFiles(first.ImportedHungYenFiles,"Đã nhập kho").Count==0,
            "Move imported workbooks without errors");
        Check(ContFileOrganizer.MoveFiles(first.HangGuiFiles,"Hàng gửi").Count==0,
            "Move sent-goods workbooks without errors");
        var archived=Path.Combine(source,"Đã nhập kho","26.10","29102026.xlsx");
        var sentArchive=Path.Combine(source,"Hàng gửi","27.10","hanggui_001.xlsx");
        Check(File.Exists(archived)&&!File.Exists(hy)&&File.Exists(sentArchive)&&!File.Exists(sent),
            "Preserve exact dot-formatted date names 26.10 and 27.10 while sorting files");
        Check(Directory.Exists(numbered)&&Directory.Exists(sentNumbered),
            "Leave original CONT ĐI date directories 26.10 and 27.10 unchanged");
        Check(File.Exists(other),"Leave files without Hưng Yên packages at their original path");
        var second=store.ScanSources(settings);
        Check(second.Packages.Count==1 && second.Packages[0].Id==priorId && second.ImportedHungYenFiles.Count==0,
            "Archived workbooks remain readable with stable IDs and are not moved twice");
        store.WriteContSummary(master,second.Packages);
        Check(store.ReadMaster(master).ContPackages.Count==1,
            "A second sync never clears archived Hưng Yên packages");
        // Never overwrite an older same-named sent-goods file.
        File.WriteAllText(sent,"New sent-goods content");
        var third=store.ScanSources(settings);
        Check(ContFileOrganizer.MoveFiles(third.HangGuiFiles,"Hàng gửi").Count==0,
            "Archive name collision is handled without errors");
        Check(Directory.EnumerateFiles(Path.GetDirectoryName(sentArchive)!,"*.xlsx").Count()==2
            &&File.ReadAllText(sentArchive).Contains("nguyên bản"),
            "Existing archived file is preserved on name collision");
        var stale=Path.Combine(numbered,"31102026.xlsx");
        File.WriteAllText(stale,"Before");
        var staleCandidate=ContFileOrganizer.MakeCandidate(stale,[source])!;
        File.AppendAllText(stale,"After");
        Check(ContFileOrganizer.MoveFiles([staleCandidate],"Đã nhập kho").Count==1 && File.Exists(stale),
            "Changed source is not moved after classification snapshot");
        return messages;
    }
}
