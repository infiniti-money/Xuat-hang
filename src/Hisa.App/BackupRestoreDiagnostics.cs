using System.IO;
using System.IO.Compression;

namespace Hisa.App;

public static class BackupRestoreDiagnostics
{
    public static List<string> Run(string zip,string workspace,AppSettings sourceSettings)
    {
        var checks=new List<string>();
        void Check(bool condition,string label)
        {
            if(!condition)throw new InvalidOperationException("Restore regression: "+label);
            checks.Add("Restore: "+label);
        }

        var preview=BackupRestore.Inspect(zip);
        Check(!preview.Legacy && preview.DataCount>1 && preview.ExcelCount==3,
            "V2 manifest preview covers JSON, AI and three Excel files");
        var root=Path.Combine(workspace,"restore-isolation");
        var data=Path.Combine(root,"data");
        var backups=Path.Combine(root,"backups");
        var excel=Path.Combine(root,"Excel");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(excel);
        var settings=new AppSettings
        {
            MasterPath=Path.Combine(excel,"Tổng hợp.xlsx"),
            ProductExportPath=Path.Combine(excel,"Thành phẩm xuất kho.xlsx"),
            KtPath=Path.Combine(excel,"KT tổng hợp.xlsx")
        };
        foreach(var (from,to) in new[]
        {
            (sourceSettings.MasterPath,settings.MasterPath),
            (sourceSettings.ProductExportPath,settings.ProductExportPath),
            (sourceSettings.KtPath,settings.KtPath)
        }) File.Copy(from,to);
        File.WriteAllText(Path.Combine(data,"slips.json"),"[]");
        var excelBefore=File.ReadAllBytes(settings.MasterPath);

        var safety=BackupRestore.Restore(zip,data,backups,settings,true,false);
        Check(File.Exists(Path.Combine(data,"slips.json")) &&
            Directory.Exists(Path.Combine(data,"ai_schedules")) &&
            File.ReadAllBytes(settings.MasterPath).SequenceEqual(excelBefore) &&
            File.Exists(Path.Combine(safety,"restore-map.json")),
            "App-only restore preserves Excel and leaves a recoverable safety snapshot");

        // An injected error after writing one file must restore every previous destination.
        var stateBefore=Directory.EnumerateFiles(data,"*.json",SearchOption.TopDirectoryOnly)
            .ToDictionary(Path.GetFileName,x=>File.ReadAllBytes(x),StringComparer.OrdinalIgnoreCase);
        bool injected=false;
        try {BackupRestore.Restore(zip,data,backups,settings,true,false,1);}
        catch(IOException){injected=true;}
        Check(injected && stateBefore.All(p=>File.ReadAllBytes(Path.Combine(data,p.Key!)).SequenceEqual(p.Value)),
            "Mid-restore failure rolls back changed files");
        File.WriteAllText(settings.MasterPath,"local workbook to replace");
        var appStateBefore=File.ReadAllBytes(Path.Combine(data,"slips.json"));
        BackupRestore.Restore(zip,data,backups,settings,false,true);
        Check(File.ReadAllBytes(settings.MasterPath).SequenceEqual(excelBefore) &&
            File.ReadAllBytes(Path.Combine(data,"slips.json")).SequenceEqual(appStateBefore),
            "Excel-only restore does not alter application JSON");

        bool locked=false;
        using(var lockFile=new FileStream(settings.MasterPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            try {BackupRestore.Restore(zip,data,backups,settings,false,true);}
            catch(IOException){locked=true;}
        }
        Check(locked,"Locked workbook blocks restore before any overwrite");

        var bad=Path.Combine(root,"zip-slip.zip");
        using(var archive=ZipFile.Open(bad,ZipArchiveMode.Create))
        {
            using(var w=new StreamWriter(archive.CreateEntry("../escape.json").Open()))w.Write("{}");
            archive.CreateEntryFromFile(sourceSettings.MasterPath,"Excel/Tổng hợp.xlsx");
        }
        bool denied=false;
        try{BackupRestore.Inspect(bad);}
        catch(InvalidDataException){denied=true;}
        Check(denied && !File.Exists(Path.Combine(root,"escape.json")),
            "ZIP path traversal rejected without extraction");

        var old=Path.Combine(root,"legacy-v1.zip");
        using(var archive=ZipFile.Open(old,ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(sourceSettings.MasterPath,"Excel/Tổng hợp.xlsx");
            using(var w=new StreamWriter(archive.CreateEntry("slips.json").Open()))w.Write("[]");
        }
        Check(BackupRestore.Inspect(old).Legacy,"Historical V1 ZIP is recognized and explicitly marked unverified");
        var legacyData=Path.Combine(root,"legacy-data");
        BackupRestore.Restore(old,legacyData,backups,settings,true,false);
        Check(File.ReadAllText(Path.Combine(legacyData,"slips.json"))=="[]",
            "V1 selected JSON restores without overwriting workbooks");

        // Valid JSON with the wrong model type must not overwrite saved slips.
        var wrong=Path.Combine(root,"wrong-slips.zip");
        using(var archive=ZipFile.Open(wrong,ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(sourceSettings.MasterPath,"Excel/Tổng hợp.xlsx");
            using(var writer=new StreamWriter(archive.CreateEntry("slips.json").Open()))
                writer.Write("{\"unexpected\":true}");
        }
        var preserved=File.ReadAllBytes(Path.Combine(data,"slips.json"));
        denied=false;
        try{BackupRestore.Restore(wrong,data,backups,settings,true,false);}
        catch(IOException){denied=true;}
        Check(denied && preserved.SequenceEqual(File.ReadAllBytes(Path.Combine(data,"slips.json"))),
            "Wrong JSON schema cannot overwrite valid slip history");

        var nullItems=Path.Combine(root,"null-slips.zip");
        using(var archive=ZipFile.Open(nullItems,ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(sourceSettings.MasterPath,"Excel/Tổng hợp.xlsx");
            using(var writer=new StreamWriter(archive.CreateEntry("slips.json").Open()))
                writer.Write("[null]");
        }
        denied=false;
        try{BackupRestore.Restore(nullItems,data,backups,settings,true,false);}
        catch(IOException){denied=true;}
        Check(denied && preserved.SequenceEqual(File.ReadAllBytes(Path.Combine(data,"slips.json"))),
            "Null slip items rejected before any data replacement");

        var lowerCaseZip=Path.Combine(root,"excel-lowercase.zip");
        using(var archive=ZipFile.Open(lowerCaseZip,ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(sourceSettings.MasterPath,"excel/tổng hợp.xlsx");
            using(var writer=new StreamWriter(archive.CreateEntry("slips.json").Open()))
                writer.Write("[]");
        }
        Check(BackupRestore.Inspect(lowerCaseZip).ExcelCount==1,
            "Upper/lower-case Excel directory is recognized");
        File.WriteAllText(settings.MasterPath,"simulated local workbook");
        BackupRestore.Restore(lowerCaseZip,data,backups,settings,false,true);
        Check(File.ReadAllBytes(settings.MasterPath).SequenceEqual(excelBefore),
            "Legacy Excel directory casing restores correct workbook only");

        var missing=Path.Combine(root,"missing-master.zip");
        using(var archive=ZipFile.Open(missing,ZipArchiveMode.Create))
            using(var w=new StreamWriter(archive.CreateEntry("slips.json").Open()))w.Write("[]");
        denied=false;
        try{BackupRestore.Inspect(missing);}
        catch(InvalidDataException){denied=true;}
        Check(denied,"Invalid ZIP without mandatory master workbook is blocked");
        return checks;
    }
}
