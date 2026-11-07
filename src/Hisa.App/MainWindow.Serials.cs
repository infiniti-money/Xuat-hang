using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    private MotorSerialAutoFill.Result FillMotorSerialsFromSource(Slip slip)
    {
        // Reads only the configured source workbook, never writes it.
        var production=EquipmentListReader.Load(_service.Settings);
        return MotorSerialAutoFill.Fill(slip,production.Rows,_service.Slips);
    }

    private void PrintSlipWithMotorSerials(Slip slip)
    {
        // Printing only uses an in-memory copy; never confirms a delivery.
        var copy=ReferenceEquals(slip,_draft)?slip:WarehouseService.Clone(slip);
        if(copy.Lines.Where(MotorSerials.IsMotor).Any(x=>
            x.Quantity is null||x.Quantity<0||
            x.Quantity!=decimal.Truncate(x.Quantity.Value)||x.Quantity>10000))
        {
            Notify("Phiếu có dòng máy kéo chưa rõ số lượng máy nguyên. "+
                "Hãy kiểm tra số máy trên phiếu trước khi lấy serial và in.");
            return;
        }
        var imported=FillMotorSerialsFromSource(copy);
        if(imported.Missing>0)
        {
            var answer=MessageBox.Show(this,
                "Đã lấy serial có thể đối chiếu từ MÁY KÉO.xlsx. "+
                "Còn "+imported.Missing+" máy kéo chưa có serial.\n\n"+
                "Bạn có muốn nhập bổ sung serial trước khi xem/in phiếu không?\n\n"+
                "Có: mở bảng nhập serial (tự chọn công trình nếu một HĐ có nhiều công trình).\n"+
                "Không: xem/in phiếu ngay, để trống serial chưa có.\n"+
                "Hủy: quay lại phiếu.",
                "HISA · Serial máy kéo không bắt buộc",
                MessageBoxButton.YesNoCancel,MessageBoxImage.Question,
                MessageBoxResult.No);
            if(answer==MessageBoxResult.Cancel)return;
            if(answer==MessageBoxResult.Yes && !EditMotorSerials(copy,false))return;
        }
        PrintService.PreviewSlip(this,copy,FindTemplate(copy.TemplateId));
    }

    private bool ConfirmMotorProjectSelection(Slip slip,
        out Dictionary<string,string> confirmedProjects)
    {
        confirmedProjects=new();
        var production=EquipmentListReader.Load(_service.Settings);
        // Ask only for project collisions, and never change the HĐ/project
        // already selected on the source warehouse slip.
        var groups=MotorSerials.BuildRows(slip)
            .GroupBy(x=>(Rules.Contract(x.Contract),Rules.Code(x.Project)));
        foreach(var group in groups)
        {
            var first=group.First();
            var projects=MotorSerialProjectScope.Projects(
                production.Rows,first.Contract);
            if(projects.Count<=1)continue;
            var picker=new MotorSerialProjectWindow(
                first.Contract,first.Project,projects){Owner=this};
            if(picker.ShowDialog()!=true)return false;
            confirmedProjects[MotorSerialProjectScope.SlipKey(
                first.Contract,first.Project,first.Company)]=picker.SelectedProject;
        }
        return true;
    }

    private bool EditMotorSerials(Slip slip,bool requireAll)
    {
        FillMotorSerialsFromSource(slip);
        var rows=MotorSerials.BuildRows(slip);
        if(rows.Count==0){slip.MotorSerials=[];if(!requireAll)Notify("Phiếu không có máy kéo đang chọn để bàn giao.");return true;}
        if(!ConfirmMotorProjectSelection(slip,out var confirmedProjects))
            return false;
        if(confirmedProjects.Count>0)
        {
            // A chosen alias/source project may have existing serials. Pull
            // only those verified project-scoped entries into the draft.
            MotorSerialAutoFill.Fill(slip,
                EquipmentListReader.Load(_service.Settings).Rows,
                _service.Slips,confirmedProjects);
        }
        var dialog=new MotorSerialWindow(slip,_service.Slips,requireAll){Owner=this};
        if(dialog.ShowDialog()!=true)return false;
        slip.MotorSerials=dialog.ResultSerials;
        slip.ConfirmedMotorSourceProjects ??= [];
        foreach(var selected in confirmedProjects)
            slip.ConfirmedMotorSourceProjects[selected.Key]=selected.Value;
        try
        {
            var proposed=MotorSerialExcelSync.Preview(
                _service.Settings,slip,confirmedProjects);
            if(proposed.Proposed.Count>0)
            {
                var mappedRows=string.Join("\n",proposed.Proposed.Take(12)
                    .Select(x=>"• HĐ "+x.Contract+" · dòng "+x.SourceRow+
                        " (cột I) · "+x.Project+" · "+x.Model+
                        " → serial "+x.Serial));
                if(Ask("ĐÃ NHẬP "+proposed.Proposed.Count+" SERIAL CHƯA CÓ TRONG EXCEL.\n\n"+
                       "HISA tra HỢP ĐỒNG ở cột D và phân biệt CÔNG TRÌNH ở cột B.\n"+
                       "Chỉ ghi serial mới vào ô trống cột I đúng công trình đã đối chiếu.\n"+
                       "Không lấy serial từ công trình khác dù cùng HĐ. Các dòng sẽ ghi:\n"+
                       mappedRows+(proposed.Proposed.Count>12?"\n…":"")+
                       "\n\nChỉ điền ô serial đang trống, không sửa ngày xuất, trạng thái hoặc công thức. "+
                       "Đã tạo bản sao lưu trước khi ghi. Đồng ý?"))
                {
                    var result=MotorSerialExcelSync.Apply(_service.Settings,slip,
                        System.IO.Path.Combine(_service.BackupDirectory,"Excel"),
                        proposed.Proposed,confirmedProjects);
                    Notify("Đã ghi "+result.Written+" serial vào MÁY KÉO.xlsx cột I."+
                        (result.Warnings.Count>0?"\nCảnh báo: "+
                        string.Join("\n",result.Warnings.Take(6)):""));
                }
            }
            else if(proposed.Warnings.Count>0)
                Notify("Serial đã giữ trên phiếu. Chưa ghi nguồn Excel vì cần kiểm tra:\n"+
                    string.Join("\n",proposed.Warnings.Take(6)));
        }
        catch(Exception ex) when(ex is System.IO.IOException or
            System.IO.InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            Notify("Serial đã giữ trên phiếu nhưng CHƯA ghi được MÁY KÉO.xlsx:\n"+
                ex.Message+"\nHãy kiểm tra file nguồn rồi mở lại bảng serial để đồng bộ.");
        }
        return true;
    }
    private bool ConfirmMotorSerialsForDelivery(Slip slip)
    {
        // Existing serials are imported before delivery. Missing serials do
        // NOT force data entry; an explicit choice is required to ship blank.
        FillMotorSerialsFromSource(slip);
        MotorSerials.Validate(slip,_service.Slips,requireAll:false);
        var sourceRows=EquipmentListReader.Load(_service.Settings).Rows
            .Where(x=>x.Category=="Máy kéo").ToList();
        var sourceSerials=sourceRows
            .Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
            .Select(x=>Rules.Code(x.Serial))
            .ToHashSet(StringComparer.Ordinal);
        var unresolvedProjects=MotorSerials.BuildRows(slip)
            .Where(x=>!string.IsNullOrWhiteSpace(x.SerialNumber) &&
                !sourceSerials.Contains(Rules.Code(x.SerialNumber)))
            .GroupBy(x=>MotorSerialProjectScope.SlipKey(
                x.Contract,x.Project,x.Company))
            .Select(g=>g.First())
            .Where(x=>MotorSerialProjectScope.Projects(
                    sourceRows,x.Contract).Count>1 &&
                (!(slip.ConfirmedMotorSourceProjects ?? []).TryGetValue(
                    MotorSerialProjectScope.SlipKey(
                        x.Contract,x.Project,x.Company),out var approved) ||
                 string.IsNullOrWhiteSpace(approved)))
            .ToList();
        if(unresolvedProjects.Count>0)
        {
            var choice=MessageBox.Show(this,
                "Có "+unresolvedProjects.Count+" hợp đồng/công trình có serial "+
                "mới nhưng cột D của MÁY KÉO.xlsx đang chứa nhiều công trình.\\n\\n"+
                "Có: chọn và xác nhận công trình đích trước khi HISA "+
                "ghi serial mới vào cột I khi xuất hàng.\\n"+
                "Không: vẫn xác nhận Đã xuất, nhưng serial mới ở cột I "+
                "và ngày K tương ứng sẽ chờ đối chiếu.\\n"+
                "Hủy: quay lại phiếu, không xác nhận Đã xuất.",
                "HISA · Xác nhận công trình trước khi ghi serial I",
                MessageBoxButton.YesNoCancel,MessageBoxImage.Warning,
                MessageBoxResult.Yes);
            if(choice==MessageBoxResult.Cancel)return false;
            if(choice==MessageBoxResult.Yes)
            {
                if(!ConfirmMotorProjectSelection(slip,out var selectedProjects))
                    return false;
                slip.ConfirmedMotorSourceProjects ??= [];
                foreach(var selected in selectedProjects)
                    slip.ConfirmedMotorSourceProjects[selected.Key]=selected.Value;
            }
        }
        var missing=MotorSerials.BuildRows(slip).Count(x=>
            string.IsNullOrWhiteSpace(x.SerialNumber));
        if(missing==0)
        {
            slip.AllowMissingMotorSerials=false;
            return true;
        }
        var answer=MessageBox.Show(this,
            "Phiếu "+slip.SlipCode+" còn "+missing+
            " máy kéo chưa có serial sau khi tra MÁY KÉO.xlsx.\n\n"+
            "Bạn có muốn nhập bổ sung không? Không bắt buộc.\n\n"+
            "Có: nhập hoặc bổ sung vào đúng công trình; có thể giữ ô trống.\n"+
            "Không: xác nhận xuất với serial còn trống, bổ sung sau.\n"+
            "Hủy: không ghi nhận Đã xuất.",
            "HISA · Xác nhận serial máy kéo",
            MessageBoxButton.YesNoCancel,MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if(answer==MessageBoxResult.Cancel)return false;
        if(answer==MessageBoxResult.Yes && !EditMotorSerials(slip,false))
            return false;
        MotorSerials.Validate(slip,_service.Slips,requireAll:false);
        slip.AllowMissingMotorSerials=MotorSerials.BuildRows(slip).Any(x=>
            string.IsNullOrWhiteSpace(x.SerialNumber));
        return true;
    }

    private void ShowMotorSerialHistory(Slip slip)
    {
        var grid=Table(slip.MotorSerials??[],("Số HĐ","Contract",120),("Công trình","Project",260),("Model máy kéo","Material",-1),("Máy thứ","UnitIndex",85),("Serial","SerialNumber",240));
        ShowDialogGrid("Serial máy kéo · "+slip.SlipCode,grid);
    }
}
