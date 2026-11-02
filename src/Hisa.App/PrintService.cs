using System.Globalization;
using System.Printing;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace Hisa.App;

/// <summary>Native fixed-page printing. Every row is measured, paginated and repeated headers are retained.</summary>
public static class PrintService
{
    public static void PreviewSlip(Window owner, Slip slip, PrintTemplate template) =>
        Preview(owner, BuildSlipDocument(slip,template),
            slip.SlipCode.Length>0?slip.SlipCode:"Phiếu bàn giao",
            template, selected=>BuildSlipDocument(slip,selected));
    public static void PreviewReport(Window owner, ContractReport report) => Preview(owner, BuildReportDocument(report), "Báo cáo hợp đồng và vật tư");
    public static void PreviewDaily(Window owner, DailyReport report) => Preview(owner, BuildDailyDocument(report), "Báo cáo xuất hàng " + Rules.Day(report.Date));

    public static FixedDocument BuildSlipDocument(Slip slip, PrintTemplate template)
    {
        var p = new PageWriter(template, template.Title, $"{template.CodeLabel}: {slip.SlipCode}  ·  {template.DateLabel}: {Rules.Day(slip.HandoverDate)}");
        var lines = slip.Lines.Where(x => x.Include).ToList();
        var contractOnly = (slip.ContractOnly??[])
            .Where(x=>!string.IsNullOrWhiteSpace(x.Contract))
            .DistinctBy(x=>x.Identity).ToList();
        if (template.Layout == "contract_delivery")
        {
            var groups = lines.GroupBy(x => Rules.Identity(x.Contract, x.Project, x.Company)).ToList();
            if (groups.Count == 0)
            {
                Header(p, template, slip);
                if(contractOnly.Count==0)
                    p.Paragraph("Phiếu chưa có dòng vật tư. Hãy tải dữ liệu hoặc bổ sung danh mục trước khi giao hàng.");
                else
                    ContractOnlySection(p,template,contractOnly);
                Signatures(p, template, slip);
            }
            for (var index = 0; index < groups.Count; index++)
            {
                if (index > 0) p.BreakPage();
                var group = groups[index].ToList();
                var first = group[0];
                Header(p, template, slip);
                var legacyOpening=Rules.Code(template.OpeningText)=="HOMNAYNGAY";
                p.Paragraph($"{(legacyOpening?template.OpeningText:"Hôm nay, ngày")} {Rules.Day(slip.HandoverDate)}, tại {template.Location}:", italic: true, align: template.MetaAlign);
                p.Paragraph(Numbered(1,legacyOpening?"Xuất theo danh mục vật tư (List hàng)":template.OpeningText), bold: true);
                p.Paragraph($"- {template.ProjectLabel}: {first.Project}", align: template.MetaAlign);
                p.Paragraph($"- {template.CompanyLabel}: {first.Company}", align: template.MetaAlign);
                p.Paragraph($"- {template.ContractLabel}: {first.Contract}", align: template.MetaAlign);
                if (!string.IsNullOrWhiteSpace(slip.Note)) p.Paragraph("Ghi chú phiếu: " + slip.Note);
                p.Paragraph(Numbered(2,template.SupplementaryLabel), bold: true);
                var headers = new[] { "STT", template.MaterialLabel, template.UnitLabel, $"{template.QuantityLabel}\n{template.AccountingQtyLabel}", $"{template.QuantityLabel}\n{template.ActualQtyLabel}", template.NotesLabel };
                double[] weights=[0.055,0.455,0.08,0.115,0.115,0.18];string[] alignment=["center",template.TableAlign,"center","right","right",template.TableAlign];
                var visible=Enumerable.Range(0,6).Where(index=>(index!=2||template.ShowUnit)&&(index is not (3 or 4)||template.ShowQuantity)).ToArray();
                p.BeginTable(visible.Select(i=>headers[i]).ToArray(),visible.Select(i=>weights[i]).ToArray(),visible.Select(i=>alignment[i]).ToArray());
                var number = 0;
                foreach (var item in group)
                {
                    // Accounting quantity is the planned/list quantity.
                    // Actual quantity may only be asserted after the slip has
                    // been explicitly confirmed as delivered.
                    string[] cells=[(++number).ToString(),item.Material,item.Unit,
                        Qty(item.Quantity),slip.Status=="delivered"?Qty(item.Quantity):"",
                        MaterialNote(item,template,slip)];
                    p.Row(visible.Select(i=>cells[i]).ToArray());
                }
                while (number < Math.Clamp(template.MinRows, 0, 100)) {var cells=visible.Select(_=>"").ToArray();cells[0]=(++number).ToString();p.Row(cells);}
                p.EndTable();
                Signatures(p, template, slip);
            }
            if(groups.Count>0 && contractOnly.Count>0)
            {
                p.BreakPage();
                Header(p,template,slip);
                p.Paragraph($"{template.DateLabel}: {Rules.Day(slip.HandoverDate)}      {template.CodeLabel}: {slip.SlipCode}",align:template.MetaAlign);
                ContractOnlySection(p,template,contractOnly);
                Signatures(p,template,slip);
            }
        }
        else
        {
            Header(p, template, slip);
            p.Paragraph($"{template.DateLabel}: {Rules.Day(slip.HandoverDate)}      {template.CodeLabel}: {slip.SlipCode}", align: template.MetaAlign);
            if (!string.IsNullOrWhiteSpace(slip.Name)) p.Paragraph(slip.Name, bold: true);
            // The template editor exposes these fields for every print layout,
            // so no layout may silently suppress the configured opening text.
            if (!string.IsNullOrWhiteSpace(template.OpeningText))
                p.Paragraph(template.OpeningText,align:template.MetaAlign);
            if (!string.IsNullOrWhiteSpace(slip.Note)) p.Paragraph("Ghi chú phiếu: " + slip.Note);
            if(!string.IsNullOrWhiteSpace(template.SupplementaryLabel))
                p.Paragraph(template.SupplementaryLabel,bold:true);
            string[] headers=["STT",template.ProjectLabel,template.CompanyLabel,template.ContractLabel,template.MaterialLabel,template.UnitLabel,template.QuantityLabel];
            double[] weights=[0.045,0.27,0.12,0.11,0.325,0.055,0.075];string[] alignment=["center",template.TableAlign,template.TableAlign,"center",template.TableAlign,"center","right"];
            var visible=Enumerable.Range(0,7).Where(index=>(index!=5||template.ShowUnit)&&(index!=6||template.ShowQuantity)).ToArray();
            p.BeginTable(visible.Select(i=>headers[i]).ToArray(),visible.Select(i=>weights[i]).ToArray(),visible.Select(i=>alignment[i]).ToArray());
            var count = 0;
            var categoryOrder = Rules.Categories.Concat(lines.Select(x=>x.Group)).Distinct().ToList();
            var ordered = template.ShowCategory ? categoryOrder.SelectMany(c=>lines.Where(x=>x.Group==c)).ToList() : lines;
            var previous = "";
            foreach (var item in ordered)
            {
                if (template.ShowCategory && item.Group != previous)
                {
                    previous = item.Group;
                    p.GroupRow($"{template.CategoryPrefix} {item.Group.ToUpper(CultureInfo.GetCultureInfo("vi-VN"))} {template.CategorySuffix}".Trim());
                }
                var label = item.Material;
                var note = MaterialNote(item, template,slip);
                if (!string.IsNullOrWhiteSpace(note)) label += "\n" + note;
                string[] cells=[(++count).ToString(),item.Project,item.Company,item.Contract,label,item.Unit,Qty(item.Quantity)];
                p.Row(visible.Select(i=>cells[i]).ToArray());
            }
            if (lines.Count == 0) p.GroupRow("Chưa có danh sách vật tư bàn giao.");
            p.EndTable();
            if(contractOnly.Count>0) ContractOnlySection(p,template,contractOnly);
            Signatures(p, template, slip);
        }
        return p.Finish();
    }

