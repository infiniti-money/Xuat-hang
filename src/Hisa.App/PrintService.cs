using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace Hisa.App;

/// <summary>Native fixed-page printing. Every row is measured, paginated and repeated headers are retained.</summary>
public static class PrintService
{
    public static void PreviewSlip(Window owner, Slip slip, PrintTemplate template) => Preview(owner, BuildSlipDocument(slip, template), slip.SlipCode.Length > 0 ? slip.SlipCode : "Phiếu bàn giao");
    public static void PreviewReport(Window owner, ContractReport report) => Preview(owner, BuildReportDocument(report), "Báo cáo hợp đồng và vật tư");
    public static void PreviewDaily(Window owner, DailyReport report) => Preview(owner, BuildDailyDocument(report), "Báo cáo xuất hàng " + Rules.Day(report.Date));

    public static FixedDocument BuildSlipDocument(Slip slip, PrintTemplate template)
    {
        var p = new PageWriter(template, template.Title, $"{template.CodeLabel}: {slip.SlipCode}  ·  {template.DateLabel}: {Rules.Day(slip.HandoverDate)}");
        var lines = slip.Lines.Where(x => x.Include).ToList();
        if (template.Layout == "contract_delivery")
        {
            var groups = lines.GroupBy(x => Rules.Identity(x.Contract, x.Project, x.Company)).ToList();
            if (groups.Count == 0)
            {
                Header(p, template, slip);
                p.Paragraph("Phiếu chưa có dòng vật tư. Hãy tải dữ liệu hoặc bổ sung danh mục trước khi giao hàng.");
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
                    string[] cells=[(++number).ToString(),item.Material,item.Unit,Qty(item.Quantity),"",MaterialNote(item,template,slip)];
                    p.Row(visible.Select(i=>cells[i]).ToArray());
                }
                while (number < Math.Clamp(template.MinRows, 0, 100)) {var cells=visible.Select(_=>"").ToArray();cells[0]=(++number).ToString();p.Row(cells);}
                p.EndTable();
                Signatures(p, template, slip);
            }
        }
        else
        {
            Header(p, template, slip);
            p.Paragraph($"{template.DateLabel}: {Rules.Day(slip.HandoverDate)}      {template.CodeLabel}: {slip.SlipCode}", align: template.MetaAlign);
            if (!string.IsNullOrWhiteSpace(slip.Name)) p.Paragraph(slip.Name, bold: true);
            if (!string.IsNullOrWhiteSpace(slip.Note)) p.Paragraph("Ghi chú phiếu: " + slip.Note);
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

    private static void Preview(Window owner, FixedDocument document, string title)
    {
        var window=new Window{Title="Xem trước khi in · "+title,Width=1180,Height=850,MinWidth=760,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=owner,Background=new SolidColorBrush(Color.FromRgb(235,241,247))};
        var root=new DockPanel();
        var bar=new Border{Background=Brushes.White,Padding=new Thickness(18,12,18,12)};
        var controls=new DockPanel();
        var close=new Button{Content="Đóng",Padding=new Thickness(18,9,18,9),Margin=new Thickness(10,0,0,0)};close.Click+=(_,_)=>window.Close();DockPanel.SetDock(close,Dock.Right);controls.Children.Add(close);
        var print=new Button{Content="In / Lưu PDF",Padding=new Thickness(22,9,22,9),Background=new SolidColorBrush(Color.FromRgb(13,116,132)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};
        DockPanel.SetDock(print,Dock.Right);controls.Children.Add(print);
        controls.Children.Add(new TextBlock{Text=$"{title}   ·   {document.Pages.Count} trang",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold,FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(25,48,70))});bar.Child=controls;DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        var hint=new TextBlock{Text="Chọn Microsoft Print to PDF trong hộp thoại in để lưu thành tệp PDF. In không thay đổi trạng thái xuất hàng.",Margin=new Thickness(20,8,20,8),Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(hint,Dock.Bottom);root.Children.Add(hint);
        root.Children.Add(new DocumentViewer{Document=document,Background=new SolidColorBrush(Color.FromRgb(222,230,238))});
        print.Click+=(_,_)=>
        {
            try
            {
                var dialog=new PrintDialog();
                dialog.PrintTicket.PageOrientation=document.DocumentPaginator.PageSize.Width>document.DocumentPaginator.PageSize.Height?PageOrientation.Landscape:PageOrientation.Portrait;
                dialog.PrintTicket.PageMediaSize=new PageMediaSize(document.DocumentPaginator.PageSize.Width,document.DocumentPaginator.PageSize.Height);
                if(dialog.ShowDialog()==true) dialog.PrintDocument(document.DocumentPaginator,title);
            }
            catch(Exception ex){MessageBox.Show(window,"Chưa thể in: "+ex.Message,"In tài liệu",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };
        window.Content=root;window.ShowDialog();
    }

    /// <summary>Measured fixed-page writer. No overflow clipping: oversized rows are split at wrapped text lines.</summary>
    private sealed class PageWriter
    {
        private readonly FixedDocument document=new();
        private readonly PrintTemplate template;
        private readonly double width,height,left,right,top,bottom,font,tableFont;
        private readonly string title,context;
        private readonly List<TextBlock> footers=[];
        private Canvas canvas=null!;
        private double y;
        private string[]? tableHeaders;
        private double[] columnWidths=[];
        private string[] aligns=[];
        private readonly FontFamily family;
        private const double Pad=5;
        private double ContentWidth=>width-left-right;
        private double FooterHeight=>font*.72*1.24+2;
        private double Limit=>height-bottom-FooterHeight-8;
        private static ArgumentException LayoutError()=>new("Khổ giấy, lề hoặc cỡ chữ hiện tại không đủ chỗ in nội dung. Hãy giảm lề/cỡ chữ hoặc chọn khổ giấy lớn hơn.");
        public PageWriter(PrintTemplate template,string title,string context)
        {
            this.template=template;this.title=title;this.context=context;
            var a5=template.PaperSize=="A5";var pw=(a5?148:210)*96/25.4;var ph=(a5?210:297)*96/25.4;
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
            var header=SingleLine(title,font*.84,true,"left");Place(header,left,y);y+=header.DesiredSize.Height+3;
            var meta=SingleLine(context,font*.78,false,"left");Place(meta,left,y);y+=meta.DesiredSize.Height+7;
            var footer=SingleLine("",font*.72,false,"right");Place(footer,left,height-bottom-FooterHeight);footers.Add(footer);
            if(y+font*1.24>Limit)throw LayoutError();
            if(tableHeaders is not null)
            {
                if(y+HeaderHeight()+tableFont*1.24+Pad*2>Limit)throw LayoutError();
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
            EndTable();tableHeaders=headers;var total=proportions.Sum();columnWidths=proportions.Select(x=>ContentWidth*x/total).ToArray();aligns=alignment??Enumerable.Repeat("left",headers.Length).ToArray();
            var needed=HeaderHeight();
            if(y+needed+tableFont*1.24+Pad*2>Limit)NewPage();else DrawHeader();
        }
        private double HeaderHeight()=>tableHeaders!.Select((x,i)=>Text(x,columnWidths[i]-Pad*2,tableFont,true,"center").DesiredSize.Height).Max()+Pad*2;
        private void DrawHeader()=>DrawCells(tableHeaders!,true,new SolidColorBrush(Color.FromRgb(231,237,241)),Enumerable.Repeat("center",tableHeaders!.Length).ToArray());
        public void EndTable(){if(tableHeaders is not null){tableHeaders=null;y+=9;}}
        public void Row(string[] cells)
        {
            var all=cells.Select((x,i)=>Wrap(x,columnWidths[i]-Pad*2,tableFont,false)).ToList();
            var lineHeight=tableFont*1.24;
            var count=all.Max(x=>x.Count);
            for(var offset=0;offset<count;)
            {
                var maxLines=(int)((Limit-y-Pad*2)/lineHeight);
                if(maxLines<1){NewPage();maxLines=(int)((Limit-y-Pad*2)/lineHeight);}
                if(maxLines<1)throw LayoutError();
                var take=Math.Min(maxLines,count-offset);
                var parts=all.Select(lines=>string.Join("\n",lines.Skip(offset).Take(take))).ToArray();
                var needed=parts.Select((x,i)=>Text(x,columnWidths[i]-Pad*2,tableFont,false,aligns[i]).DesiredSize.Height).Max()+Pad*2;
                Ensure(needed);DrawCells(parts,false,Brushes.White,aligns);
                offset+=take;
            }
        }
        public void GroupRow(string text)
        {
            var lines=Wrap(text,ContentWidth-Pad*2,tableFont,true);var lineHeight=tableFont*1.24;
            for(int offset=0;offset<lines.Count;)
            {
                var count=(int)((Limit-y-Pad*2)/lineHeight);
                if(count<1){NewPage();count=(int)((Limit-y-Pad*2)/lineHeight);}
                if(count<1)throw LayoutError();
                var take=Math.Min(count,lines.Count-offset);var element=Text(string.Join("\n",lines.Skip(offset).Take(take)),ContentWidth-Pad*2,tableFont,true,"left");Ensure(element.DesiredSize.Height+Pad*2);
                var box=new Border{Width=ContentWidth,Height=element.DesiredSize.Height+Pad*2,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(.5),Background=new SolidColorBrush(Color.FromRgb(243,246,248)),Padding=new Thickness(Pad),Child=element};Place(box,left,y);y+=box.Height;offset+=take;
            }
        }
        private void DrawCells(string[] cells,bool bold,Brush background,string[] alignment)
        {
            var elements=cells.Select((x,i)=>Text(x,columnWidths[i]-Pad*2,tableFont,bold,alignment[i])).ToArray();
            var h=Math.Max(tableFont*1.24+Pad*2,elements.Max(x=>x.DesiredSize.Height)+Pad*2);var x=left;
            for(var i=0;i<elements.Length;i++)
            {
                var border=new Border{Width=columnWidths[i],Height=h,BorderBrush=new SolidColorBrush(Color.FromRgb(80,86,92)),BorderThickness=new Thickness(.5),Background=background,Padding=new Thickness(Pad),Child=elements[i]};
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
            for(var i=0;i<footers.Count;i++)footers[i].Text=$"{context}     ·     Trang {i+1} / {footers.Count}";
            foreach(var content in document.Pages){content.Child.Measure(new Size(width,height));content.Child.Arrange(new Rect(0,0,width,height));content.Child.UpdateLayout();}
            return document;
        }
    }
}
