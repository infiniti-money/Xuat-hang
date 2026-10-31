using System.IO;

namespace Hisa.App;

/// <summary>
/// Standard offline warehouse layout:
/// Kho HISA/ Kho chính/{Tổng hợp,KT tổng hợp,Thành phẩm xuất kho}.xlsx,
///          CONT ĐI/,
///          Danh sách sản xuất/{TỦ ĐIỆN,MÁY KÉO,Đầu cửa car ...}.xlsx.
/// Never move user files; only resolve paths on the local filesystem.
/// </summary>
public static class HisaFolderLayout
{
    public sealed record Paths(string Root,string Cont,string Master,string Kt,
        string Product,string Cabinet,string Motor,string Door);

    private static string? ExcelNamed(string folder,string stem)
    {
        if(!Directory.Exists(folder))return null;
        var name=Rules.Code(stem);
        return Directory.EnumerateFiles(folder,"*.xlsx",SearchOption.TopDirectoryOnly)
            .Where(x=>!Path.GetFileName(x).StartsWith("~$") &&
                Rules.Code(Path.GetFileNameWithoutExtension(x))==name)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static string ProductionExcel(string folder,string prefix,string? preferred)
    {
        if(!Directory.Exists(folder))return "";
        var matches=Directory.EnumerateFiles(folder,"*.xlsx",SearchOption.TopDirectoryOnly)
            .Where(x=>!Path.GetFileName(x).StartsWith("~$") &&
                Rules.Code(Path.GetFileNameWithoutExtension(x)).StartsWith(prefix,StringComparison.Ordinal))
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        if(matches.Length==1)return matches[0];
        // Multiple monthly production lists must be selected deliberately;
        // guessing a month could show incomplete or wrong production records.
        if(matches.Length>1 && !string.IsNullOrWhiteSpace(preferred))
        {
            var absolute=Path.GetFullPath(preferred);
            if(matches.Any(x=>x.Equals(absolute,StringComparison.OrdinalIgnoreCase)))return absolute;
        }
        return "";
    }

    public static Paths? Detect(string selected,AppSettings? previous=null)
    {
        if(!Directory.Exists(selected))return null;
        var folder=Path.GetFullPath(selected);
        string root;
        if(Directory.Exists(Path.Combine(folder,"Kho chính")) &&
           Directory.Exists(Path.Combine(folder,"CONT ĐI")))
            root=folder;
        else if(new[]{"CONT DI","KHO CHINH","DANH SACH SAN XUAT"}
            .Select(Rules.Code).Contains(Rules.Code(Path.GetFileName(folder))) &&
            Directory.GetParent(folder) is { } parent &&
            Directory.Exists(Path.Combine(parent.FullName,"Kho chính")) &&
            Directory.Exists(Path.Combine(parent.FullName,"CONT ĐI")))
            root=parent.FullName;
        else return null;

        var main=Path.Combine(root,"Kho chính");
        var master=ExcelNamed(main,"Tổng hợp");
        if(master==null)throw new FileNotFoundException(
            "Đã tìm thấy cấu trúc Kho HISA nhưng thiếu Kho chính/Tổng hợp.xlsx.",Path.Combine(main,"Tổng hợp.xlsx"));
        var production=Path.Combine(root,"Danh sách sản xuất");
        return new Paths(root,Path.Combine(root,"CONT ĐI"),master,
            ExcelNamed(main,"KT tổng hợp")??Path.Combine(main,"KT tổng hợp.xlsx"),
            ExcelNamed(main,"Thành phẩm xuất kho")??Path.Combine(main,"Thành phẩm xuất kho.xlsx"),
            ProductionExcel(production,"TUDIEN",previous?.ElectricalCabinetListPath),
            ProductionExcel(production,"MAYKEO",previous?.TractionMachineListPath),
            ProductionExcel(production,"DAUCUACAR",previous?.CarDoorListPath));
    }

    public static string PreferredSelection(AppSettings settings)
    {
        var source=settings.DataSources.FirstOrDefault();
        if(!string.IsNullOrWhiteSpace(source) &&
           Detect(source,settings) is { } layout)
            return layout.Root;
        return source??"";
    }

    public static void Apply(AppSettings settings,Paths layout)
    {
        settings.DataSources=[layout.Cont];
        settings.MasterPath=layout.Master;
        settings.KtPath=layout.Kt;
        settings.ProductExportPath=layout.Product;
        // Production catalogues are optional; no silent overwrites of valid
        // custom paths unless an exact catalogue is found in the chosen layout.
        if(layout.Cabinet.Length>0)settings.ElectricalCabinetListPath=layout.Cabinet;
        if(layout.Motor.Length>0)settings.TractionMachineListPath=layout.Motor;
        if(layout.Door.Length>0)settings.CarDoorListPath=layout.Door;
    }
}