    public static FixedDocument BuildReportDocument(ContractReport report)
    {
        var template = ReportTemplate();
        var p = new PageWriter(template, report.Title, "Lập lúc " + report.GeneratedAt.ToString("dd/MM/yyyy HH:mm"));
        p.Title(report.Title);
        WriteContractReport(p, report);
        return p.Finish();
    }

    public static FixedDocument BuildDailyDocument(DailyReport report)
    {
        var p = new PageWriter(ReportTemplate(), "BÁO CÁO TỔNG HỢP XUẤT HÀNG THEO NGÀY", "Ngày " + Rules.Day(report.Date));
        p.Title("BÁO CÁO TỔNG HỢP XUẤT HÀNG");
        p.Paragraph("Ngày kế hoạch / xuất hàng: " + Rules.Day(report.Date), bold: true, align: "center");
        p.Paragraph("Trạng thái là trạng thái hiện tại của các phiếu thuộc ngày đã chọn. Phiếu hoãn còn chờ được theo dõi riêng.");
        p.BeginTable(["Tổng phiếu", "Chờ xuất", "Hoãn chờ xuất", "Đã xuất", "Đã hủy"], [0.2,0.2,0.2,0.2,0.2]);
        p.Row([report.Slips.Count.ToString(), Count("planned"), Count("delayed"), Count("delivered"), Count("cancelled")]);
        p.EndTable();
        p.Section("1. CƠ CẤU VẬT TƯ TRONG PHIẾU NGÀY");
        Categories(p, report.Categories);
        foreach (var status in new[] { "planned", "delayed", "delivered", "cancelled" })
        {
            var selected = report.Slips.Where(x => NormalizeStatus(x.Status) == status).ToList();
            p.Section($"{Rules.StatusLabel(status).ToUpper(CultureInfo.GetCultureInfo("vi-VN"))} · {selected.Count} phiếu");
            if (selected.Count == 0) { p.Paragraph("Không có phiếu."); continue; }
            foreach (var slip in selected)
            {
                p.Paragraph($"{slip.SlipCode} · {slip.Name} · {slip.DateLabel}", bold: true);
                p.Paragraph("Hợp đồng: " + slip.ContractLabel + (slip.DelayReason.Length>0?" · Lý do hoãn: "+slip.DelayReason:"") + (slip.CancelReason.Length>0?" · Lý do hủy: "+slip.CancelReason:""));
                MaterialTable(p, slip.Lines.Where(x=>x.Include).Select(line=>{var copy=line.Copy();copy.Note=MotorSerials.NoteWithSerials(slip,line);return copy;}).ToList());
            }
        }
        p.Section($"2. PHIẾU HOÃN CÒN CHỜ ĐẾN NGÀY CHỌN · {report.OutstandingDelayed.Count} phiếu");
        p.BeginTable(["Mã phiếu", "Ngày kế hoạch", "Số hợp đồng", "Tên phiếu", "Lý do hoãn"], [0.19,0.12,0.2,0.25,0.24]);
        foreach (var slip in report.OutstandingDelayed) p.Row([slip.SlipCode,slip.DateLabel,slip.ContractLabel,slip.Name,slip.DelayReason]);
        if (report.OutstandingDelayed.Count==0) p.GroupRow("Không có phiếu hoãn còn chờ.");
        p.EndTable();
        p.Section($"3. XUẤT HÀNG THEO SỔ CHUẨN / LỊCH SỬ · {report.LegacyDeliveries.Count} dòng");
        p.BeginTable(["Hợp đồng", "Công trình", "Công ty", "Ngày xuất", "Nguồn / ghi chú"], [0.13,0.32,0.16,0.12,0.27]);
        foreach (var entry in report.LegacyDeliveries) p.Row([entry.Contract,entry.Project,entry.Company,Rules.Day(entry.Date),entry.Source+(entry.Note.Length>0?"\n"+entry.Note:"")]);
        if (report.LegacyDeliveries.Count==0) p.GroupRow("Không có bản ghi xuất hàng bổ sung trong ngày.");
        p.EndTable();
        p.Section("4. CHI TIẾT HỢP ĐỒNG VÀ ĐỐI CHIẾU DỮ LIỆU");
        WriteContractReport(p, report.Contracts);
        return p.Finish();

        string Count(string status) => report.StatusCounts.TryGetValue(status,out var value) ? value.ToString() : report.Slips.Count(x=>NormalizeStatus(x.Status)==status).ToString();
    }

