using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    /// <summary>
    /// Shared source-file workflow for Zalo-extracted and manually entered
    /// contracts. Never treats a forecast as a warehouse arrival.
    /// </summary>
    private FrameworkElement BuildContDiSourcePanel(
        Func<List<AiScheduleLine>> requests,Action? commitInput=null,bool searchWhenLoaded=false)
    {
        var actualDate=DateInput(DateTime.Today.ToString("yyyy-MM-dd"));
        var explanation=T("Chọn ngày cont THỰC TẾ về kho. HISA tìm đúng số hợp đồng trong thư mục "+
            "CONT ĐI HƯNG YÊN, liệt kê từng kiện Hưng Yên riêng theo công trình, T1/T2, ngày cont đi "+
            "và số lượng. Không tự sao chép chỉ vì ảnh Zalo có lịch dự kiến.",12,false,Muted);
        var feedback=T("Bấm Tìm kiện trong CONT ĐI để đọc mới các file Excel nguồn.",12,false,Muted);
        var rows=new List<PlannedArrivalRow>();
        var scanned=false;
        var grid=Table(rows,
            ("Số hợp đồng","Contract",120),
            ("Công trình/T1/T2","Project",195),
            ("Đơn vị","Company",160),
            ("Cont nguồn","Container",130),
            ("Kiện","PackageNo",70),
            ("Ngày cont đi","SourceContDay",110),
            ("Vật tư Hưng Yên · SL","MaterialSummary",320),
            ("Kết quả","Status",220),
            ("Nguồn/cảnh báo","Notes",290));
        grid.CanUserAddRows=false;grid.IsReadOnly=false;
        grid.Columns.Insert(0,OneClickCheckColumn("Đã về", "Selected",85));
        void InvalidateSearch()
        {
            scanned=false;
            rows=[];grid.ItemsSource=rows;
            feedback.Text="Ngày thực tế hoặc danh sách hợp đồng đã thay đổi. Bấm Tìm kiện để đối chiếu mới.";
        }
        actualDate.SelectedDateChanged+=(_,_)=>InvalidateSearch();
        async Task SearchAsync()
        {
            commitInput?.Invoke();
            var input=requests();
            if(input.Count==0)
                throw new InvalidOperationException("Cần có số hợp đồng cont về (từ ảnh Zalo hoặc nhập tay).");
            if(Day(actualDate).Length==0)
                throw new InvalidOperationException("Chọn ngày cont thực tế về kho.");
            scanned=false;
            feedback.Text="Đang quét trực tiếp file Excel trong CONT ĐI HƯNG YÊN…";
            await _service.RefreshContDiForArrivalAsync();
            rows=ContDiArrivalLookup.Preview(input,_service.Snapshot,Day(actualDate));
            grid.ItemsSource=rows;scanned=true;
            var ready=rows.Count(x=>x.Status==ContDiArrivalLookup.Ready);
            var already=rows.Count(x=>x.Status=="Kiện đã nhập kho");
            feedback.Text=$"Đã tìm trong CONT ĐI: {rows.Count} dòng/kiện · "+
                $"{ready} kiện đủ nguồn, chờ xác nhận thực tế · {already} kiện đã nhập. "+
                "Mỗi ô tích chỉ chọn một kiện, KHÔNG lấy tất cả kiện cùng HĐ. "+
                "Sửa Excel hoặc số HĐ thì bấm Tìm kiện lại.";
        }
        var buttons=Row(
            A("⌕ Tìm kiện Hưng Yên trong CONT ĐI",SearchAsync,true),
            B("Bỏ chọn",()=>{foreach(var x in rows)x.Selected=false;grid.Items.Refresh();}),
            A("✓ Sao chép kiện đã về → Tổng hợp.xlsx",async()=>
            {
                commitInput?.Invoke();Commit(grid);
                if(!scanned)
                    throw new InvalidOperationException("Cần tìm lại dữ liệu nguồn mới nhất trước khi nhập.");
                if(Day(actualDate).Length==0 || actualDate.SelectedDate is null)
                    throw new InvalidOperationException("Chọn ngày thực tế cont về.");
                var marked=rows.Where(x=>x.Selected).ToList();
                if(marked.Count==0)
                    throw new InvalidOperationException("Tích từng kiện Hưng Yên đã về thực tế.");
                // No cached permission: rescan and check package identity/content
                // immediately before displaying the final confirmation.
                await _service.RefreshContDiForArrivalAsync();
                var selected=ContDiArrivalLookup.SelectVerified(marked,requests(),
                    _service.Snapshot,Day(actualDate));
                if(!Ask("XÁC NHẬN CONT ĐÃ VỀ KHO HƯNG YÊN\n\n"+
                    "Ngày về thực tế: "+Rules.Day(Day(actualDate))+"\n"+
                    "Số kiện: "+selected.Count+"\n"+
                    "Hợp đồng: "+selected.Select(x=>Rules.Contract(x.Contract)).Distinct().Count()+
                    "\nDòng vật tư Hưng Yên: "+selected.Sum(x=>x.Items.Count(i=>i.IsHungYen))+
                    "\n\n"+string.Join("\n",selected.Take(12).Select(x=>
                        x.Contract+" · "+x.Project+" · kiện "+x.PackageNo+
                        " · cont đi "+Rules.Day(x.SourceDate)))+
                    "\n\nHISA sẽ sao chép các dòng vật tư HƯNG YÊN của các kiện đã chọn "+
                    "từ nguồn CONT ĐI vào trang 'chi tiết' của Tổng hợp.xlsx theo NGÀY VỀ trên. "+
                    "Không lấy hàng khác HĐ, không sửa KT tổng hợp; trước khi ghi sẽ sao lưu. Tiếp tục?"))
                {
                    InvalidateSearch();return;
                }
                var outcome=await _service.RecordVerifiedArrivalAsync(selected,
                    actualDate.SelectedDate.Value,false);
                Notify($"Đã sao chép vào Tổng hợp.xlsx: {outcome.Contracts} hợp đồng · "+
                    $"{outcome.Packages} kiện · {outcome.Lines} dòng vật tư.\n"+
                    string.Join("\n",outcome.Warnings));
                InvalidateSearch();
                await SearchAsync();
            })
        );
        var panel=Stack(Card(Stack(
            T("TÌM KIỆN CONT ĐI → XÁC NHẬN CONT VỀ → TỔNG HỢP.XLSX",16,true),
            explanation,
            Row(Field("Ngày cont thực tế về kho",actualDate)),
            buttons,feedback)),
            grid);
        if(searchWhenLoaded)
        {
            panel.Loaded+=async (_,_) =>
            {
                if(scanned)return;
                try {await SearchAsync();}
                catch(Exception ex) {feedback.Text="Chưa tìm được kiện trong CONT ĐI: "+
                    ex.Message+" · Kiểm tra đường dẫn Excel nguồn, sau đó bấm Tìm kiện.";}
            };
        }
        return panel;
    }
}
