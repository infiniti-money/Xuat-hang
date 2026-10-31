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
        var cont=Path.Combine(root,"CONT ĐI");
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
                "Selecting "+Path.GetFileName(selected)+" resolves correct 3+3 files and CONT ĐI");
            var settings=new AppSettings();
            HisaFolderLayout.Apply(settings,found!);
            EquipmentListReader.ValidateIndependentSources(settings);
            Check(settings.DataSources.Count==1 && settings.DataSources[0]==cont &&
                  settings.MasterPath==master && settings.KtPath==kt &&
                  settings.ProductExportPath==product,
                "All three canonical Excel files are in Kho chính, while only CONT ĐI is scanned");
            Check(EquipmentListReader.ConfiguredPaths(settings).Count()==3,
                "Three production catalogue files resolve from Danh sách sản xuất");
            var scan=new ExcelStore().ScanSources(settings);
            Check(scan.Packages.Count==0 && scan.FileCount==0,
                "No production or main Excel workbook gets imported as a container");
            Check(HisaFolderLayout.PreferredSelection(settings)==root,
                "Reopening source settings displays Kho HISA root, not an internal CONT ĐI path");
        }
        var original=new AppSettings
        {
            MasterPath=master,KtPath=kt,ProductExportPath=product,
            DataSources=[cont],ElectricalCabinetListPath=cabinet,
            TractionMachineListPath=motor,CarDoorListPath=door
        };
        var months=MakeExcel(production,"Đầu cửa car T09.2026.xlsx");
        var ambiguous=HisaFolderLayout.Detect(root,new AppSettings());
        Check(ambiguous!=null && ambiguous.Door.Length==0,
            "Two monthly car-door workbooks are not silently guessed");
        var chosen=HisaFolderLayout.Detect(root,original)!;
        Check(chosen.Door==door && File.Exists(months),
            "A previously selected car-door month remains selected when multiple books exist");
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
        return results;
    }
}