    private static string NormalizeStatus(string value) => value is "draft" or "" ? "planned" : value;
    private static string Numbered(int number,string text)=>System.Text.RegularExpressions.Regex.IsMatch(text,@"^\s*\d+\s*[.)]")?text:number+". "+text;
    public static void ValidateTemplate(PrintTemplate template)
    {
        // Include a real table row and signature block: individually valid margins and
        // fonts can still leave no printable space on a small paper size.
        BuildSlipDocument(new Slip{SlipCode="KIEM-TRA-MAU",Name="Kiểm tra mẫu",Contracts=["00000000"],Lines=[new MaterialLine{Contract="00000000",Project="Công trình",Company="Đơn vị",Material="Vật tư",Unit="Bộ",Quantity=1}]},template);
    }
    private static string Qty(decimal? value) => value?.ToString("#,0.00##########################",CultureInfo.GetCultureInfo("vi-VN")) ?? "";
    private static string MaterialNote(MaterialLine item, PrintTemplate template,Slip? slip=null) => string.Join(" · ",new[]{slip==null?item.Note:MotorSerials.NoteWithSerials(slip,item),template.ShowSource ? $"Kiện {item.PackageNo} · {item.SourceFile}" : ""}.Where(x=>!string.IsNullOrWhiteSpace(x)));
    private static PrintTemplate ReportTemplate() => new(){ Title="BÁO CÁO KHO HƯNG YÊN", FontFamily="Arial",FontSize=9,TableFontSize=8,TitleFontSize=16,Orientation="landscape",MarginTop=10,MarginBottom=10,MarginLeft=10,MarginRight=10 };
    private static void Header(PageWriter p, PrintTemplate t, Slip slip)
    {
        if (t.Company.Length>0) p.Paragraph(t.Company,bold:true,align:t.CompanyAlign);
        if (t.Branch.Length>0) p.Paragraph(t.Branch,align:t.CompanyAlign);
        p.Title(t.Title,t.TitleAlign);
        if(t.Subtitle.Length>0) p.Paragraph(t.Subtitle,italic:true,align:t.TitleAlign);
        if(t.Layout=="contract_delivery") p.Paragraph($"{t.CodeLabel}: {slip.SlipCode}",align:"right");
    }
    private static void ContractOnlySection(PageWriter p,PrintTemplate template,
        IReadOnlyList<ContractRow> contracts)
    {
        p.Paragraph("HỢP ĐỒNG ĐƯỢC BÀN GIAO / CHỜ ĐỐI CHIẾU VẬT TƯ",bold:true);
        p.Paragraph("Các hợp đồng sau không có dòng vật tư chi tiết trên phiếu. "+
            "Thông tin hợp đồng được ghi nhận, không tự suy diễn vật tư hoặc số lượng.",italic:true);
        p.BeginTable([template.ContractLabel,template.ProjectLabel,template.CompanyLabel,
            template.NotesLabel],[0.16,0.34,0.22,0.28]);
        foreach(var row in contracts)
            p.Row([row.Contract,row.Project,row.Company,
                string.IsNullOrWhiteSpace(row.Warning)?"Chưa có danh mục vật tư":row.Warning]);
        p.EndTable();
    }
    private static void Signatures(PageWriter p, PrintTemplate t, Slip slip)
    {
        if(t.Note.Length>0) p.Paragraph(t.Note);
        var signatures=t.Signatures.Count>0?t.Signatures:new List<string>{"BÊN NHẬN","BÊN BÀN GIAO","THỦ KHO"};
        p.Signatures(signatures,t.SignatureCaption,t.SignatureAlign,$"{t.Location}, ngày {Rules.Day(slip.HandoverDate)}");
        if(t.FooterText.Length>0) p.Paragraph(t.FooterText,italic:true);
    }
    private static void WriteContractReport(PageWriter p, ContractReport report)
    {
        p.Paragraph(report.Summary,bold:true);
        p.Paragraph($"Đã nhập {report.RequestedCount:N0} mã · Đã lên phiếu: {report.WithSlips:N0} HĐ · Có phiếu hủy: {report.CancelledCount:N0} HĐ.");
        if(report.Filter.ArrivalDate.Length>0) p.Paragraph("Ngày cont về: "+Rules.Day(report.Filter.ArrivalDate));
        if(report.Filter.ShipDate.Length>0) p.Paragraph("Ngày xuất: "+Rules.Day(report.Filter.ShipDate));
        if(report.Filter.ContractsText.Length>0) p.Paragraph("Mã yêu cầu: "+string.Join(", ",Rules.Tokens(report.Filter.ContractsText)));
        if(report.Filter.Categories.Count>0 || report.Filter.KeywordsText.Length>0) p.Paragraph("Bộ lọc vật tư: "+string.Join(", ",report.Filter.Categories)+" "+report.Filter.KeywordsText);
        p.Paragraph("Số HĐ đếm theo mã chuẩn hóa. Công trình trùng mã được giữ riêng. Một HĐ có thể nằm trong nhiều nhóm hoặc nhiều trạng thái; không cộng các nhóm để ra tổng.",italic:true);
        if(!report.DataAvailable) p.Paragraph("CHƯA ĐỐI CHIẾU ĐƯỢC NGUỒN: cần đọc xong Tổng hợp.xlsx để xác nhận mã thiếu.",bold:true);
        foreach(var warning in report.Warnings) p.Paragraph("Cảnh báo: "+warning,bold:true);
        p.Section("HỢP ĐỒNG CÓ TỪNG NHÓM VẬT TƯ");
        Categories(p,report.Categories);
        foreach(var category in report.Categories.Where(x=>x.Contracts.Count>0))
        {
            p.Paragraph($"{category.Category}: {category.ContractCount} HĐ · {category.ProjectCount} công trình",bold:true);
            ContractList(p,category.Contracts);
        }
        p.Section("TRẠNG THÁI VÀ LỊCH SỬ PHIẾU");
        foreach(var pair in report.StatusDetails)
        {
            var title=pair.Key switch { "withSlips"=>"Đã lên phiếu", "cancelPickup"=>"Hủy lấy hàng", _=>Rules.StatusLabel(pair.Key) };
            p.Paragraph($"{title}: {pair.Value.Select(x=>Rules.Contract(x.Contract)).Distinct().Count()} HĐ · {pair.Value.Count} công trình",bold:true);
            if(pair.Value.Count>0) ContractList(p,pair.Value); else p.Paragraph("Không có.");
        }
        WriteProblem("MÃ CHƯA CÓ DỮ LIỆU KIỆN HƯNG YÊN",report.Missing);
        WriteProblem("CÓ DỮ LIỆU NHƯNG KHÔNG KHỚP NGÀY CHỌN",report.DateMismatch);
        WriteProblem("CÓ KIỆN NHƯNG KHÔNG CÓ VẬT TƯ KHỚP BỘ LỌC",report.NoMaterialMatch);
        p.Section("DANH SÁCH VẬT TƯ THEO HỢP ĐỒNG / CÔNG TRÌNH");
        foreach(var row in report.Rows)
        {
            p.Paragraph($"HĐ {row.Contract} · {row.Project} · {row.Company}",bold:true);
            p.Paragraph($"{row.PackageCount} kiện · Ngày cont về: {row.ArrivalDates} · {row.StatusLabel}");
            foreach(var package in row.Packages) p.Paragraph($"Kiện {package.PackageNo}: {package.ItemCount} dòng · Cont về {package.ArrivalLabel} · {package.SourceFile}");
            MaterialTable(p,row.Materials);
        }
        if(report.Rows.Count==0) p.Paragraph("Không có dòng vật tư phù hợp.");
        void WriteProblem(string title,List<ContractRow> rows)
        {
            p.Section($"{title} · {rows.Count} công trình/mã");
            if(rows.Count>0) ContractList(p,rows); else p.Paragraph("Không có.");
        }
    }
    private static void Categories(PageWriter p,List<CategoryStat> categories)
    {
        p.BeginTable(["Nhóm vật tư","Số HĐ","Công trình","Dòng vật tư","Số lượng theo ĐVT"],[0.29,0.1,0.11,0.12,0.38]);
        foreach(var category in categories) p.Row([category.Category,category.ContractCount.ToString(),category.ProjectCount.ToString(),category.LineCount.ToString(),category.QuantityLabel]);
        if(categories.Count==0) p.GroupRow("Không có dữ liệu vật tư.");
        p.EndTable();
    }
    private static void ContractList(PageWriter p,List<ContractRow> rows)
    {
        p.BeginTable(["Số HĐ","Công trình","Công ty","Chi tiết trạng thái / cảnh báo"],[0.12,0.31,0.16,0.41]);
        foreach(var row in rows)
        {
            var detail=new List<string>();
            if(row.Warning.Length>0) detail.Add(row.Warning);
            foreach(var slip in row.Slips) detail.Add($"{slip.SlipCode} · {slip.DateLabel} · {slip.StatusLabel}"+(slip.DelayReason.Length>0?" · "+slip.DelayReason:"")+(slip.CancelReason.Length>0?" · "+slip.CancelReason:""));
            foreach(var flag in row.Flags) detail.Add($"{flag.StatusLabel}: {flag.Note}");
            foreach(var ship in row.Shipments) detail.Add($"Xuất {Rules.Day(ship.Date)} · {ship.Source} · {ship.SlipCode}");
            p.Row([row.Contract,row.Project,row.Company,string.Join("\n",detail)]);
        }
        p.EndTable();
    }
    private static void MaterialTable(PageWriter p,List<MaterialLine> lines)
    {
        p.BeginTable(["STT","Hợp đồng","Công trình","Tên vật tư","ĐVT","Số lượng","Ghi chú"],[0.045,0.105,0.23,0.35,0.06,0.08,0.13],["center","center","left","left","center","right","left"]);
        var number=0;
        foreach(var line in lines) p.Row([(++number).ToString(),line.Contract,line.Project,line.Material,line.Unit,Qty(line.Quantity),line.Note]);
        if(lines.Count==0) p.GroupRow("Không có dòng vật tư.");
        p.EndTable();
    }

