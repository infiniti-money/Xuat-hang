using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    private bool EditMotorSerials(Slip slip,bool requireAll)
    {
        var rows=MotorSerials.BuildRows(slip);
        if(rows.Count==0){slip.MotorSerials=[];if(!requireAll)Notify("Phiếu không có máy kéo đang chọn để bàn giao.");return true;}
        var dialog=new MotorSerialWindow(slip,_service.Slips,requireAll){Owner=this};
        if(dialog.ShowDialog()!=true)return false;
        slip.MotorSerials=dialog.ResultSerials;return true;
    }
    private void ShowMotorSerialHistory(Slip slip)
    {
        var grid=Table(slip.MotorSerials??[],("Số HĐ","Contract",120),("Công trình","Project",260),("Model máy kéo","Material",-1),("Máy thứ","UnitIndex",85),("Serial","SerialNumber",240));
        ShowDialogGrid("Serial máy kéo · "+slip.SlipCode,grid);
    }
}
