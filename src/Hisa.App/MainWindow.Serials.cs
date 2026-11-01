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
        // A printed copy includes serials from MÁY KÉO.xlsx even if the
        // operator has not yet clicked Save. Printing itself does not
        // alter the saved slip, delivery status or the source workbook.
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
            // Read the source by HĐ first; if still missing, explicitly ask
            // for a manual serial rather than printing a blank machine.
            Notify("HISA chưa tìm thấy "+imported.Missing+
                " serial máy kéo khớp HĐ/công trình/model trong MÁY KÉO.xlsx. "+
                "Hãy nhập serial thực tế ở bảng tiếp theo. Sau khi xác nhận, "+
                "HISA đề nghị ghi lại vào cột I của file nguồn.");
            if(!EditMotorSerials(copy,false))return;
            var left=MotorSerials.BuildRows(copy).Count(x=>
                string.IsNullOrWhiteSpace(x.SerialNumber));
            if(left>0)
            {
                Notify("Phiếu còn "+left+" máy kéo thiếu serial. "+
                    "Hãy kiểm tra MÁY KÉO.xlsx hoặc nhập đúng serial trước khi in.");
                return;
            }
        }
        PrintService.PreviewSlip(this,copy,FindTemplate(copy.TemplateId));
    }

    private bool EditMotorSerials(Slip slip,bool requireAll)
    {
        FillMotorSerialsFromSource(slip);
        var rows=MotorSerials.BuildRows(slip);
        if(rows.Count==0){slip.MotorSerials=[];if(!requireAll)Notify("Phiếu không có máy kéo đang chọn để bàn giao.");return true;}
        var dialog=new MotorSerialWindow(slip,_service.Slips,requireAll){Owner=this};
        if(dialog.ShowDialog()!=true)return false;
        slip.MotorSerials=dialog.ResultSerials;
        try
        {
            var proposed=MotorSerialExcelSync.Preview(_service.Settings,slip);
            if(proposed.Proposed.Count>0)
            {
                var rows=string.Join("\n",proposed.Proposed.Take(12)
                    .Select(x=>"• HĐ "+x.Contract+" · "+x.Project+
                        " · dòng "+x.SourceRow+" → serial "+x.Serial));
                if(Ask("ĐÃ NHẬP "+proposed.Proposed.Count+" SERIAL CHƯA CÓ TRONG EXCEL.\n\n"+
                       "HISA sẽ ghi ngược vào MÁY KÉO.xlsx, CỘT I, theo đúng HĐ, công trình, đơn vị và model:\n"+
                       rows+(proposed.Proposed.Count>12?"\n…":"")+
                       "\n\nChỉ điền ô serial đang trống, không sửa ngày xuất, trạng thái hoặc công thức. "+
                       "Đã tạo bản sao lưu trước khi ghi. Đồng ý?"))
                {
                    var result=MotorSerialExcelSync.Apply(_service.Settings,slip,
                        System.IO.Path.Combine(_service.BackupDirectory,"Excel"));
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
        // Keep existing correctly matched serials; create blank entries for missing units.
        // Only the explicit Yes action waives the missing-serial requirement.
        var import=FillMotorSerialsFromSource(slip);
        MotorSerials.Validate(slip,_service.Slips,requireAll:false);
        var missing=slip.MotorSerials.Count(x=>string.IsNullOrWhiteSpace(x.SerialNumber));
        if(missing==0)
        {
            slip.AllowMissingMotorSerials=false;
            return true;
        }
        Notify("Còn "+missing+" máy kéo thiếu serial theo HĐ/công trình trong file MÁY KÉO.xlsx. "+
            "Hãy nhập tay serial thực tế để in trên phiếu xuất/bàn giao.");
        if(EditMotorSerials(slip,true))
        {
            slip.AllowMissingMotorSerials=false;
            return true;
        }
        // Keep the pre-existing explicit exception path for shipments where
        // physically verified serials genuinely cannot be obtained, but never
        // do this without a separate warning + Yes confirmation.
        var confirmation=MessageBox.Show(this,
            $"Phiếu {slip.SlipCode} có {missing:N0} máy kéo chưa nhập serial trong tổng số {slip.MotorSerials.Count:N0} máy.\n\nBạn có muốn xác nhận ĐÃ XUẤT dù thiếu serial?\n\nCác serial còn thiếu sẽ được lưu trống để bổ sung sau. Serial đã nhập vẫn được kiểm tra trùng.",
            "Xác nhận xuất máy kéo thiếu serial",
            MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);
        if(confirmation!=MessageBoxResult.Yes)return false;
        slip.AllowMissingMotorSerials=true;
        return true;
    }
    private void ShowMotorSerialHistory(Slip slip)
    {
        var grid=Table(slip.MotorSerials??[],("Số HĐ","Contract",120),("Công trình","Project",260),("Model máy kéo","Material",-1),("Máy thứ","UnitIndex",85),("Serial","SerialNumber",240));
        ShowDialogGrid("Serial máy kéo · "+slip.SlipCode,grid);
    }
}