    // Preview and printer consume the EXACT same FixedDocument paginator.
    // Paper changes rebuild the document before preview, never after printing.
    private static void Preview(Window owner,FixedDocument original,string title,
        PrintTemplate? sourceTemplate=null,Func<PrintTemplate,FixedDocument>? rebuild=null)
    {
        var window=new Window
        {
            Title="Xem trước khi in · "+title,Width=1180,Height=850,
            MinWidth=760,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Owner=owner,Background=new SolidColorBrush(Color.FromRgb(235,241,247))
        };
        var document=original;
        var working=sourceTemplate==null?null:
            JsonSerializer.Deserialize<PrintTemplate>(JsonSerializer.Serialize(sourceTemplate));
        var root=new DockPanel();
        var bar=new Border{Background=Brushes.White,Padding=new Thickness(18,12,18,12)};
        var controls=new DockPanel();
        var close=new Button
        {
            Content="Đóng",Padding=new Thickness(18,9,18,9),Margin=new Thickness(10,0,0,0)
        };
        close.Click+=(_,_)=>window.Close();
        DockPanel.SetDock(close,Dock.Right);controls.Children.Add(close);
        var print=new Button
        {
            Content="In đúng bản xem trước / PDF",Padding=new Thickness(18,9,18,9),
            Background=new SolidColorBrush(Color.FromRgb(13,116,132)),
            Foreground=Brushes.White,BorderThickness=new Thickness(0)
        };
        DockPanel.SetDock(print,Dock.Right);controls.Children.Add(print);
        var status=new TextBlock
        {
            Text=title+" · "+document.Pages.Count+" trang",VerticalAlignment=VerticalAlignment.Center,
            FontWeight=FontWeights.SemiBold,FontSize=13,
            Foreground=new SolidColorBrush(Color.FromRgb(25,48,70))
        };
        controls.Children.Add(status);
        bar.Child=controls;DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);

