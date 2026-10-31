using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowGeminiExcelAssistant()
    {
        _page="gemini-excel";
        GeminiExcelAssistant.Preview? viewed=null;
        GeminiExcelAssistant.Proposal? suggestion=null;
        var file=T("Chưa chọn file Excel. Chỉ chọn dữ liệu thuộc Kho HISA đã cấu hình.",12,false,Muted);
        var text=Input("Kiểm tra bảng và đề xuất sửa lỗi chính tả, ghi chú hoặc mô tả tại các dòng đã hiển thị.",true);
        text.Height=64;
        var previewBox=new TextBox
        {
            AcceptsReturn=true,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=215,
            FontFamily=new System.Windows.Media.FontFamily("Consolas"),
            FontSize=12,Padding=new Thickness(12)
        };
        var description=T("Gemini chỉ nhận tối đa 45 dòng × 16 cột sau khi bạn chủ động yêu cầu. Không gửi toàn bộ tệp.",12,false,Muted);
        var changes=new ContentControl();
        var apply=B("✓ Duyệt & ghi các ô đề xuất",()=>{ },true);
        apply.IsEnabled=false;
        void Reset()
        {
            viewed=null;suggestion=null;changes.Content=null;previewBox.Clear();
            apply.IsEnabled=false;
        }
        var choose=B("① Chọn Excel & xem dữ liệu",()=>
        {
            var dialog=new OpenFileDialog{Filter="Tệp Excel (*.xlsx)|*.xlsx",
                Title="Chọn tệp Excel HISA để AI đọc và đề xuất chỉnh sửa",CheckFileExists=true};
            if(dialog.ShowDialog(this)!=true)return;
            Reset();
            viewed=GeminiExcelAssistant.Read(dialog.FileName,_service.Settings);
            file.Text="Tệp đang xem: "+Path.GetFileName(viewed.Path)+" · sheet "+viewed.Sheet;
            previewBox.Text=viewed.Text;
            description.Text="Chỉ đang xem dữ liệu cục bộ, CHƯA gửi cho Gemini. "+
                "Không tự sửa nguồn Excel. Chỉ gửi bản xem trước sau khi nhấn bước ②.";
        });
        var propose=A("② Gemini đề xuất ô cần sửa",async()=>
        {
            if(viewed==null)throw new InvalidOperationException("Hãy chọn file Excel trước.");
            var key=new GeminiCredentialStore().ReadKey();
            if(string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Chưa có Gemini API Key; cần cấu hình khóa.");
            apply.IsEnabled=false;suggestion=null;
            description.Text="Đang xin gợi ý sửa từ Gemini; chỉ gửi bản xem trước đã chọn…";
            suggestion=await GeminiExcelAssistant.ProposeAsync(key,text.Text,viewed);
            description.Text=suggestion.Summary+" · "+suggestion.Changes.Count+
                " ô được đề xuất. Xem kỹ trước khi duyệt.";
            changes.Content=Table(suggestion.Changes,
                ("Dòng","Row",55),("Cột","Column",55),
                ("Hiện tại","Before",210),("Đề xuất","After",210),
                ("Lý do","Reason",-1));
            apply.IsEnabled=suggestion.Changes.Count>0;
        },true);
        apply.Click+=async (_,_)=>
        {
            if(viewed==null || suggestion==null || suggestion.Changes.Count==0)return;
            var original=viewed;var proposed=suggestion;
            if(!Ask("XÁC NHẬN AI SỬA FILE EXCEL\n\n"+
                Path.GetFileName(original.Path)+" · "+original.Sheet+"\n"+
                string.Join("\n",proposed.Changes.Select(c=>
                    "Dòng "+c.Row+" cột "+c.Column+": ["+c.Before+"] → ["+c.After+"]"))+
                "\n\nHISA sẽ kiểm tra lại file, sao lưu bản gốc rồi chỉ sửa đúng các ô đã duyệt. "+
                "Tổng hợp.xlsx, cột K máy kéo và chứng từ xuất không cho AI sửa."))return;
            apply.IsEnabled=false;
            try
            {
                var backup=await Task.Run(()=>GeminiExcelAssistant.Apply(original,proposed,
                    _service.Settings,Path.Combine(_service.BackupDirectory,"Excel")));
                Notify("Đã ghi các ô đã duyệt.\nBản sao trước sửa: "+backup);
                Reset();
                await _service.RefreshAsync(false);
                description.Text="Đã cập nhật. Để làm tiếp, hãy chọn lại Excel và đọc bản mới.";
            }
            catch(Exception e)
            {
                description.Text="CHƯA GHI: "+e.Message;
                // Users can retry only after a new preview; avoids stale proposals.
                Reset();
            }
        };
        var heading=Card(Stack(T("GEMINI · TRỢ LÝ ĐỌC VÀ ĐỀ XUẤT SỬA EXCEL",19,true),
            T("Quy trình: chọn file HISA → xem trước dữ liệu → gửi bản xem trước đến Gemini → xem đề xuất từng ô → phê duyệt và sao lưu trước khi ghi.",12,false,Muted),
            T("KHÔNG tự sửa sổ kho Tổng hợp, sổ xuất, cột K máy kéo, seri hoặc công thức. Giới hạn 8 ô/lần; nếu Excel đổi khi đang xem, phải đọc lại.",12,false,Brush("#A36731"))));
        var panel=Stack(heading,Card(Stack(
            Row(choose,B("Về trò chuyện Gemini",()=>Navigate("gemini-chat"))),
            file,Field("Yêu cầu sửa nội dung",text),
            Row(propose,apply),description,
            T("BẢN ĐỌC EXCEL TRÊN MÁY (CHƯA GỬI)",13,true),
            previewBox,T("ĐỀ XUẤT ĐANG CHỜ XÁC NHẬN",13,true),changes)));
        SetPage("AI đọc & sửa Excel","Đọc có giới hạn · đề xuất từng ô · xác nhận người dùng · sao lưu",new ScrollViewer{
            Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
}
