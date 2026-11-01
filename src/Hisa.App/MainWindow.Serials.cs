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
        var copy=WarehouseService.Clone(slip);
        var imported=FillMotorSerialsFromSource(copy);
        if(imported.Missing>0)
        {
            // Never silently produce a motor handover without its serials.
            // Only ambiguous/missing cases require a manual review.
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
        slip.MotorSerials=dialog.ResultSerials;return true;
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
