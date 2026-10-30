using System.IO;

namespace Hisa.App;

public sealed partial class WarehouseService
{
    private void EnsureReportDestination(string path)
    {
        var target=Path.GetFullPath(path);
        if(!Path.GetExtension(target).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Báo cáo Excel cần dùng tệp .xlsx.");
        var protectedFiles=new[]{Settings.MasterPath,Settings.KtPath,Settings.ProductExportPath,Path.Combine(Path.GetDirectoryName(Settings.MasterPath)??Workspace,"XUẤT HÀNG","Sổ theo dõi xuất hàng.xlsx")}
            .Concat(Settings.DataSources.Where(File.Exists)).Where(x=>!string.IsNullOrWhiteSpace(x));
        if(protectedFiles.Any(file=>Path.GetFullPath(file).Equals(target,StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Đây là tệp dữ liệu nguồn hoặc sổ theo dõi của ứng dụng. Chọn tên báo cáo khác để giữ nguyên dữ liệu.");
    }
}
