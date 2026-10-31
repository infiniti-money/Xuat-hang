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

    /// <summary>
    /// Single-folder setup: require the user-selected ROOT and all six real
    /// Excel workbooks. Never fall back to an older, unrelated workbook path.
    /// Does not move/copy/modify any user data.
    /// </summary>
    public static Paths RequireCompleteRoot(string selected,AppSettings? previous=null)
    {
        if(string.IsNullOrWhiteSpace(selected)||!Directory.Exists(selected))
            throw new DirectoryNotFoundException("Chọn đúng thư mục gốc Kho HISA đang tồn tại.");
        var full=Path.GetFullPath(selected);
        var layout=Detect(full,previous);
        if(layout==null ||
           !string.Equals(full,layout.Root,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Chỉ chọn duy nhất thư mục gốc Kho HISA, không chọn Kho chính, CONT ĐI hoặc Danh sách sản xuất.");
        if(!Directory.Exists(Path.Combine(layout.Root,"Danh sách sản xuất")))
            throw new DirectoryNotFoundException("Thiếu thư mục Kho HISA/Danh sách sản xuất.");
        var expected=new[]{
            // KT is optional; the KT comparison workflow has been removed.
            ("Kho chính/Thành phẩm xuất kho.xlsx",layout.Product),
            ("Danh sách sản xuất/TỦ ĐIỆN.xlsx",layout.Cabinet),
            ("Danh sách sản xuất/MÁY KÉO.xlsx",layout.Motor),
            ("Danh sách sản xuất/Đầu cửa car T10.2026.xlsx",layout.Door)
        };
        var missing=expected.Where(x=>string.IsNullOrWhiteSpace(x.Item2)||!File.Exists(x.Item2))
            .Select(x=>x.Item1).ToList();
        if(missing.Count>0)
            throw new FileNotFoundException("Kho HISA chưa nhận diện đủ các file Excel: "+
                string.Join(", ",missing)+
                ". Chỉ cần đặt đúng tên và vị trí file trong thư mục, không chọn từng file.");
        // Validate that no two catalogue names resolved to one file or to
        // Excel canonical data, without changing the selected settings.
        var probe=new AppSettings{
            MasterPath=layout.Master,KtPath=layout.Kt,
            ProductExportPath=layout.Product,DataSources=[layout.Cont],
            ElectricalCabinetListPath=layout.Cabinet,
            TractionMachineListPath=layout.Motor,CarDoorListPath=layout.Door
        };
        EquipmentListReader.ValidateIndependentSources(probe);
        return layout;
    }

    public static string PreferredSelection(AppSettings settings)
    {
        var source=settings.DataSources.FirstOrDefault();
        if(string.IsNullOrWhiteSpace(source))return "";
        try
        {
            if(Detect(source,settings) is { } layout)
                return layout.Root;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            // The original path should remain editable even when a workbook
            // has temporarily disappeared. Configuration must never crash.
        }
        return source;
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
