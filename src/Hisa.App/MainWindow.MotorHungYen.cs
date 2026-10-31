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
                    // Treat operator-edited column L as the visible production
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
        var query=Input();query.Width=230;
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
            ("Chọn làm","WorkLabel",95),("Cột L (Excel)","ExcelStatusLabel",160),
            ("Ngày gửi","SentDay",100),("Ngày làm","CompletedDay",100),
            ("Ngày bàn giao","HandoverDay",110),
            ("Ghi chú","LastNote",170),("Nguồn","SourceStatus",130));
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
                workChoice.SelectedIndex=picked.ExcelDispatchDay.Length>0?-1:picked.WorkLabel=="Đã làm"?1:0;
            }
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
                "Lịch sử gửi làm lưu riêng; chưa thay đổi cột L nếu không chọn tình trạng làm."))return;
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
        var tools=Card(Stack(
            T("HỢP ĐỒNG MÁY KÉO GIA CÔNG TẠI HƯNG YÊN",18,true),
            T("Đồng bộ hai chiều cột L: đọc trực tiếp trạng thái/ngày xuất từ Excel; bấm Lưu để ghi Chưa làm / Đã làm. Ngày xuất kho chỉ tự ghi theo phiếu Đã xuất, không tự xác nhận từ ngày nhập tay.",12,false,Muted),
            T("Chỉ hợp đồng đã được người thao tác xác nhận GỬI mới vào danh sách gia công; việc xuất hiện trong MÁY KÉO.xlsx hay ghi chú DXH không có nghĩa đã gửi gia công tại Hưng Yên.",11,false,Muted),
            Row(Field("Tìm số HĐ / công trình / máy / seri",query),
                Field("Trạng thái máy",stage),
                B("Làm mới từ file Máy kéo",()=>ShowMotorHungYen())),
            contractsSummary,
            confirmedOnly,
            summary,
            selection,
            Row(Field("Ngày xác nhận thực tế",effectiveDay),
                Field("Ghi chú / chứng từ / người nhận",note)),
            Row(B("Xác nhận đã gửi gia công tại Hưng Yên",()=>MoveTo(MotorHungYenStore.Sent)),
                Field("Tình trạng gia công (chọn một)",workChoice),
                A("Lưu trạng thái vào Excel cột L",async()=>
                {
                    if(grid.SelectedItem is not MotorHungYenStore.Item selected)
                    {
                        Notify("Chọn đúng máy kéo và seri cần cập nhật.");return;
                    }
                    if(selected.State==MotorHungYenStore.HandedOver || selected.ExcelDispatchDay.Length>0)
                    {
                        Notify("Cột L đã có ngày xuất kho hoặc phiếu đã xác nhận Đã xuất; không ghi đè ngày bằng Chưa làm / Đã làm. Hãy kiểm tra phiếu và dữ liệu Excel.");return;
                    }
                    if(workChoice.SelectedIndex<0 || Day(effectiveDay).Length==0)
                    {
                        Notify("Chọn Chưa làm hoặc Đã làm và ngày xác nhận.");return;
                    }
                    var choice=workChoice.SelectedIndex==1?"Đã làm":"Chưa làm";
                    if(!Ask("CẬP NHẬT MÁY KÉO VÀ FILE EXCEL CỘT L\n"+
                        "HĐ: "+selected.Contract+"\nCông trình: "+selected.Project+
                        "\nSeri: "+selected.Serial+"\nTình trạng: "+choice+
                        "\n\nChỉ khi xác nhận phiếu Đã xuất mới đổi chữ thành ngày bàn giao."))return;
                    var result=await _service.SetMotorWorkAsync(selected,workChoice.SelectedIndex==1,
                        Day(effectiveDay),note.Text);
                    // Refresh the original L cells immediately after writing:
                    // the equipment catalog loaded when this page opened is stale.
                    machineRows=EquipmentListReader.Read(_service.Settings.TractionMachineListPath,"Máy kéo");
                    items=ReadVisibleItems();
                    RefreshContracts();FilterMachines();
                    grid.SelectedItem=(grid.ItemsSource as IEnumerable<MotorHungYenStore.Item>)
                        ?.FirstOrDefault(x=>x.Key==selected.Key);
                    if(result.Warnings.Count>0)Notify(string.Join("\n",result.Warnings));
                },true),
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
            T("Cột L trong MÁY KÉO.xlsx là nguồn trạng thái hai chiều. Sửa trực tiếp Excel rồi bấm Làm mới để đọc lại; chọn Chưa làm / Đã làm tại HISA để ghi Excel. Ngày xuất kho có sẵn được giữ nguyên; chỉ phiếu Đã xuất mới cập nhật ngày tự động. HISA sao lưu trước khi ghi.",11,false,Muted)
        ));
        var page=Stack(tools);
        if(_service.Settings.TractionMachineListPath.Length==0)
            page.Children.Add(Alert("Chưa chọn file Máy kéo. Mở Cấu hình nguồn dữ liệu → Danh sách thiết bị theo hợp đồng để liên kết MÁY KÉO.xlsx."));
        if(catalog.Warnings.Count>0)
            page.Children.Add(Alert("Nguồn Excel: "+string.Join(" | ",catalog.Warnings)));
        var header=new ScrollViewer
        {
            Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=335
        };

        var tables=new Grid();
        tables.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        tables.RowDefinitions.Add(new RowDefinition{Height=new GridLength(2,GridUnitType.Star)});
        tables.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        tables.RowDefinitions.Add(new RowDefinition{Height=new GridLength(3,GridUnitType.Star)});
        var contractHeading=T("1 · HỢP ĐỒNG MÁY KÉO GIA CÔNG TẠI HƯNG YÊN",14,true);
        tables.Children.Add(contractHeading);
        Grid.SetRow(contractGrid,1);tables.Children.Add(contractGrid);
        var machineHeading=T("2 · CHI TIẾT MÁY KÉO THEO HỢP ĐỒNG (CHỌN MÁY ĐỂ XÁC NHẬN)",14,true);
        Grid.SetRow(machineHeading,2);tables.Children.Add(machineHeading);
        Grid.SetRow(grid,3);tables.Children.Add(grid);
        RefreshContracts();
        FilterMachines();
        SetPage("Máy kéo gia công tại Hưng Yên",
            "Xem hợp đồng đã gửi gia công → đã làm xong → đã bàn giao · theo từng HĐ và seri",
            Layout(header,tables));
    }
}
