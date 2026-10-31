using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Temporary fixture only; never touches real Excel sources.</summary>
public static class HisaFolderLayoutDiagnostics
{
    public static List<string> Run(string workspace)
    {
        var results=new List<string>();
        void Check(bool ok,string name)
        {
            if(!ok)throw new InvalidOperationException("Standard Kho HISA layout failed: "+name);
            results.Add("Kho HISA folders: "+name);
        }
        var root=Path.Combine(workspace,"standard-kho-"+Guid.NewGuid().ToString("N"),"Kho HISA");
        var main=Path.Combine(root,"Kho chính");
        var cont=Path.Combine(root,"Container");
        var production=Path.Combine(root,"Danh sách sản xuất");
        Directory.CreateDirectory(main);
        Directory.CreateDirectory(cont);
        Directory.CreateDirectory(production);
        string MakeExcel(string folder,string name)
        {
            var path=Path.Combine(folder,name);
            using var book=new XLWorkbook();
            book.AddWorksheet("Dữ liệu");
            book.SaveAs(path);
            return path;
        }
        var master=MakeExcel(main,"Tổng hợp.xlsx");
        var kt=MakeExcel(main,"KT tổng hợp.xlsx");
        var product=MakeExcel(main,"Thành phẩm xuất kho.xlsx");
        var cabinet=MakeExcel(production,"TỦ ĐIỆN.xlsx");
        var motor=MakeExcel(production,"MÁY KÉO.xlsx");
        var door=MakeExcel(production,"Đầu cửa car T10.2026.xlsx");
        foreach(var selected in new[]{root,main,cont,production})
        {
            var found=HisaFolderLayout.Detect(selected);
            Check(found!=null && found.Root==root && found.Cont==cont &&
                found.Master==master && found.Kt==kt && found.Product==product &&
                found.Cabinet==cabinet && found.Motor==motor && found.Door==door,
                "Selecting "+Path.GetFileName(selected)+" resolves correct 3+3 files and Container");
            var settings=new AppSettings();
            HisaFolderLayout.Apply(settings,found!);
            EquipmentListReader.ValidateIndependentSources(settings);
            Check(settings.DataSources.Count==1 && settings.DataSources[0]==cont &&
                  settings.MasterPath==master && settings.KtPath==kt &&
                  settings.ProductExportPath==product,
                "All three canonical Excel files are in Kho chính, while only Container is scanned");
            Check(EquipmentListReader.ConfiguredPaths(settings).Count()==3,
                "Three production catalogue files resolve from Danh sách sản xuất");
            var scan=new ExcelStore().ScanSources(settings);
            Check(scan.Packages.Count==0 && scan.FileCount==0,
                "No production or main Excel workbook gets imported as a container");
            Check(HisaFolderLayout.PreferredSelection(settings)==root,
                "Reopening source settings displays Kho HISA root, not an internal Container path");
        }
        var single=HisaFolderLayout.RequireCompleteRoot(root);
        Check(single.Master==master && single.Kt==kt && single.Product==product &&
              single.Cabinet==cabinet && single.Motor==motor && single.Door==door &&
              single.Cont==cont,
            "One selected root resolves all six Excel sources, no individual file pickers");
        foreach(var wrong in new[]{main,cont,production})
        {
            bool rejected=false;
            try{HisaFolderLayout.RequireCompleteRoot(wrong);}
            catch(InvalidOperationException){rejected=true;}
            Check(rejected,"One-folder setup rejects nested "+Path.GetFileName(wrong)+"; require Kho HISA root");
        }
        var original=new AppSettings
        {
            MasterPath=master,KtPath=kt,ProductExportPath=product,
            DataSources=[cont],ElectricalCabinetListPath=cabinet,
            TractionMachineListPath=motor,CarDoorListPath=door
        };
        var months=MakeExcel(production,"Đầu cửa car T09.2026.xlsx");
        bool missingAmbiguousMonth=false;
        try{HisaFolderLayout.RequireCompleteRoot(root,new AppSettings());}
        catch(FileNotFoundException){missingAmbiguousMonth=true;}
        Check(missingAmbiguousMonth,
            "Multiple car door months never get silently assigned when no month was previously selected");
        var ambiguous=HisaFolderLayout.Detect(root,new AppSettings());
        Check(ambiguous!=null && ambiguous.Door.Length==0,
            "Two monthly car-door workbooks are not silently guessed");
        var chosen=HisaFolderLayout.Detect(root,original)!;
        Check(chosen.Door==door && File.Exists(months),
            "A previously selected car-door month remains selected when multiple books exist");
        Check(HisaFolderLayout.RequireCompleteRoot(root,original).Door==door,
            "One folder selection reuses correctly identified existing car door month");
        var copied=new AppSettings
        {
            ElectricalCabinetListPath=cabinet,TractionMachineListPath=motor,CarDoorListPath=door
        };
        HisaFolderLayout.Apply(copied,chosen);
        Check(copied.ElectricalCabinetListPath==cabinet &&
              copied.TractionMachineListPath==motor &&
              copied.CarDoorListPath==door,
            "Layout selection never clears valid existing production catalogue sources");
        var bytes=new[]{master,kt,product,cabinet,motor,door,months}
            .Select(p=>new FileInfo(p).Length).ToArray();
        HisaFolderLayout.Detect(root,original);
        Check(new[]{master,kt,product,cabinet,motor,door,months}
                .Select(p=>new FileInfo(p).Length).SequenceEqual(bytes),
            "Recognizing folders neither moves nor modifies Excel files");
        var otherRoot=Path.Combine(workspace,"standard-kho-new-"+Guid.NewGuid().ToString("N"),"Kho HISA");
        var otherMain=Path.Combine(otherRoot,"Kho chính");
        var otherCont=Path.Combine(otherRoot,"Container");
        var otherProduction=Path.Combine(otherRoot,"Danh sách sản xuất");
        Directory.CreateDirectory(otherMain);
        Directory.CreateDirectory(otherCont);
        Directory.CreateDirectory(otherProduction);
        MakeExcel(otherMain,"Tổng hợp.xlsx");
        MakeExcel(otherMain,"KT tổng hợp.xlsx");
        MakeExcel(otherMain,"Thành phẩm xuất kho.xlsx");
        MakeExcel(otherProduction,"TỦ ĐIỆN.xlsx");
        MakeExcel(otherProduction,"MÁY KÉO.xlsx");
        bool missingEquipment=false;
        try{HisaFolderLayout.RequireCompleteRoot(otherRoot,original);}
        catch(FileNotFoundException){missingEquipment=true;}
        Check(missingEquipment,
            "Different warehouse cannot silently reuse the old car-door workbook from another root");
        MakeExcel(otherProduction,"Đầu cửa car T10.2026.xlsx");
        var another=HisaFolderLayout.RequireCompleteRoot(otherRoot,original);
        Check(another.Root==otherRoot && another.Door.StartsWith(otherRoot,StringComparison.OrdinalIgnoreCase) &&
              another.Cabinet.StartsWith(otherRoot,StringComparison.OrdinalIgnoreCase) &&
              another.Motor.StartsWith(otherRoot,StringComparison.OrdinalIgnoreCase),
            "Switching one root relocates all six paths to the new warehouse without stale files");
        // The physical Container folder is optional when the workflow uses
        // the explicit Container search, not a cached master worksheet.
        Directory.Delete(otherCont);
        var noContFolder=HisaFolderLayout.RequireCompleteRoot(otherRoot,original);
        var noContSettings=new AppSettings();
        HisaFolderLayout.Apply(noContSettings,noContFolder);
        Check(noContFolder.Master==Path.Combine(otherMain,"Tổng hợp.xlsx") &&
              noContSettings.DataSources.Count==0,
            "Valid Kho HISA root without Container folder keeps master Excel available and disables folder scanning");
        Check(HisaFolderLayout.PreferredSelection(noContSettings)==otherRoot,
            "Settings can reopen the warehouse root without a physical Container folder");
        var temporarilyMissing=master+".unavailable";
        File.Move(master,temporarilyMissing);
        try
        {
            Check(HisaFolderLayout.PreferredSelection(original)==cont,
                "Missing master workbook does not crash settings: original Container path stays editable");
        }
        finally{File.Move(temporarilyMissing,master);}
        return results;
    }
}