        var hint=new TextBlock
        {
            Text="Chọn khổ giấy và chiều giấy để tự dàn lại toàn bộ nội dung trước khi in. "+
                 "Bản in dùng đúng tài liệu đang hiển thị. Nếu máy in không hỗ trợ "+
                 "khổ hoặc vùng in, HISA sẽ cảnh báo thay vì tự thu nhỏ/cắt mất chữ.",
            Margin=new Thickness(20,8,20,8),Foreground=Brushes.DimGray,
            TextWrapping=TextWrapping.Wrap
        };
        DockPanel.SetDock(hint,Dock.Bottom);root.Children.Add(hint);
        var viewer=new DocumentViewer
        {
            Document=document,Background=new SolidColorBrush(Color.FromRgb(222,230,238))
        };
        if(rebuild!=null && working!=null)
        {
            var choices=new StackPanel
            {
                Orientation=Orientation.Horizontal,Margin=new Thickness(18,0,18,9)
            };
            choices.Children.Add(new TextBlock
            {
                Text="Khổ giấy:",VerticalAlignment=VerticalAlignment.Center,
                Margin=new Thickness(0,0,8,0)
            });
            var paper=new ComboBox
            {
                Width=155,ItemsSource=PrintPaperSizes.Options.Select(x=>
                    new KeyValuePair<string,string>(x.Key,x.Label)).ToList(),
                DisplayMemberPath="Value",SelectedValuePath="Key",
                SelectedValue=working.PaperSize
            };
            choices.Children.Add(paper);
            choices.Children.Add(new TextBlock
            {
                Text="Chiều giấy:",VerticalAlignment=VerticalAlignment.Center,
                Margin=new Thickness(18,0,8,0)
            });
            var orientation=new ComboBox
            {
                Width=95,ItemsSource=new[]
                {
                    new KeyValuePair<string,string>("landscape","Ngang"),
                    new KeyValuePair<string,string>("portrait","Dọc")
                },DisplayMemberPath="Value",SelectedValuePath="Key",
                SelectedValue=working.Orientation
            };
            choices.Children.Add(orientation);
            void Relayout()
            {
                var selectedPaper=paper.SelectedValue?.ToString();
                var selectedOrientation=orientation.SelectedValue?.ToString();
                if(selectedPaper==null||selectedOrientation==null)return;
                var previousPaper=working.PaperSize;
                var previousOrientation=working.Orientation;
                try
                {
                    working.PaperSize=selectedPaper;
                    working.Orientation=selectedOrientation;
                    var updated=rebuild(working);
                    document=updated;
                    viewer.Document=updated;
                    status.Text=title+" · "+updated.Pages.Count+" trang · "+
                        selectedPaper+" · "+(selectedOrientation=="landscape"?"Ngang":"Dọc");
                }
                catch(Exception ex)
                {
                    working.PaperSize=previousPaper;
                    working.Orientation=previousOrientation;
                    // Do not print an unpreviewed or partially rendered document.
                    MessageBox.Show(window,"Không dàn được giấy vừa chọn: "+ex.Message,
                        "Kiểm tra bản in",MessageBoxButton.OK,MessageBoxImage.Warning);
                }
            }
            paper.SelectionChanged+=(_,_)=>Relayout();
            orientation.SelectionChanged+=(_,_)=>Relayout();
            DockPanel.SetDock(choices,Dock.Top);root.Children.Add(choices);
            status.Text=title+" · "+document.Pages.Count+" trang · "+
                working.PaperSize+" · "+
                (working.Orientation=="landscape"?"Ngang":"Dọc");
        }
        root.Children.Add(viewer);
        print.Click+=(_,_) =>
        {
            try
            {
                var dialog=new PrintDialog();
                var size=document.DocumentPaginator.PageSize;
                var landscape=size.Width>size.Height;
                dialog.PrintTicket.PageOrientation=landscape
                    ?PageOrientation.Landscape:PageOrientation.Portrait;
                // Use the named A4 size for printer drivers instead of a
                // custom size; preview dimensions still come from our pages.
                dialog.PrintTicket.PageMediaSize=working?.PaperSize=="A4"
                    ?new PageMediaSize(PageMediaSizeName.ISOA4)
                    :new PageMediaSize(Math.Min(size.Width,size.Height),
                        Math.Max(size.Width,size.Height));
                if(dialog.ShowDialog()!=true)return;
                var mismatch=CheckPrinterGeometry(document,dialog);
                if(mismatch!=null)
                {
                    MessageBox.Show(window,mismatch,
                        "Máy in khác bản xem trước",MessageBoxButton.OK,MessageBoxImage.Warning);
                    return;
                }
                dialog.PrintDocument(document.DocumentPaginator,title);
            }
            catch(Exception ex)
            {
                MessageBox.Show(window,"Chưa thể in: "+ex.Message,
                    "In tài liệu",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
        };
        window.Content=root;window.ShowDialog();
    }

    /// <summary>
    /// Reject a printer ticket that would scale, crop or change the paper.
    /// This keeps the printed visual identical to the already shown preview.
    /// </summary>
    private static string? CheckPrinterGeometry(FixedDocument document,PrintDialog dialog)
    {
        var size=document.DocumentPaginator.PageSize;
        var ticket=dialog.PrintTicket;
        if(ticket.PageMediaSize?.Width is double pw &&
           ticket.PageMediaSize?.Height is double ph &&
           (Math.Abs(Math.Min(pw,ph)-Math.Min(size.Width,size.Height))>3 ||
            Math.Abs(Math.Max(pw,ph)-Math.Max(size.Width,size.Height))>3))
            return "Khổ giấy máy in khác bản xem trước. Hãy chọn đúng khổ giấy "+
                "ở bản xem trước và trong cài đặt máy in, sau đó thử lại.";
        if(ticket.PageOrientation is PageOrientation.Landscape &&
           size.Width<size.Height ||
           ticket.PageOrientation is PageOrientation.Portrait &&
           size.Width>size.Height)
            return "Chiều giấy của máy in khác bản xem trước. "+
                "Chọn cùng chiều Ngang/Dọc ở cả hai nơi.";
        // Not all printer drivers expose their nonprintable margins.
        // Validate when available; don't pretend a cropped print will match.
        var area=dialog.PrintQueue?.GetPrintCapabilities(ticket)?.PageImageableArea;
        if(area!=null &&
           area.ExtentWidth>0&&area.ExtentHeight>0 &&
           area.ExtentWidth<=size.Width+2&&area.ExtentHeight<=size.Height+2 &&
           !ContentFitsPrintArea(document,new Rect(area.OriginWidth,area.OriginHeight,
               area.ExtentWidth,area.ExtentHeight)))
            return "Vùng in thực tế của máy in nhỏ hơn nội dung A4 đang xem. "+
                "Hãy tăng lề trong mẫu in hoặc chọn máy in hỗ trợ khổ/vùng in này. "+
                "HISA chưa gửi lệnh in để tránh mất chữ.";
        return null;
    }

    /// <summary>Tests inspect the same fixed-page content bounds sent to print.</summary>
    internal static bool ContentFitsPrintArea(FixedDocument document,Rect printableArea)
    {
        const double tolerance=1.5;
        foreach(var item in document.Pages)
        {
            if(item.Child is not FixedPage page)continue;
            foreach(var canvas in page.Children.OfType<Canvas>())
            foreach(var child in canvas.Children.OfType<FrameworkElement>())
            {
                var rect=new Rect(Canvas.GetLeft(child),Canvas.GetTop(child),
                    child.ActualWidth,child.ActualHeight);
                if(rect.Left<printableArea.Left-tolerance||
                   rect.Top<printableArea.Top-tolerance||
                   rect.Right>printableArea.Right+tolerance||
                   rect.Bottom>printableArea.Bottom+tolerance)return false;
            }
        }
        return true;
    }

    /// <summary>Measured fixed-page writer. No overflow clipping: oversized rows are split at wrapped text lines.</summary>
    private sealed class PageWriter
    {
        private readonly FixedDocument document=new();
        private readonly PrintTemplate template;
        private readonly double width,height,left,right,top,bottom,font;
        private double tableFont;
        private double pad=5;
        private readonly string title,context;
        private readonly List<TextBlock> footers=[];
        private Canvas canvas=null!;
        private double y;
        private string[]? tableHeaders;
        private double[] columnWidths=[];
        private string[] aligns=[];
        private readonly FontFamily family;
        
        private double ContentWidth=>width-left-right;
        private double FooterHeight=>font*.72*1.24+2;
        private double Limit=>height-bottom-FooterHeight-8;
        private static ArgumentException LayoutError()=>new("Khổ giấy, lề hoặc cỡ chữ hiện tại không đủ chỗ in nội dung. Hãy giảm lề/cỡ chữ hoặc chọn khổ giấy lớn hơn.");
        public PageWriter(PrintTemplate template,string title,string context)
        {
            this.template=template;this.title=title;this.context=context;
            var (paperWidthMm,paperHeightMm)=PrintPaperSizes.GetMillimeters(template.PaperSize);
            var pw=paperWidthMm*96/25.4;var ph=paperHeightMm*96/25.4;
            if(template.Orientation is not ("landscape" or "portrait"))
                throw new ArgumentException("Hướng giấy không hợp lệ. Chọn Ngang hoặc Dọc.");
            (width,height)=template.Orientation=="landscape"?(ph,pw):(pw,ph);
            left=Math.Clamp(template.MarginLeft,3,40)*96/25.4;right=Math.Clamp(template.MarginRight,3,40)*96/25.4;top=Math.Clamp(template.MarginTop,3,40)*96/25.4;bottom=Math.Clamp(template.MarginBottom,3,40)*96/25.4;
            font=Math.Clamp(template.FontSize,6,24)*96/72;tableFont=Math.Clamp(template.TableFontSize,6,20)*96/72;
            family=new FontFamily(string.IsNullOrWhiteSpace(template.FontFamily)?"Arial":template.FontFamily);
            document.DocumentPaginator.PageSize=new Size(width,height);NewPage();
        }
        private void NewPage()
        {
            var page=new FixedPage{Width=width,Height=height,Background=Brushes.White};
            canvas=new Canvas{Width=width,Height=height};page.Children.Add(canvas);
            var content=new PageContent();((IAddChild)content).AddChild(page);document.Pages.Add(content);
            y=top;
            // The old single-line heading used CharacterEllipsis, silently
            // dropping long slip titles and contract/context text on paper.
            // Measure and wrap both headings so each printed page shows them.
            var header=Text(title,ContentWidth,font*.84,true,"left");
            var meta=Text(context,ContentWidth,font*.78,false,"left");
            if(y+header.DesiredSize.Height+meta.DesiredSize.Height+10>Limit)
                throw LayoutError();
            Place(header,left,y);y+=header.DesiredSize.Height+3;
            Place(meta,left,y);y+=meta.DesiredSize.Height+7;
            var footer=SingleLine("",font*.72,false,"right");Place(footer,left,height-bottom-FooterHeight);footers.Add(footer);
            if(y+font*1.24>Limit)throw LayoutError();
            if(tableHeaders is not null)
            {
                if(y+HeaderHeight()+tableFont*1.24+pad*2>Limit)throw LayoutError();
                DrawHeader();
            }
        }
        public void BreakPage(){tableHeaders=null;NewPage();}
        private void Ensure(double rowHeight){if(y+rowHeight>Limit)NewPage();if(y+rowHeight>Limit+.01)throw LayoutError();}
        public void Title(string text,string align="center")=>Paragraph(text,true,false,align,Math.Clamp(template.TitleFontSize,10,32)*96/72,10);
        public void Section(string text){EndTable();Paragraph(text,true,false,"left",font*1.08,7);}
        public void Paragraph(string text,bool bold=false,bool italic=false,string align="left",double? size=null,double after=5)
        {
            var fs=size??font;var lines=Wrap(text,ContentWidth,fs,bold);var lineHeight=fs*1.24;
            for(var offset=0;offset<lines.Count;)
            {
                var maxLines=(int)((Limit-y-after)/lineHeight);
                if(maxLines<1){NewPage();maxLines=(int)((Limit-y-after)/lineHeight);}
                if(maxLines<1)throw LayoutError();
                var take=Math.Min(maxLines,lines.Count-offset);
                var chunk=string.Join("\n",lines.Skip(offset).Take(take));
                var item=Text(chunk,ContentWidth,fs,bold,align,italic);Ensure(item.DesiredSize.Height+after);Place(item,left,y);y+=item.DesiredSize.Height+after;
                offset+=take;
            }
        }
        public void BeginTable(string[] headers,double[] proportions,string[]? alignment=null)
        {
            EndTable();
            tableHeaders=headers;
            // Reflow the table according to the chosen physical sheet. On
            // portrait A4 and smaller formats, use tighter cell padding and
            // a modest reduction of the configured table font rather than
            // squeezing cells outside the printable width.
            var compact=width<820 && headers.Length>=6;
            pad=compact?3:5;
            tableFont=Math.Clamp(template.TableFontSize,6,20)*96/72;
            if(compact)
            {
                var factor=Math.Clamp(ContentWidth/(headers.Length*105.0),.72,1);
                tableFont=Math.Max(6*96/72,tableFont*factor);
            }
            var total=proportions.Sum();
            var raw=proportions.Select(x=>ContentWidth*x/total).ToArray();
            // Numeric/contract columns must have enough width to avoid clipped
            // headers and data. Borrow width proportionally from wider columns.
            var floor=Math.Max(24,tableFont*2+pad*2);
            var shortage=raw.Where(x=>x<floor).Sum(x=>floor-x);
            var excess=raw.Where(x=>x>floor).Sum(x=>x-floor);
            if(shortage>excess+.01)throw LayoutError();
            columnWidths=raw.Select(x=>x<floor?floor:
                x-shortage*(x-floor)/Math.Max(1,excess)).ToArray();
            aligns=alignment??Enumerable.Repeat("left",headers.Length).ToArray();
            var needed=HeaderHeight();
            if(y+needed+tableFont*1.24+pad*2>Limit)NewPage();else DrawHeader();
        }
        private double HeaderHeight()=>tableHeaders!.Select((x,i)=>Text(x,columnWidths[i]-pad*2,tableFont,true,"center").DesiredSize.Height).Max()+pad*2;
        private void DrawHeader()=>DrawCells(tableHeaders!,true,new SolidColorBrush(Color.FromRgb(231,237,241)),Enumerable.Repeat("center",tableHeaders!.Length).ToArray());
        public void EndTable(){if(tableHeaders is not null){tableHeaders=null;y+=9;pad=5;}}
        public void Row(string[] cells)
        {
            var all=cells.Select((x,i)=>Wrap(x,columnWidths[i]-pad*2,tableFont,false)).ToList();
            var lineHeight=tableFont*1.24;
            var count=all.Max(x=>x.Count);
            for(var offset=0;offset<count;)
            {
                var maxLines=(int)((Limit-y-pad*2)/lineHeight);
                if(maxLines<1){NewPage();maxLines=(int)((Limit-y-pad*2)/lineHeight);}
                if(maxLines<1)throw LayoutError();
                var take=Math.Min(maxLines,count-offset);
                var parts=all.Select(lines=>string.Join("\n",lines.Skip(offset).Take(take))).ToArray();
                var needed=parts.Select((x,i)=>Text(x,columnWidths[i]-pad*2,tableFont,false,aligns[i]).DesiredSize.Height).Max()+pad*2;
                Ensure(needed);DrawCells(parts,false,Brushes.White,aligns);
                offset+=take;
            }
        }
        public void GroupRow(string text)
        {
            var lines=Wrap(text,ContentWidth-pad*2,tableFont,true);var lineHeight=tableFont*1.24;
            for(int offset=0;offset<lines.Count;)
            {
                var count=(int)((Limit-y-pad*2)/lineHeight);
                if(count<1){NewPage();count=(int)((Limit-y-pad*2)/lineHeight);}
                if(count<1)throw LayoutError();
                var take=Math.Min(count,lines.Count-offset);var element=Text(string.Join("\n",lines.Skip(offset).Take(take)),ContentWidth-pad*2,tableFont,true,"left");Ensure(element.DesiredSize.Height+pad*2);
                var box=new Border{Width=ContentWidth,Height=element.DesiredSize.Height+pad*2,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(.5),Background=new SolidColorBrush(Color.FromRgb(243,246,248)),Padding=new Thickness(pad),Child=element};Place(box,left,y);y+=box.Height;offset+=take;
            }
        }
        private void DrawCells(string[] cells,bool bold,Brush background,string[] alignment)
        {
            var elements=cells.Select((x,i)=>Text(x,columnWidths[i]-pad*2,tableFont,bold,alignment[i])).ToArray();
            var h=Math.Max(tableFont*1.24+pad*2,elements.Max(x=>x.DesiredSize.Height)+pad*2);var x=left;
            for(var i=0;i<elements.Length;i++)
            {
                var border=new Border{Width=columnWidths[i],Height=h,BorderBrush=new SolidColorBrush(Color.FromRgb(80,86,92)),BorderThickness=new Thickness(.5),Background=background,Padding=new Thickness(pad),Child=elements[i]};
                Place(border,x,y);x+=columnWidths[i];
            }
            y+=h;
        }
        public void Signatures(List<string> labels,string caption,string alignment,string? dateText=null)
        {
            EndTable();var w=ContentWidth/labels.Count;var headingHeight=labels.Select(x=>Text(x,w-12,font,true,alignment).DesiredSize.Height).Max();var captionHeight=Text(caption,w-12,font,false,alignment,true).DesiredSize.Height;
            var dateHeight=string.IsNullOrWhiteSpace(dateText)?0:Text(dateText,ContentWidth,font,false,"right",true).DesiredSize.Height+5;
            Ensure(dateHeight+headingHeight+captionHeight+72);
            if(dateHeight>0)Paragraph(dateText!,italic:true,align:"right");
            var x=left;
            foreach(var label in labels){var a=Text(label,w-12,font,true,alignment);Place(a,x+6,y);var b=Text(caption,w-12,font,false,alignment,true);Place(b,x+6,y+headingHeight+5);x+=w;}
            y+=headingHeight+captionHeight+72;
        }
        private TextBlock Text(string? value,double textWidth,double fs,bool bold,string align,bool italic=false)
        {
            var block=new TextBlock{Text=value??"",Width=Math.Max(10,textWidth),TextWrapping=TextWrapping.Wrap,FontFamily=family,FontSize=fs,LineHeight=fs*1.24,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,FontStyle=italic?FontStyles.Italic:FontStyles.Normal,Foreground=Brushes.Black,TextAlignment=align switch{"center"=>TextAlignment.Center,"right"=>TextAlignment.Right,_=>TextAlignment.Left}};
            block.Measure(new Size(Math.Max(10,textWidth),double.PositiveInfinity));return block;
        }
        private TextBlock SingleLine(string value,double fs,bool bold,string alignment)
        {
            var block=Text(value,ContentWidth,fs,bold,alignment);block.TextWrapping=TextWrapping.NoWrap;block.TextTrimming=TextTrimming.CharacterEllipsis;block.Height=fs*1.24;block.Measure(new Size(ContentWidth,block.Height));return block;
        }
        private void Place(UIElement item,double x,double yy){Canvas.SetLeft(item,x);Canvas.SetTop(item,yy);canvas.Children.Add(item);}
        private List<string> Wrap(string? value,double available,double fs,bool bold)
        {
            var result=new List<string>();var typeface=new Typeface(family,FontStyles.Normal,bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal);
            bool Fits(string s)=>new FormattedText(s,CultureInfo.GetCultureInfo("vi-VN"),FlowDirection.LeftToRight,typeface,fs,Brushes.Black,1).WidthIncludingTrailingWhitespace<=available-.5;
            foreach(var paragraph in (value??"").Replace("\r","").Split('\n'))
            {
                var current="";
                foreach(var word in paragraph.Split(' '))
                {
                    var next=current.Length==0?word:current+" "+word;
                    if(Fits(next)){current=next;continue;}
                    if(current.Length>0){result.Add(current);current="";}
                    if(Fits(word)){current=word;continue;}
                    foreach(var ch in word){if(current.Length>0&&!Fits(current+ch)){result.Add(current);current="";}current+=ch;}
                }
                result.Add(current);
            }
            return result.Count==0?[""]:result;
        }
        public FixedDocument Finish()
        {
            // Long codes and dates are already wrapped in the page header.
            // Keep the footer short so it can never be ellipsized on A4/A5.
            for(var i=0;i<footers.Count;i++)footers[i].Text=$"Trang {i+1} / {footers.Count}";
            foreach(var content in document.Pages){content.Child.Measure(new Size(width,height));content.Child.Arrange(new Rect(0,0,width,height));content.Child.UpdateLayout();}
            return document;
        }
    }
}
