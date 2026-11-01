using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Hisa.App;

public partial class MainWindow
{
    private void ShowMotorHungYen()
    {
        _page="motor-hung-yen";
        var store=new MotorHungYenStore(_service.DataDirectory);
        var catalog=EquipmentListReader.Load(_service.Settings);
        var machineRows=catalog.Rows.Where(x=>x.Category=="Máy kéo").ToList();
        List<MotorHungYenStore.Item> ReadVisibleItems()
        {
            var result=store.List(machineRows);
            var deliveries=MotorHandoverExcel.ConfirmedDates(_service.Slips);
            var serialCounts=machineRows.Where(x=>!string.IsNullOrWhiteSpace(x.Serial))
                .GroupBy(x=>MotorHandoverExcel.IdentityFor(new MotorHungYenStore.Item
                {
                    Contract=x.Contract,Project=x.Project,Company=x.Company,Serial=x.Serial
                })).ToDictionary(x=>x.Key,x=>x.Count());
            foreach(var item in result.Where(x=>x.PresentInSource))
            {
                var key=MotorHandoverExcel.IdentityFor(item);
                if(serialCounts.TryGetValue(key,out var count)&&count==1 &&
                   deliveries.TryGetValue(key,out var confirmed))
                {
                    item.State=MotorHungYenStore.HandedOver;
                    item.HandoverDay=confirmed;
                }
                else
                {
                    // Treat operator-edited column K as the visible production
                    // status, while never treating an Excel date as a confirmed
                    // shipment (only a delivered slip may do that).
                    if(item.State==MotorHungYenStore.HandedOver)
                    {
                        item.State=MotorHungYenStore.Completed;
                        item.HandoverDay="";
                    }
                    if(item.SourceWorkValue=="Đã làm")
                        item.State=MotorHungYenStore.Completed;
                    else if(item.SourceWorkValue=="Chưa làm")
                        item.State=item.SentDay.Length>0?
                            MotorHungYenStore.Sent:MotorHungYenStore.Pending;
                }
            }
            return result;
        }
        var items=ReadVisibleItems();
        var query=Input();query.Width=310;
        query.ToolTip="Gõ hợp đồng, công trình, tên máy hoặc số serial: danh sách tự lọc ngay.";
        var stage=new ComboBox
        {
            Width=175,
            ItemsSource=new[]{"Tất cả trạng thái",MotorHungYenStore.Name(MotorHungYenStore.Pending),
                MotorHungYenStore.Name(MotorHungYenStore.Sent),
                MotorHungYenStore.Name(MotorHungYenStore.Completed),
                MotorHungYenStore.Name(MotorHungYenStore.HandedOver)},
            SelectedIndex=0
        };
        var workChoice=new ComboBox
        {
            Width=145,
            ItemsSource=new[]{"Chưa làm","Đã làm"},
            SelectedIndex=-1
        };
        var effectiveDay=DateInput(DateTime.Today.ToString("yyyy-MM-dd"));
        var note=Input();note.Width=340;
        var summary=T("",12,true,Muted);
        var hints=T("① Tìm hoặc chọn hợp đồng  →  ② Chọn đúng máy/serial  →  ③ Xác nhận tiến độ.",12,true,Brush("#137B75"));
        var selection=T("Chọn một máy kéo trong bảng bên dưới để xác nhận tiến độ.",12,true);
        var contractsSummary=T("",12,true,Muted);
        var confirmedOnly=new CheckBox
        {
            Content="Chỉ hiện hợp đồng đã xác nhận gửi gia công tại Hưng Yên",
            IsChecked=true,VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(0,0,16,0)
        };
        var selectedContractKey="";
        var contractGrid=Table(Array.Empty<MotorHungYenContractOverview.Contract>(),
            ("Số HĐ","ContractNumber",110),("Công trình","Project",205),
            ("Công ty","Company",140),("Số máy","MachineCount",68),
            ("Số lượng","Quantity",78),("Đang làm","SentCount",82),
            ("Làm xong","CompletedCount",85),("Bàn giao","HandedOverCount",86),
            ("Chưa gửi","PendingCount",81),("Tiến độ hợp đồng","Status",-1));
        contractGrid.IsReadOnly=true;
        contractGrid.SelectionMode=DataGridSelectionMode.Single;
        contractGrid.CanUserAddRows=false;
        var grid=Table(Array.Empty<MotorHungYenStore.Item>(),
            ("Số HĐ","Contract",110),("Công trình","Project",190),
            ("Đơn vị","Company",125),("Máy kéo","Material",-1),
            ("SL","Quantity",55),("Số lô","Lot",115),("Seri","Serial",100),
            ("Tiến độ xác nhận","StatusLabel",185),
            ("Chọn làm","WorkLabel",95),("Cột K (Excel)","ExcelStatusLabel",160),
            ("Ngày gửi","SentDay",100),("Ngày làm","CompletedDay",100),
            ("Ngày bàn giao","HandoverDay",110),
            ("Ghi chú","LastNote",170),("Nguồn","SourceStatus",130));
        grid.IsReadOnly=true;
        grid.SelectionMode=DataGridSelectionMode.Single;
        grid.CanUserAddRows=false;
        grid.MinHeight=155;
        grid.RowHeight=34;
        contractGrid.RowHeight=34;
        var rowStyle=new Style(typeof(DataGridRow));
        foreach(var (code,color) in new[]{
            (MotorHungYenStore.Pending,"#F8FAFC"),
            (MotorHungYenStore.Sent,"#FFF8E9"),
            (MotorHungYenStore.Completed,"#EBF8F5"),
            (MotorHungYenStore.HandedOver,"#EAF4FF")
        })
        {
            var trigger=new DataTrigger{Binding=new Binding("State"),Value=code};
            trigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty,Brush(color)));
            rowStyle.Triggers.Add(trigger);
        }
        var selectedRow=new Trigger{Property=DataGridRow.IsSelectedProperty,Value=true};
        selectedRow.Setters.Add(new Setter(DataGridRow.BackgroundProperty,Brush("#C9E8E4")));
        selectedRow.Setters.Add(new Setter(DataGridRow.ForegroundProperty,Brush("#15394B")));
        rowStyle.Triggers.Add(selectedRow);
        grid.RowStyle=rowStyle;
        grid.SelectionChanged+=(_,_)=>
        {
            if(grid.SelectedItem is MotorHungYenStore.Item picked)
            {
                selection.Text="HĐ "+picked.Contract+" · "+picked.Project+
                    " · "+picked.Material+" / seri "+(picked.Serial.Length>0?picked.Serial:"(chưa có)")+
                    " · "+picked.StatusLabel;
                note.Text=picked.LastNote;
                workChoice.SelectedIndex=picked.ExcelDispatchDay.Length>0?-1:picked.WorkLabel=="Đã làm"?1:0;
                hints.Text="ĐÃ CHỌN · HĐ "+picked.Contract+" · Serial "+
                    (picked.Serial.Length>0?picked.Serial:"CHƯA CÓ")+
                    " · "+picked.WorkLabel+" · "+
                    (picked.ExcelDispatchDay.Length>0?
                        "Có ngày xuất: "+Rules.Day(picked.ExcelDispatchDay):
                        "Chưa ghi ngày xuất thực tế");
            }
            else
                hints.Text="① Tìm hoặc chọn hợp đồng  →  ② Chọn đúng máy/serial  →  ③ Xác nhận tiến độ.";
        };

        void FilterMachines()
        {
            var search=Rules.Code(query.Text);
            var selectedStage=stage.SelectedIndex;
            var filtered=items.Where(x=>
                (selectedContractKey.Length==0 ||
                    MotorHungYenContractOverview.Identity(x)==selectedContractKey) &&
                (selectedStage==0 || x.State==(selectedStage switch
                {
                    1=>MotorHungYenStore.Pending,2=>MotorHungYenStore.Sent,
                    3=>MotorHungYenStore.Completed,4=>MotorHungYenStore.HandedOver,
                    _=>""
                })) &&
                (search.Length==0 || Rules.Code(x.Contract+" "+x.Project+" "+x.Company+
                    " "+x.Material+" "+x.Serial+" "+x.Lot).Contains(search,StringComparison.Ordinal)))
                .OrderBy(x=>x.Contract).ThenBy(x=>x.Project).ThenBy(x=>x.Serial).ToList();
            var previous=(grid.SelectedItem as MotorHungYenStore.Item)?.Key;
            grid.ItemsSource=filtered;
            if(previous!=null)grid.SelectedItem=filtered.FirstOrDefault(x=>x.Key==previous);
            summary.Text=$"{filtered.Count:N0} máy kéo đang xem / {items.Count:N0} máy · "+
                $"{items.Count(x=>x.State==MotorHungYenStore.Sent):N0} đang làm · "+
                $"{items.Count(x=>x.State==MotorHungYenStore.Completed):N0} đã làm xong · "+
                $"{items.Count(x=>x.State==MotorHungYenStore.HandedOver):N0} đã bàn giao";
        }

        void RefreshContracts()
        {
            var search=Rules.Code(query.Text);
            var all=MotorHungYenContractOverview.Summarize(items,confirmedOnly.IsChecked==true);
            var shown=all.Where(x=>search.Length==0 ||
                Rules.Code(x.ContractNumber+" "+x.Project+" "+x.Company)
                    .Contains(search,StringComparison.Ordinal) ||
                items.Any(item=>MotorHungYenContractOverview.Identity(item)==x.Key &&
                    Rules.Code(item.Material+" "+item.Serial+" "+item.Lot)
                        .Contains(search,StringComparison.Ordinal))).ToList();
            contractGrid.ItemsSource=shown;
            if(selectedContractKey.Length>0)
                contractGrid.SelectedItem=shown.FirstOrDefault(x=>x.Key==selectedContractKey);
            contractsSummary.Text=$"{shown.Count:N0} hợp đồng đang hiển thị · "+
                $"{MotorHungYenContractOverview.Summarize(items,true).Count:N0} hợp đồng đã gửi gia công tại Hưng Yên · "+
                $"{MotorHungYenContractOverview.Summarize(items,false).Count:N0} hợp đồng trong file Máy kéo";
            if(shown.Count==0 && confirmedOnly.IsChecked==true)
                contractsSummary.Text+="\nChưa có hợp đồng được xác nhận gửi. Bỏ chọn bộ lọc để xem HĐ từ Excel, sau đó chọn máy và xác nhận gửi thực tế.";
        }

        contractGrid.SelectionChanged+=(_,_)=>
        {
            if(contractGrid.SelectedItem is MotorHungYenContractOverview.Contract chosen)
                selectedContractKey=chosen.Key;
            else
                selectedContractKey="";
            FilterMachines();
        };
        confirmedOnly.Checked+=(_,_)=>{RefreshContracts();FilterMachines();};
        confirmedOnly.Unchecked+=(_,_)=>{RefreshContracts();FilterMachines();};
        query.TextChanged+=(_,_)=>{RefreshContracts();FilterMachines();};
        stage.SelectionChanged+=(_,_)=>FilterMachines();

        void MoveTo(string next)
        {
            if(grid.SelectedItem is not MotorHungYenStore.Item selected)
            {
                Notify("Chọn một máy kéo ở danh sách chi tiết để xác nhận.");return;
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
            if(!Ask($"XÁC NHẬN TRẠNG THÁI MÁY KÉO GIA CÔNG TẠI HƯNG YÊN\n"+
                $"HĐ: {selected.Contract}\nCông trình: {selected.Project}\n"+
                $"Máy: {selected.Material}\nSeri: {selected.Serial}\n"+
                $"Hiện tại: {current}\nChuyển sang: {MotorHungYenStore.Name(next)}\n"+
                $"Ngày thực tế: {Rules.Day(Day(effectiveDay))}\n\n"+
                "Chỉ chọn Có khi đã kiểm tra chứng từ hoặc thực tế sản xuất/bàn giao. "+
                "Lịch sử gửi làm lưu riêng; chưa thay đổi cột K nếu không chọn tình trạng làm."))return;
            try
            {
                store.Advance(selected,next,Day(effectiveDay),note.Text);
                var selectedKey=selected.Key;
                items=ReadVisibleItems();
                RefreshContracts();
                FilterMachines();
                grid.SelectedItem=(grid.ItemsSource as IEnumerable<MotorHungYenStore.Item>)
                    ?.FirstOrDefault(x=>x.Key==selectedKey);
            }
            catch(Exception ex) when(ex is InvalidOperationException or
                ArgumentException or System.IO.IOException or System.IO.InvalidDataException)
            {
                Notify("Không thể ghi nhận:\n"+ex.Message);
            }
        }
        async Task SaveWorkChoiceAsync(int chosenIndex)
        {
            if(grid.SelectedItem is not MotorHungYenStore.Item selected)
            {
                Notify("Chọn một dòng máy kéo có serial trước khi cập nhật.");return;
            }
            if(selected.State==MotorHungYenStore.HandedOver || selected.ExcelDispatchDay.Length>0)
            {
                Notify("Máy đã xuất/bàn giao hoặc cột K có ngày. Không thay thế ngày xuất bằng trạng thái làm.");return;
            }
            if(chosenIndex is not (0 or 1)||Day(effectiveDay).Length==0)
            {
                Notify("Chọn ngày thực tế và trạng thái Chưa làm / Đã làm.");return;
            }
            var choice=chosenIndex==1?"Đã làm":"Chưa làm";
            if(!Ask("LƯU TÌNH TRẠNG MÁY KÉO VÀO EXCEL CỘT K\n"+
                "HĐ: "+selected.Contract+"\nCông trình: "+selected.Project+
                "\nMáy: "+selected.Material+"\nSerial: "+selected.Serial+
                "\nNgày ghi nhận: "+Rules.Day(Day(effectiveDay))+
                "\nTrạng thái mới: "+choice+
                "\n\nChỉ phiếu Đã xuất mới được ghi ngày xuất thực tế. Tiếp tục?"))return;
            var result=await _service.SetMotorWorkAsync(selected,chosenIndex==1,
                Day(effectiveDay),note.Text);
            machineRows=EquipmentListReader.Read(
                _service.Settings.TractionMachineListPath,"Máy kéo");
            var preserved=selected.Key;
            items=ReadVisibleItems();
            RefreshContracts();
            FilterMachines();
            grid.SelectedItem=(grid.ItemsSource as IEnumerable<MotorHungYenStore.Item>)
                ?.FirstOrDefault(x=>x.Key==preserved);
            if(result.Warnings.Count>0)Notify(string.Join("\n",result.Warnings.Take(8)));
        }

        Button StageTile(string label,int count,int value,string tint,bool showUnconfirmed=false)
        {
            var button=B("",()=>
            {
                if(showUnconfirmed)confirmedOnly.IsChecked=false;
                stage.SelectedIndex=value;
            });
            button.Content=Stack(T(count.ToString("N0"),22,true,Ink),
                T(label,11,true,Muted));
            button.Background=Brush(tint);
            button.BorderBrush=Brush("#D6E6E7");
            button.Padding=new Thickness(16,8,18,8);
            button.Margin=new Thickness(0,0,8,6);
            button.MinWidth=135;
            button.HorizontalContentAlignment=HorizontalAlignment.Left;
            button.ToolTip="Nhấp để lọc ngay những máy kéo ở trạng thái này";
            return button;
        }
        var filters=Row(
            StageTile("TỔNG SỐ MÁY",items.Count,0,"#EFF4F9",true),
            StageTile("CHƯA GỬI",items.Count(x=>x.State==MotorHungYenStore.Pending),1,"#F4F6F8",true),
            StageTile("ĐANG GIA CÔNG",items.Count(x=>x.State==MotorHungYenStore.Sent),2,"#FFF4DC"),
            StageTile("ĐÃ LÀM XONG",items.Count(x=>x.State==MotorHungYenStore.Completed),3,"#E3F6F0"),
            StageTile("ĐÃ BÀN GIAO",items.Count(x=>x.State==MotorHungYenStore.HandedOver),4,"#E6F1FF")
        );

        var tools=Card(Stack(
            T("MÁY KÉO GIA CÔNG TẠI HƯNG YÊN",19,true),
            T("Theo dõi từng máy bằng số serial · trạng thái xuất thực tế chỉ xác nhận từ phiếu xuất/bàn giao.",12,false,Muted),
            filters,
            Row(Field("Tìm hợp đồng, công trình, serial",query),
                Field("Lọc theo tiến độ",stage),
                B("↻ Làm mới từ Excel",()=>ShowMotorHungYen()),
                B("Bỏ lọc HĐ",()=>{selectedContractKey="";contractGrid.SelectedItem=null;FilterMachines();})),
            confirmedOnly,
            contractsSummary,
            summary,
            hints,
            Row(Field("Ngày xác nhận thực tế",effectiveDay),
                Field("Ghi chú / chứng từ",note)),
            Row(
                B("→ ĐÃ GỬI GIA CÔNG",()=>MoveTo(MotorHungYenStore.Sent),true),
                A("✓ ĐÃ LÀM XONG · GHI EXCEL",async()=>await SaveWorkChoiceAsync(1),true),
                A("○ CHƯA LÀM · GHI EXCEL",async()=>await SaveWorkChoiceAsync(0))
            ),
            new Expander{
                Header="⚙ Tùy chọn bổ sung · tình trạng cột K, lịch sử và hướng dẫn",
                IsExpanded=false,
                Content=Stack(
                    Row(Field("Tình trạng làm",workChoice),
                        A("Lưu trạng thái vào Excel cột K",
                            async()=>await SaveWorkChoiceAsync(workChoice.SelectedIndex),true),
                        B("Lịch sử dòng chọn",()=>
                        {
                            if(grid.SelectedItem is not MotorHungYenStore.Item selected)
                            {
                                Notify("Chọn máy kéo để xem lịch sử.");return;
                            }
                            ShowDialogGrid("Lịch sử máy kéo HĐ "+selected.Contract,
                                Table(store.HistoryFor(selected.Key),
                                    ("Trạng thái","Label",210),("Ngày thực tế","EffectiveDay",140),
                                    ("Ghi nhận lúc","RecordedAt",210),("Người thao tác","By",150),
                                    ("Ghi chú","Note",-1)));
                        })),
                    T("Nguồn MÁY KÉO.xlsx, cột K: Đã làm / Chưa làm hoặc ngày xuất thực tế. "+
                        "HISA đọc lại những thay đổi bạn đã lưu trong Excel. Chỉ phiếu Đã xuất mới có quyền "+
                        "ghi ngày theo serial và luôn tạo bản sao trước khi ghi.",12,false,Muted),
                    T("Lưu ý: tên HĐ xuất hiện trong file không chứng minh đã gửi gia công. "+
                        "Chọn máy theo đúng serial và chỉ bấm Đã gửi khi việc gửi đã diễn ra thực tế.",11,false,Muted)
                ),
                Margin=new Thickness(0,10,0,0)
            }
        ));
        var page=Stack(tools);
        if(_service.Settings.TractionMachineListPath.Length==0)
            page.Children.Add(Alert("Chưa chọn file Máy kéo. Mở Cấu hình nguồn dữ liệu → Danh sách thiết bị theo hợp đồng để liên kết MÁY KÉO.xlsx."));
        if(catalog.Warnings.Count>0)
            page.Children.Add(Alert("Nguồn Excel: "+string.Join(" | ",catalog.Warnings)));
        var header=new ScrollViewer
        {
            Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=390
        };

        var tables=new Grid();
        tables.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        tables.RowDefinitions.Add(new RowDefinition{Height=new GridLength(2,GridUnitType.Star)});
        tables.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        tables.RowDefinitions.Add(new RowDefinition{Height=new GridLength(3,GridUnitType.Star)});
        var contractHeading=T("1 · CHỌN HỢP ĐỒNG (BẤM DÒNG ĐỂ XEM MÁY BÊN DƯỚI)",14,true);
        tables.Children.Add(contractHeading);
        Grid.SetRow(contractGrid,1);tables.Children.Add(contractGrid);
        var machineHeading=T("2 · CHỌN MÁY THEO SERIAL → THAO TÁC Ở THANH TRÊN",14,true);
        Grid.SetRow(machineHeading,2);tables.Children.Add(machineHeading);
        Grid.SetRow(grid,3);tables.Children.Add(grid);
        RefreshContracts();
        FilterMachines();
        SetPage("Máy kéo gia công tại Hưng Yên",
            "Tìm HĐ/serial · bộ lọc tiến độ trực quan · một lần xác nhận trạng thái",
            Layout(header,tables));
    }
}
