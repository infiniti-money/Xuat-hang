using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Hisa.App;

/// <summary>Printing regressions are built offline; no printer or window is opened.</summary>
public static class AuditPrintDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool valid,string name){if(!valid)throw new InvalidOperationException("FAIL: "+name);checks.Add(name);}
        var slip=new Slip{SlipCode="AUDIT-PRINT-001",HandoverDate="2026-10-27",Note="GHI_CHU_RIENG_CUA_PHIEU",Contracts=["123(1)"],Lines=[new MaterialLine{Id="motor-audit",Contract="123(1)",Project="Công trình kiểm tra",Company="Đơn vị",Material="Máy kéo kiểm tra",Unit="Bộ",Quantity=2}]};
        slip.MotorSerials=MotorSerials.BuildRows(slip);slip.MotorSerials[0].SerialNumber="SN-001-KEEP";slip.MotorSerials[1].SerialNumber="SN-002-KEEP";
        var template=new PrintTemplate{Layout="contract_delivery",OpeningText="Hôm nay, ngày",SupplementaryLabel="2. Danh sách vật tư bổ sung"};
        var doc=PrintService.BuildSlipDocument(slip,template);var text=AllText(doc);
        Check(text.Contains(slip.Note),"Contract delivery prints the saved slip note");
        Check(!text.Contains("2. 2.")&&!text.Contains("1. Hôm nay"),"Legacy opening and numbered section labels print once");
        Check(slip.MotorSerials.All(x=>text.Contains(x.SerialNumber)),"Contract print keeps all physical motor serials");
        var mixed=new Slip
        {
            SlipCode="BB-HY-20261102-TEST",
            HandoverDate="2026-11-02",Status="delivered",
            Contracts=["10392026","25132026","46242026"],
            Name="Tên phiếu bàn giao cần giữ nguyên",
            Note="GHI CHÚ BÀN GIAO ĐẦY ĐỦ",
            ContractOnly=[new ContractRow
            {
                Contract="46242026",Project="Công trình chưa có vật tư",
                Company="Bên nhận độc lập",Warning="Chờ đối chiếu nguồn vật tư"
            }],
            Lines=[
                new MaterialLine{Id="vt1",Contract="10392026",Project="Công trình A T1",
                    Company="Đơn vị A",Material="TỦ ĐIỆN TEST 001",Unit="Bộ",
                    Quantity=3,Category="Tủ điện",Note="GHI CHÚ VẬT TƯ TEST"},
                new MaterialLine{Id="vt2",Contract="25132026",Project="Công trình B T2",
                    Company="Đơn vị B",Material="RAY DẪN HƯỚNG TEST 002",Unit="mét",
                    Quantity=12.125m,Category="Ray dẫn hướng"},
                new MaterialLine{Id="vtExclude",Contract="25132026",
                    Project="Công trình B T2",Company="Đơn vị B",
                    Material="VẬT TƯ ĐÃ BỎ CHỌN KHÔNG IN",Unit="Bộ",
                    Quantity=9,Include=false}
            ]
        };
        var fullTemplate=new PrintTemplate
        {
            OpeningText="NỘI DUNG MỞ ĐẦU KHÔNG ĐƯỢC THIẾU",
            SupplementaryLabel="DANH SÁCH VẬT TƯ BÀN GIAO ĐẦY ĐỦ",
            FooterText="CHÂN PHIẾU XÁC NHẬN ĐẦY ĐỦ",
            Note="ĐIỀU KHOẢN XÁC NHẬN CỦA HAI BÊN",
            PaperSize="A4",Orientation="landscape",
            FontSize=9,TableFontSize=8,MinRows=0
        };
        foreach(var layout in new[]{"internal_summary","contract_delivery","standard"})
        {
            fullTemplate.Layout=layout;
            var fullDocument=PrintService.BuildSlipDocument(mixed,fullTemplate);
            var fullText=AllText(fullDocument);
            foreach(var value in new[]
            {
                mixed.SlipCode,"10392026","25132026","46242026",
                "TỦ ĐIỆN TEST 001","RAY DẪN HƯỚNG TEST 002",
                "12,125","Bên nhận độc lập","Công trình chưa có vật tư",
                "Chờ đối chiếu nguồn vật tư",
                fullTemplate.OpeningText,fullTemplate.SupplementaryLabel,
                fullTemplate.FooterText,fullTemplate.Note,mixed.Note
            })
                Check(fullText.Contains(value),"Full "+layout+" handover retains: "+value);
            Check(!fullText.Contains("VẬT TƯ ĐÃ BỎ CHỌN KHÔNG IN"),
                layout+" respects intentionally unchecked material rows");
            Check(fullText.Contains(fullTemplate.Signatures[0]),
                layout+" retains signature block with contract-only handovers");
        }
        fullTemplate.Layout="contract_delivery";
        mixed.Status="planned";
        var pendingText=AllText(PrintService.BuildSlipDocument(mixed,fullTemplate));
        Check(pendingText.Contains("12,125") &&
              !pendingText.Contains("VẬT TƯ ĐÃ BỎ CHỌN KHÔNG IN"),
              "Pending slip keeps planned quantity while excluding unchecked materials");
        // A slip containing ONLY contract identities still requires a complete
        // printable document rather than an empty table and lost contracts.
        var noMaterials=new Slip
        {
            SlipCode="NO-MATERIAL",HandoverDate="2026-11-02",
            ContractOnly=[new ContractRow{Contract="77882026",
                Project="Công trình chưa nhận vật tư",Company="Đơn vị thiếu nguồn"}]
        };
        foreach(var layout in new[]{"contract_delivery","internal_summary","standard"})
        {
            var printed=AllText(PrintService.BuildSlipDocument(noMaterials,
                new PrintTemplate{Layout=layout,MinRows=0}));
            Check(printed.Contains("77882026") &&
                  printed.Contains("Công trình chưa nhận vật tư"),
                layout+" prints contract-only slips even without any material lines");
        }
        foreach(var layout in new[]{"internal_summary","contract_delivery","standard"})
        {
            var hidden=new PrintTemplate{Layout=layout,ShowUnit=false,ShowQuantity=false,UnitLabel="UNIT_COLUMN_TO_HIDE",QuantityLabel="QUANTITY_COLUMN_TO_HIDE"};
            var hiddenText=AllText(PrintService.BuildSlipDocument(slip,hidden));
            Check(!hiddenText.Contains(hidden.UnitLabel)&&!hiddenText.Contains(hidden.QuantityLabel),"Hidden unit and quantity columns removed in "+layout);
            var fraction=new Slip{SlipCode="FRACTION",HandoverDate="2026-10-27",Contracts=["123"],Lines=[new MaterialLine{Contract="123",Project="Công trình",Material="Vật tư phân số",Unit="mét",Quantity=.125m},new MaterialLine{Contract="123",Project="Công trình",Material="Vật tư rất nhỏ",Unit="mét",Quantity=.001m}]};
            var fractionText=AllText(PrintService.BuildSlipDocument(fraction,new PrintTemplate{Layout=layout}));
            Check(fractionText.Contains("0,125")&&fractionText.Contains("0,001"),"Fractional quantities retain source precision in "+layout);
        }
        foreach(var option in PrintPaperSizes.Options)
        foreach(var orientation in new[]{"portrait","landscape"})
        {
            var paper=new PrintTemplate{Layout="standard",PaperSize=option.Key,
                Orientation=orientation,MinRows=0,MarginTop=8,MarginBottom=8,
                MarginLeft=8,MarginRight=8,FontSize=9,TableFontSize=8};
            var dimensions=PrintPaperSizes.GetMillimeters(option.Key);
            var expectedWidth=(orientation=="landscape"?dimensions.HeightMm:dimensions.WidthMm)*96/25.4;
            var expectedHeight=(orientation=="landscape"?dimensions.WidthMm:dimensions.HeightMm)*96/25.4;
            var page=PrintService.BuildSlipDocument(slip,paper).Pages[0].Child;
            Check(Math.Abs(page.Width-expectedWidth)<.02 &&
                  Math.Abs(page.Height-expectedHeight)<.02,
                option.Key+" "+orientation+" uses the correct physical page size");
        }
        try
        {
            PrintService.BuildSlipDocument(slip,new PrintTemplate{PaperSize="unsupported"});
            throw new InvalidOperationException("FAIL: unsupported paper silently printed on A4");
        }
        catch(ArgumentException ex)
        {
            Check(ex.Message.Contains("Khổ giấy"),"Unsupported paper never silently falls back to A4");
        }
        var longSlip=new Slip{SlipCode="LONG-"+new string('X',500),HandoverDate="2026-10-27",Contracts=["123(1)"],Lines=[new MaterialLine{Id="long-motor",Contract="123(1)",Project=string.Join(" ",Enumerable.Repeat("Công trình rất dài cần xuống dòng",100)),Company="Đơn vị",Material="Máy kéo kiểm tra dài",Unit="Bộ",Quantity=150}]};
        longSlip.MotorSerials=MotorSerials.BuildRows(longSlip);for(int i=0;i<longSlip.MotorSerials.Count;i++)longSlip.MotorSerials[i].SerialNumber="SER-"+(i+1).ToString("D4");
        var a5=new PrintTemplate{Layout="contract_delivery",PaperSize="A5",Orientation="portrait",MarginTop=8,MarginBottom=8,MarginLeft=8,MarginRight=8,FontSize=10,TableFontSize=10};
        var longDoc=PrintService.BuildSlipDocument(longSlip,a5);
        Check(longDoc.Pages.Count>1,"Long A5 contract print paginates");
        var longText=AllText(longDoc);
        Check(longSlip.MotorSerials.All(x=>longText.Contains(x.SerialNumber)),"Long A5 print retains every serial across page splits");
        Check(BordersInsidePrintableArea(longDoc,a5),"A5 rows remain inside the printable page area");
        Check(FootersSingleLineAndInside(longDoc),"Long slip codes do not wrap the page footer below paper");
        var signaturePages=longDoc.Pages.Where(page=>Nodes(page.Child).OfType<TextBlock>().Any(x=>x.Text==a5.Signatures[0])).ToList();
        Check(signaturePages.Count==1&&Nodes(signaturePages[0].Child).OfType<TextBlock>().Any(x=>x.Text==a5.Location+", ngày "+Rules.Day(longSlip.HandoverDate)),"Handover date stays on the same page as signatures");
        var impossible=new PrintTemplate{Layout="contract_delivery",PaperSize="A5",Orientation="landscape",MarginTop=40,MarginBottom=40,MarginLeft=40,MarginRight=40,FontSize=24,TableFontSize=20,TitleFontSize=32};
        try{PrintService.BuildSlipDocument(slip,impossible);throw new InvalidOperationException("FAIL: Infeasible layout accepted");}
        catch(ArgumentException ex){Check(ex.Message.Contains("Khổ giấy"),"Infeasible A5 layout shows a useful margin/font error");}
        var editor=new TemplateEditorWindow(impossible);
        try
        {
            try{typeof(TemplateEditorWindow).GetMethod("Read",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(editor,null);throw new InvalidOperationException("FAIL: Infeasible template saved");}
            catch(TargetInvocationException ex)when(ex.InnerException is ArgumentException){checks.Add("Template editor prevents saving an unprintable layout");}
        }
        finally{editor.Close();}
        return checks;
    }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject item)
    {
        yield return item;
        foreach(var child in LogicalTreeHelper.GetChildren(item))if(child is DependencyObject value)foreach(var descendant in Nodes(value))yield return descendant;
    }
    private static string AllText(FixedDocument doc)=>string.Join("\n",doc.Pages.SelectMany(page=>Nodes(page.Child)).OfType<TextBlock>().Select(x=>x.Text));
    private static bool BordersInsidePrintableArea(FixedDocument doc,PrintTemplate template)
    {
        foreach(var page in doc.Pages)
        {
            var limit=page.Child.Height-template.MarginBottom*96/25.4-(template.FontSize*96/72*.72*1.24+2)-8;
            foreach(var canvas in Nodes(page.Child).OfType<Canvas>())foreach(var border in canvas.Children.OfType<Border>())
                if(Canvas.GetTop(border)+border.ActualHeight>limit+.1||Canvas.GetLeft(border)+border.ActualWidth>page.Child.Width-template.MarginRight*96/25.4+.1)return false;
        }
        return true;
    }
    private static bool FootersSingleLineAndInside(FixedDocument doc)
    {
        foreach(var page in doc.Pages)
        {
            var footer=Nodes(page.Child).OfType<TextBlock>().FirstOrDefault(x=>x.Text.Contains("Trang "));
            if(footer==null||footer.TextWrapping!=TextWrapping.NoWrap||Canvas.GetTop(footer)+footer.ActualHeight>page.Child.Height)return false;
        }
        return true;
    }
}
