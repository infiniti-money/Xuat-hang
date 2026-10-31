using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    /// <summary>
    /// Shared source-worksheet workflow for Zalo-extracted and manually entered
    /// contracts. Never treats a forecast as a warehouse arrival.
    /// </summary>
    private FrameworkElement BuildContDiSourcePanel(
        Func<List<AiScheduleLine>> requests,Action? commitInput=null,bool searchWhenLoaded=false)
    {
        var actualDate=DateInput(DateTime.Today.ToString("yyyy-MM-dd"));
        string SourceLabel()=>"các file Excel trong thư mục Container";
        var explanation=T("HISA CHỈ quét Container khi bạn bấm Tìm kiện, không quét lúc khởi động. "+
            "Chọn ngày thực tế đã nhận và từng kiện ghi HƯNG YÊN/HUNG YEN; "+
            "giữ riêng công trình, thang 1/2, T1/T2, hậu tố (1)/(2) và ngày cont đi. "+
            "Không tra cứu hay ghi trang Cont đi Hưng Yên trong Tổng hợp.xlsx.",12,false,Muted);
        var feedback=T("Bấm Tìm kiện để đọc mới "+SourceLabel()+".",12,false,Muted);
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
            feedback.Text="Đang đọc nguồn: "+SourceLabel()+"…";
            await _service.RefreshContDiForArrivalAsync();
            rows=ContDiArrivalLookup.Preview(input,_service.ContDiLookupSnapshot,Day(actualDate));
            grid.ItemsSource=rows;scanned=true;
            var ready=rows.Count(x=>x.Status==ContDiArrivalLookup.Ready);
            var already=rows.Count(x=>x.Status=="Kiện đã nhập kho");
            feedback.Text=$"Đã tìm trong {SourceLabel()}: {rows.Count} dòng/kiện · "+
                $"{ready} kiện đủ nguồn, chờ xác nhận thực tế · {already} kiện đã nhập. "+
                "Mỗi ô tích chỉ chọn một kiện, KHÔNG lấy tất cả kiện cùng HĐ. "+
                "Sửa file nguồn hoặc số HĐ thì bấm Tìm kiện lại.";
        }
        var buttons=Row(
            A("⌕ Tìm kiện Hưng Yên trong Container",SearchAsync,true),
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
                // No cached permission: recheck the source and package identity/content
                // immediately before displaying the final confirmation.
                await _service.RefreshContDiForArrivalAsync();
                var selected=ContDiArrivalLookup.SelectVerified(marked,requests(),
                    _service.ContDiLookupSnapshot,Day(actualDate));
                var duplicateWarnings=marked
                    .Where(x=>x.Notes.Contains("CẢNH BÁO",StringComparison.Ordinal))
                    .Select(x=>x.Contract+" · "+x.Project+" · kiện "+x.PackageNo)
                    .Distinct().ToList();
                if(!Ask("XÁC NHẬN CONT ĐÃ VỀ KHO HƯNG YÊN\n\n"+
                    (duplicateWarnings.Count==0?"":"⚠ CÓ KIỆN TRÙNG VẬT TƯ CÙNG NGÀY: "+
                        string.Join("; ",duplicateWarnings)+"\nHãy kiểm tra đây là kiện mới thực tế.\n\n")+
                    "Ngày về thực tế: "+Rules.Day(Day(actualDate))+"\n"+
                    "Số kiện: "+selected.Count+"\n"+
                    "Hợp đồng: "+selected.Select(x=>Rules.Contract(x.Contract)).Distinct().Count()+
                    "\nDòng vật tư Hưng Yên: "+selected.Sum(x=>x.Items.Count(i=>i.IsHungYen))+
                    "\n\n"+string.Join("\n",selected.Take(12).Select(x=>
                        x.Contract+" · "+x.Project+" · kiện "+x.PackageNo+
                        " · cont đi "+Rules.Day(x.SourceDate)))+
                    "\n\nHISA sẽ sao chép các dòng vật tư HƯNG YÊN của các kiện đã chọn "+
                    "từ "+SourceLabel()+" sang trang 'chi tiết' trong Tổng hợp.xlsx theo NGÀY VỀ trên. "+
                    "Không lấy kiện khác, không sửa KT hoặc trang Cont đi. Trước khi ghi sẽ sao lưu; file nhận đủ kiện chuyển sang Đã nhập kho, file hàng gửi phân loại riêng. Tiếp tục?"))
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
            T("CONTAINER → XÁC NHẬN KIỆN → TỔNG HỢP.XLSX / CHI TIẾT",16,true),
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
                catch(Exception ex) {feedback.Text="Chưa tìm được kiện từ "+SourceLabel()+": "+
                    ex.Message+" · Kiểm tra file nguồn rồi bấm Tìm kiện.";}
            };
        }
        return panel;
    }
}
