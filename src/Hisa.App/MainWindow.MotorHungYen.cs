using System.Windows;
using System.Windows.Controls;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowMotorHungYen()
    {
        _page="motor-hung-yen";
        var store=new MotorHungYenStore(_service.DataDirectory);
        var catalog=EquipmentListReader.Load(_service.Settings);
        var machineRows=catalog.Rows.Where(x=>x.Category=="Máy kéo").ToList();
        var items=store.List(machineRows);
        var query=Input();query.Width=220;
        var stage=new ComboBox
        {
            Width=190,
            ItemsSource=new[]{"Tất cả trạng thái",MotorHungYenStore.Name(MotorHungYenStore.Pending),
                MotorHungYenStore.Name(MotorHungYenStore.Sent),
                MotorHungYenStore.Name(MotorHungYenStore.Completed),
                MotorHungYenStore.Name(MotorHungYenStore.HandedOver)},
            SelectedIndex=0
        };
        var effectiveDay=DateInput(DateTime.Today.ToString("yyyy-MM-dd"));
        var note=Input();note.Width=340;
        var summary=T("",12,true,Muted);
        var selection=T("Chọn một máy kéo trong bảng để cập nhật trạng thái.",12,true);
        var grid=Table(Array.Empty<MotorHungYenStore.Item>(),
            ("Số HĐ","Contract",110),("Công trình","Project",200),
            ("Đơn vị","Company",130),("Máy kéo","Material",-1),
            ("SL","Quantity",56),("Số lô","Lot",138),("Seri","Serial",115),
            ("Tình trạng gia công","StatusLabel",200),
            ("Đã gửi","SentDay",110),("Đã làm","CompletedDay",110),
            ("Đã bàn giao","HandoverDay",118),
            ("Ghi chú","LastNote",180),
            ("Nguồn","SourceStatus",135));
        grid.IsReadOnly=true;
        grid.SelectionMode=DataGridSelectionMode.Single;
        grid.CanUserAddRows=false;
        grid.SelectionChanged+=(_,_)=>
        {
            if(grid.SelectedItem is MotorHungYenStore.Item picked)
            {
                selection.Text="HĐ "+picked.Contract+" · "+picked.Project+
                    " · "+picked.Material+" / seri "+(picked.Serial.Length>0?picked.Serial:"(chưa có)")+
                    " · "+picked.StatusLabel;
                note.Text=picked.LastNote;
            }
        };

        void Filter()
        {
            var text=Rules.Code(query.Text);
            var selectedStage=stage.SelectedIndex;
            var filtered=items.Where(x=>
                (selectedStage==0 || x.State==selectedStage switch
                {
                    1=>MotorHungYenStore.Pending,
                    2=>MotorHungYenStore.Sent,
                    3=>MotorHungYenStore.Completed,
                    4=>MotorHungYenStore.HandedOver,
                    _=>""
                }) &&
                (text.Length==0 || Rules.Code(x.Contract+" "+x.Project+" "+x.Company+" "+
                    x.Material+" "+x.Serial+" "+x.Lot).Contains(text,StringComparison.Ordinal)))
                .OrderBy(x=>x.Contract).ThenBy(x=>x.Project).ThenBy(x=>x.Serial).ToList();
            var previous=(grid.SelectedItem as MotorHungYenStore.Item)?.Key;
            grid.ItemsSource=filtered;
            if(previous!=null)grid.SelectedItem=filtered.FirstOrDefault(x=>x.Key==previous);
            summary.Text=$"{filtered.Count:N0} dòng đang xem / {items.Count:N0} máy kéo · "+
                $"{items.Count(x=>x.State==MotorHungYenStore.Sent):N0} đang gửi làm · "+
                $"{items.Count(x=>x.State==MotorHungYenStore.Completed):N0} đã làm xong · "+
                $"{items.Count(x=>x.State==MotorHungYenStore.HandedOver):N0} đã bàn giao";
        }
        query.TextChanged+=(_,_)=>Filter();
        stage.SelectionChanged+=(_,_)=>Filter();
        void MoveTo(string next)
        {
            if(grid.SelectedItem is not MotorHungYenStore.Item selected)
            {
                Notify("Chọn một dòng máy kéo cần xác nhận.");return;
            }
            if(Day(effectiveDay).Length==0)
            {
                Notify("Chọn ngày thực tế trước khi xác nhận.");return;
            }
            if(!selected.PresentInSource)
            {
                Notify("Máy kéo này không còn trong Excel nguồn. Kiểm tra file trước khi cập nhật.");return;
            }
            var current=selected.StatusLabel;
            if(!Ask($"XÁC NHẬN TRẠNG THÁI MÁY KÉO\n"+
                $"HĐ: {selected.Contract}\nCông trình: {selected.Project}\n"+
                $"Máy: {selected.Material}\nSeri: {selected.Serial}\n"+
                $"Hiện tại: {current}\nChuyển sang: {MotorHungYenStore.Name(next)}\n"+
                $"Ngày thực tế: {Rules.Day(Day(effectiveDay))}\n\n"+
                "Chỉ chọn Có khi đã kiểm tra chứng từ, bàn giao hoặc thực tế sản xuất. "+
                "HISA lưu lịch sử riêng, không ghi vào Excel và không cập nhật tồn kho."))return;
            try
            {
                store.Advance(selected,next,Day(effectiveDay),note.Text);
                var selectedKey=selected.Key;
                items=store.List(machineRows);
                Filter();
                grid.SelectedItem=(grid.ItemsSource as IEnumerable<MotorHungYenStore.Item>)?
                    .FirstOrDefault(x=>x.Key==selectedKey);
            }
            catch(Exception ex) when(ex is InvalidOperationException or
                ArgumentException or System.IO.IOException or System.IO.InvalidDataException)
            {
                Notify("Không thể ghi nhận:\n"+ex.Message);
            }
        }
        var tools=Card(Stack(
            T("MÁY KÉO GỬI HƯNG YÊN LÀM – THEO DÕI TIẾN ĐỘ",18,true),
            T("Ba bước riêng: (1) Đã gửi Hưng Yên → (2) Đã làm xong → (3) Đã bàn giao. Không tự suy ra từ cột ghi chú DXH hoặc từ việc có tên trong Excel.",12,false,Muted),
            Row(Field("Tìm theo HĐ / tên máy / seri / công trình",query),
                Field("Lọc trạng thái",stage),B("Làm mới từ file Máy kéo",()=>ShowMotorHungYen())),
            summary,
            selection,
            Row(Field("Ngày thực tế",effectiveDay),Field("Ghi chú / người nhận / chứng từ",note)),
            Row(B("1 · Xác nhận đã gửi Hưng Yên",()=>MoveTo(MotorHungYenStore.Sent),true),
                B("2 · Xác nhận đã làm xong",()=>MoveTo(MotorHungYenStore.Completed)),
                B("3 · Xác nhận đã bàn giao",()=>MoveTo(MotorHungYenStore.HandedOver)),
                B("Lịch sử dòng chọn",()=>
                {
                    if(grid.SelectedItem is not MotorHungYenStore.Item selected)
                    {
                        Notify("Hãy chọn máy kéo để xem lịch sử.");return;
                    }
                    ShowDialogGrid("Lịch sử máy kéo HĐ "+selected.Contract,
                        Table(store.HistoryFor(selected.Key),
                            ("Trạng thái","Label",210),("Ngày thực tế","EffectiveDay",140),
                            ("Ghi nhận lúc","RecordedAt",210),("Người thao tác","By",150),
                            ("Ghi chú","Note",-1)));
                }))),
            T("Dữ liệu trạng thái lưu tại Tài liệu/HISA Kho Vat Tu/APP KHO/data/motor_hung_yen_tracking.json và được sao lưu cùng HISA. Mỗi dòng máy/seri có lịch sử riêng; file MÁY KÉO.xlsx chỉ đọc.",11,false,Muted)
        ));
        var page=Stack(tools);
        if(_service.Settings.TractionMachineListPath.Length==0)
            page.Children.Add(Alert("Chưa chọn file Máy kéo. Mở Cấu hình nguồn dữ liệu → Danh sách thiết bị theo hợp đồng để liên kết MÁY KÉO.xlsx."));
        if(catalog.Warnings.Count>0)
            page.Children.Add(Alert("Nguồn Excel: "+string.Join(" | ",catalog.Warnings)));
        var header=new ScrollViewer {Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=415};
        var view=Layout(header,grid);
        Filter();
        SetPage("Theo dõi máy kéo Hưng Yên",
            "Đã gửi làm → Đã làm xong → Đã bàn giao · theo từng HĐ và seri",
            view);
    }
}
