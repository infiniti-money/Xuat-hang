using System.Globalization;
using System.IO;
using System.Text;

namespace Hisa.App;

/// <summary>
/// Cross-file warehouse reconciliation: full local source readers with a
/// contract-scoped, bounded Gemini projection. No arbitrary directory access,
/// no raw Excel upload, no serial invented by the model, no workbook writes.
/// </summary>
public static class MayWarehouseAudit
{
    public sealed class MotorStatus
    {
        public string Contract { get; set; }="";
        public string Project { get; set; }="";
        public string Company { get; set; }="";
        public string Serial { get; set; }="";
        public string Material { get; set; }="";
        public string WorkStatus { get; set; }="";
        public string PlannedWork { get; set; }="";
        public string DispatchDate { get; set; }="";
        public string HandoverDate { get; set; }="";
        public string Verification { get; set; }="";
        public string ExcelRow { get; set; }="";
    }

    public static List<MotorStatus> MotorsFor(AiScheduleLine line,
        IEnumerable<MotorHungYenStore.Item> machines)
    {
        var code=Rules.Contract(line.Contract);
        var identity=Rules.Identity(line.Contract,line.Project,line.Company);
        return machines.Where(x=>Rules.Contract(x.Contract)==code)
            .Select(x=>new MotorStatus
            {
                Contract=x.Contract,Project=x.Project,Company=x.Company,
                Serial=x.Serial,Material=x.Material,WorkStatus=x.WorkLabel,
                PlannedWork=line.HasExportSchedule==true?"Đã có lịch xuất từ trước":
                    line.HasExportSchedule==null?"Mới phát sinh":"Chưa có lịch xuất trước đó",
                DispatchDate=x.ExcelDispatchDay,
                HandoverDate=x.HandoverDay,
                Verification=Rules.Identity(x.Contract,x.Project,x.Company)==identity &&
                    !string.IsNullOrWhiteSpace(x.Serial)
                    ? "Khớp HĐ, công trình, đơn vị và có serial"
                    : "CẦN KIỂM TRA: chưa khớp công trình/đơn vị hoặc thiếu serial",
                ExcelRow=x.SourcePosition
            }).OrderBy(x=>x.Contract).ThenBy(x=>x.Serial).ToList();
    }

    private static string Clean(string? s,int max=110)
    {
        var val=(s??"").Replace('\r',' ').Replace('\n',' ').Trim();
        return val.Length>max?val[..max]+"…":val;
    }

    public static string BuildReadOnlyProjection(string question,AppSettings settings,
        WorkbookSnapshot snapshot,EquipmentListReader.Catalog equipment,
        IEnumerable<MotorHungYenStore.Item> machines)
    {
        var sources=new (string Kind,string Path)[]
        {
            ("Tổng hợp.xlsx",settings.MasterPath),
            ("Xuất hàng",settings.ProductExportPath),
            ("KT",settings.KtPath),
            ("MÁY KÉO.xlsx",settings.TractionMachineListPath),
            ("Tủ điện",settings.ElectricalCabinetListPath),
            ("Đầu cửa car",settings.CarDoorListPath)
        };
        var sb=new StringBuilder();
        sb.AppendLine("KIỂM TRA TOÀN BỘ NGUỒN HISA ĐƯỢC CẤU HÌNH (đọc tại máy, KHÔNG gửi file gốc):");
        foreach(var s in sources)
        {
            var ok=!string.IsNullOrWhiteSpace(s.Path)&&File.Exists(s.Path);
            sb.AppendLine(s.Kind+": "+(ok?"đã tìm thấy":"chưa cấu hình hoặc thiếu")+
                (ok?" · "+Clean(Path.GetFileName(s.Path),65):""));
        }
        sb.AppendLine("Đã đọc dữ liệu kho trong bộ nhớ: "+
            (snapshot.Packages?.Count??0)+" kiện; "+
            (snapshot.DetailPackages?.Count??0)+" kiện chi tiết; "+
            (snapshot.ContPackages?.Count??0)+" kiện cont; "+
            equipment.Rows.Count+" dòng danh sách sản xuất; "+
            machines.Count()+" dòng máy kéo (có cả trạng thái K và lịch sử HISA).");
        foreach(var warning in equipment.Warnings.Take(5))
            sb.AppendLine("CẢNH BÁO NGUỒN THIẾT BỊ: "+Clean(warning,150));
        var requested=GeminiChatService.ContractNumbers(question);
        if(requested.Count==0)
        {
            sb.AppendLine("Muốn xem chi tiết máy kéo/serial, hãy hỏi một số hợp đồng cụ thể.");
            return sb.ToString();
        }
        foreach(var code in requested.Take(12))
        {
            sb.AppendLine("ĐỐI CHIẾU HĐ "+Clean(code,40)+":");
            var matched=machines.Where(x=>Rules.Contract(x.Contract)==code).Take(25).ToList();
            if(matched.Count==0)
                sb.AppendLine("Máy kéo: chưa tìm thấy, không suy ra không có hàng.");
            foreach(var x in matched)
                sb.AppendLine("Máy kéo: công trình "+Clean(x.Project,55)+
                    " · đơn vị "+Clean(x.Company,42)+
                    " · tên "+Clean(x.Material,65)+
                    " · serial "+Clean(x.Serial,45)+
                    " · tiến độ="+Clean(x.WorkLabel,30)+
                    " · trạng thái HISA="+Clean(x.StatusLabel,45)+
                    " · ngày xuất thực tế cột K="+Clean(x.ExcelDispatchDay,15)+
                    " · ngày bàn giao HISA="+Clean(x.HandoverDay,15));
            foreach(var eq in equipment.Rows.Where(x=>Rules.Contract(x.Contract)==code &&
                x.Category!="Máy kéo").Take(15))
                sb.AppendLine("Thiết bị "+Clean(eq.Category,16)+
                    ": "+Clean(eq.Material,65)+" · số lượng="+
                    (eq.Quantity?.ToString(CultureInfo.InvariantCulture)??"chưa rõ")+
                    " · serial "+Clean(eq.Serial,40));
        }
        var value=sb.ToString();
        return value.Length>5800?value[..5800]+"\n[Đã giới hạn tóm tắt cho Gemini.]":value;
    }
}
