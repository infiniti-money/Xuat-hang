using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>
/// Column N of the ORIGINAL MÁY KÉO.xlsx is the shared, two-way
/// production/handover field. Operators can edit it in Excel or in HISA.
/// A delivery date can only be written by a confirmed outgoing slip.
/// </summary>
public static class MotorProductionExcel
{
    public const int ProgressColumn=14;
    public const string Header="Ngày bàn giao";

    private static string MachineKey(string contract,string project,string company,
        string material,string serial,string lot)=>
        Rules.Identity(contract,project,company)+"|"+Rules.Code(serial)+"|"+
        Rules.Code(material)+"|"+Rules.Code(lot);

    public static string Status(string? raw)
    {
        var value=(raw??"").Trim();
        if(TryDay(value,out _))return "Đã bàn giao";
        return Rules.Code(value) switch
        {
            "" or "CHUALAM"=>"Chưa làm",
            "DALAM" or "DALAMXONG"=>"Đã làm",
            _=>"Cần đối chiếu"
        };
    }

    public static string HandoverDay(string? raw)=>
        TryDay(raw,out var day)?day.ToString("dd/MM/yyyy",CultureInfo.InvariantCulture):"";

    private static bool TryDay(string? raw,out DateTime day)
    {
        return DateTime.TryParseExact((raw??"").Trim(),
            ["yyyy-MM-dd","dd/MM/yyyy","d/M/yyyy","yyyy/MM/dd","dd-MM-yyyy"],
            CultureInfo.InvariantCulture,DateTimeStyles.None,out day);
    }

    private static string Value(IXLCell cell)
    {
        if(cell.IsEmpty())return "";
        if(cell.TryGetValue<DateTime>(out var date))
            return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(cell.TryGetValue<double>(out var serial) && serial>40000 && serial<80000)
        {
            try{return DateTime.FromOADate(serial).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
            catch(ArgumentException){}
        }
        return cell.GetString().Trim();
    }

    /// <summary>Modify only the uniquely identified machine in the user's original workbook.</summary>
    public static bool SetStatus(AppSettings settings,MotorHungYenStore.Item machine,
        string status,string backupDirectory)
    {
        if(status is not ("Chưa làm" or "Đã làm"))
            throw new ArgumentException("Chỉ được chọn Chưa làm hoặc Đã làm.");
        if(!machine.PresentInSource)
            throw new InvalidOperationException("Máy đã không còn trong danh sách Excel.");
        var path=settings.TractionMachineListPath;
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy file MÁY KÉO.xlsx.");
        using var workbook=new XLWorkbook(path);
        var sheet=workbook.Worksheets.First();
        if(Rules.Code(sheet.Cell(2,4).GetString())!="HD")
            throw new InvalidDataException("Không xác định được cột hợp đồng trong MÁY KÉO.xlsx.");
        var heading=sheet.Cell(2,ProgressColumn).GetString().Trim();
        if(heading.Length>0 && Rules.Code(heading)!=Rules.Code(Header))
            throw new InvalidDataException("Cột N đang có tiêu đề khác 'Ngày bàn giao'. Không ghi đè dữ liệu cũ.");
        var rows=EquipmentListReader.Read(path,"Máy kéo");
        var key=MachineKey(machine.Contract,machine.Project,machine.Company,
            machine.Material,machine.Serial,machine.Lot);
        var matches=rows.Where(row=>MachineKey(row.Contract,row.Project,row.Company,
            row.Material,row.Serial,row.Lot)==key).ToList();
        if(matches.Count!=1)
            throw new InvalidDataException("Không tìm được duy nhất máy theo HĐ, công trình, đơn vị, model, seri và lô. Chưa sửa Excel.");
        var cell=sheet.Cell(matches[0].SourceRow,ProgressColumn);
        var original=Value(cell);
        if(Status(original)=="Đã bàn giao")
            throw new InvalidOperationException("Máy đã có ngày bàn giao tại cột N. Không thể thay bằng trạng thái làm; hãy đối chiếu phiếu xuất.");
        if(Status(original)=="Cần đối chiếu")
            throw new InvalidOperationException("Cột N chứa giá trị nhập tay chưa nhận diện: '"+original+"'. Không ghi đè.");
        if(original==status && heading.Length>0)return false;
        if(heading.Length==0)
        {
            sheet.Cell(2,ProgressColumn).Value=Header;
            sheet.Cell(2,ProgressColumn).Style.Font.Bold=true;
            sheet.Column(ProgressColumn).Width=18;
        }
        cell.Value=status;
        MotorHandoverExcel.SaveExcel(workbook,path,backupDirectory);
        return true;
    }
}
