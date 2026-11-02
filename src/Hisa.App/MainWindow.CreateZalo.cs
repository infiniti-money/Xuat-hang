using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Hisa.App;

public partial class MainWindow
{
    /// <summary>
    /// Reuse the currently read Zalo image when present. Otherwise prompt for
    /// a local image and analyze it exactly once. Nothing is written to Excel.
    /// </summary>
    private async Task<bool> EnsureZaloScheduleForSlipAsync()
    {
        if(_aiDocument!=null)return true;
        var picker=new OpenFileDialog
        {
            Title="Chọn ảnh Zalo có lịch cont về và dự kiến xuất hàng",
            Filter="Ảnh lịch Zalo JPG, PNG, WEBP|*.jpg;*.jpeg;*.png;*.webp"
        };
        if(picker.ShowDialog(this)!=true)return false;
        var geminiKey=new GeminiCredentialStore().ReadKey();
        if(string.IsNullOrWhiteSpace(geminiKey))
            throw new InvalidOperationException("Chưa có Gemini API Key. Vào Cấu hình nguồn dữ liệu → Gemini AI trước khi đọc ảnh.");
        // Do not lose the existing working image/document on analysis failure.
        var parsed=await AiScheduleReader.AnalyzeAsync(picker.FileName,geminiKey);
        _aiImagePath=picker.FileName;
        _aiDocument=parsed;
        _aiShowDispatch=false;
        return true;
    }

    /// <summary>
    /// A direct entry from Kế hoạch & phiếu bàn giao to the physical receipt
    /// workflow. Forecasted Zalo dates never automatically become actual dates.
    /// </summary>
    private async Task OpenZaloContainerArrivalFromSlipAsync()
    {
        if(!await EnsureZaloScheduleForSlipAsync())return;
        if(_aiDocument?.Arrivals.Count==0)
        {
            Notify("Ảnh Zalo chưa có hàng DỰ KIẾN CONT VỀ. "+
                "Chọn ảnh khác hoặc bổ sung hợp đồng cont về trong màn hình AI.");
        }
        _aiShowDispatch=false;
        ShowAiSchedules(); // Step 1: operator ticks actual contracts and verified packages.
    }

    /// <summary>
    /// Select outgoing contracts from the AI schedule. Do not take any Arrivals
    /// and do not save an export slip or change Excel from this dialog.
    /// </summary>
    private ZaloDispatchContractImport.Selection? PickZaloDispatchContracts(
        AiScheduleDocument doc,string existingContractCodes)
    {
        if(doc.Dispatches.Count==0)
        {
            Notify("Ảnh Zalo không có dòng HÀNG DỰ KIẾN XUẤT KHO. "+
                "Hãy kiểm tra ảnh hoặc nhập số hợp đồng thủ công.");
            return null;
        }
        var rows=doc.Dispatches.ToList();
        var grid=Table(rows,
            ("Số hợp đồng","Contract",125),
            ("Công trình","Project",245),
            ("Đơn vị","Company",170),
            ("Ngày dự kiến xuất","PlannedDate",130),
            ("Trạng thái lịch","ScheduleLabel",165),
            ("Cần kiểm tra","ReviewNote",245));
        grid.IsReadOnly=false;
        grid.CanUserAddRows=false;
        grid.Height=345;
        grid.Columns.Insert(0,OneClickCheckColumn("Chọn HĐ","SelectedForHandover",84));
        var info=T("",12,true,Brush("#177E89"));
        void RefreshCount()
        {
            info.Text="Đã tích "+rows.Count(x=>x.SelectedForHandover)+"/"+rows.Count+
                " công trình dự kiến xuất. HĐ chưa rõ số/công trình/đơn vị phải sửa ở AI trước khi lập phiếu.";
        }
        foreach(var row in rows)row.SelectedForHandover=false;
        var dialog=new Window
        {
            Owner=this,Title="Lấy HĐ dự kiến xuất từ ảnh Zalo",
            Width=Math.Min(1200,Math.Max(850,ActualWidth-50)),
            Height=590,MinWidth=800,MinHeight=500,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Background=Brush("#F3F6FA")
        };
        ZaloDispatchContractImport.Selection? result=null;
        var actions=Row(
            B("☑ Chọn tất cả HĐ đủ dữ liệu",()=>
            {
                foreach(var line in rows)
                    line.SelectedForHandover=!line.NeedsReview &&
                        Rules.Contract(line.Contract).Length>=4 &&
                        !string.IsNullOrWhiteSpace(line.Project) &&
                        !string.IsNullOrWhiteSpace(line.Company);
                grid.Items.Refresh();RefreshCount();
            }),
            B("Bỏ chọn",()=>
            {
                foreach(var line in rows)line.SelectedForHandover=false;
                grid.Items.Refresh();RefreshCount();
            }),
            B("Đưa HĐ đã chọn vào ô lập phiếu",()=>
            {
                try
                {
                    Commit(grid);
                    result=ZaloDispatchContractImport.Merge(doc,
                        rows.Where(x=>x.SelectedForHandover).ToList(),
                        existingContractCodes);
                    dialog.DialogResult=true;
                }
                catch(Exception ex)
                {
                    MessageBox.Show(dialog,ex.Message,"Kiểm tra hợp đồng",
                        MessageBoxButton.OK,MessageBoxImage.Warning);
                }
            },true),
            B("Hủy",()=>dialog.DialogResult=false)
        );
        grid.SelectionChanged+=(_,_)=>RefreshCount();
        RefreshCount();
        dialog.Content=new Border
        {
            Padding=new Thickness(20),
            Child=Stack(
                T("HÀNG DỰ KIẾN XUẤT KHO · TRÍCH XUẤT TỪ ZALO",16,true),
                T("Chỉ chọn dòng ở bảng xuất. Không tự lấy cont về. "+
                  "HĐ đã nhập tay được giữ lại; những công trình T1/T2 được đối chiếu riêng.",12,false,Muted),
                grid,info,actions,
                T("Đưa vào ô nhập không lập phiếu và không ghi dữ liệu. "+
                  "Bạn vẫn chọn nhóm vật tư và bấm Tải vật tư & kiểm tra.",11,false,Muted))
        };
        return dialog.ShowDialog()==true?result:null;
    }
}
