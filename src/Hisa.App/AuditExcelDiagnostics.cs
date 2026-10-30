using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Audit regressions use temporary workbooks only.</summary>
public static class AuditExcelDiagnostics
{
    public static Task<List<string>> RunAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"hisa-excel-audit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();var store=new ExcelStore();
        void Check(bool valid,string name){if(!valid)throw new InvalidOperationException("FAIL: "+name);checks.Add(name);}
        var quantities=Path.Combine(root,"numbers.xlsx");
        using(var book=new XLWorkbook())
        {
            var sheet=book.AddWorksheet("Cont đi Hưng Yên");Headers(sheet);
            var values=new[]{"1,5","1.5","1.234,50","1,234.50"};
            for(int index=0;index<values.Length;index++)
            {
                Cont(sheet,index+2,"1012026","Công trình A",index==0?"HƯNG YÊN":"","Ray dẫn hướng T78",1);
                sheet.Cell(index+2,8).Value=values[index];
            }
            book.SaveAs(quantities);
        }
        var parsed=store.ReadMaster(quantities).ContPackages.Single().Items.Select(x=>x.Quantity).ToList();
        Check(parsed.SequenceEqual(new decimal?[]{1.5m,1.5m,1234.5m,1234.5m}),"Excel text quantities preserve decimal commas and both standard number formats");

        var markers=Path.Combine(root,"markers.xlsx");
        using(var book=new XLWorkbook())
        {
            var cont=book.AddWorksheet("Cont đi Hưng Yên");Headers(cont);
            cont.Cell(2,5).Value="HƯNG YÊN";Cont(cont,3,"1012026","Công trình A","","Ray dẫn hướng T78",1);
            cont.Cell(4,5).Value="HƯNG YÊN";Cont(cont,5,"1012026","Công trình A","","Ray dẫn hướng T78",1);
            var detail=book.AddWorksheet("chi tiết");detail.Cell(2,2).Value="Ngày cont về";
            detail.Cell(3,6).Value="HƯNG YÊN";Detail(detail,4,"1012026","Công trình A","","Ray dẫn hướng T78",1,new DateTime(2026,10,16));
            detail.Cell(5,6).Value="HƯNG YÊN";Detail(detail,6,"1012026","Công trình A","","Ray dẫn hướng T78",1,new DateTime(2026,10,16));book.SaveAs(markers);
        }
        var markerData=store.ReadMaster(markers);
        Check(markerData.ContPackages.Count==2&&markerData.DetailPackages.Count==2&&markerData.Packages.Count==2,"Marker-only Hưng Yên separator rows preserve two physical packages");

        var interleaved=Path.Combine(root,"interleaved.xlsx");var both=Path.Combine(root,"both.xlsx");var kt=Path.Combine(root,"kt.xlsx");
        using(var book=new XLWorkbook())
        {
            var cont=book.AddWorksheet("Cont đi Hưng Yên");Headers(cont);Cont(cont,2,"1012026","Công trình A","HƯNG YÊN","Ray dẫn hướng T78",1);Cont(cont,3,"2022026","Công trình B","HƯNG YÊN","Tủ điện",2);
            var detail=book.AddWorksheet("chi tiết");detail.Cell(2,2).Value="Ngày cont về";
            Detail(detail,3,"1012026","Công trình A","HƯNG YÊN","Ray dẫn hướng A1",1,new DateTime(2026,10,16));
            Detail(detail,4,"2022026","Công trình B","","Tủ điện cần giữ",2,new DateTime(2026,10,16));
            Detail(detail,5,"1012026","Công trình A","","Ray dẫn hướng A2",3,new DateTime(2026,10,16));detail.Range("F3:F5").Merge();
            var keep=book.AddWorksheet("Giữ nguyên");keep.Cell(1,1).Value="Dữ liệu riêng";keep.Cell(2,1).FormulaA1="1+2";
            keep.Cell(3,1).FormulaA1="_xlfn.LET(_xlpm.data,_xlfn._xlws.FILTER(B3:I10000,ISNUMBER(MATCH(E3:E10000,K3:K30,0)),\"Không tìm thấy dữ liệu\"),IF(_xlpm.data=\"\",\"\",_xlpm.data))";
            book.SaveAs(interleaved);
        }
        File.Copy(interleaved,both);
        using(var book=new XLWorkbook())
        {
            var sheet=book.AddWorksheet("chi tiết (2)");sheet.Cell(2,2).Value="Ngày cont về";Detail(sheet,3,"1012026","Công trình A","HƯNG YÊN","Ray dẫn hướng KT mới",9,new DateTime(2026,10,20));book.SaveAs(kt);
        }
        store.ApplyKt(interleaved,kt);var updated=store.ReadMaster(interleaved);
        Check(updated.DetailPackages.Count==3&&updated.DetailPackages.Single(x=>x.Contract=="2022026").Items.Single().Material=="Tủ điện cần giữ","KT fallback adds the missing arrival package without replacing existing contract rows");
        Check(updated.DetailPackages.Single(x=>x.Contract=="2022026").ArrivalDate=="2026-10-16"&&updated.DetailPackages.Single(x=>x.Contract=="1012026"&&x.ArrivalDate=="2026-10-16").Items.Any(i=>i.Material=="Ray dẫn hướng A2")&&updated.DetailPackages.Any(x=>x.Contract=="1012026"&&x.ArrivalDate=="2026-10-20"),"KT fallback preserves existing arrivals and imports its missing date/package");
        using(var book=new XLWorkbook(interleaved))
        {
            Check(book.Worksheet("Giữ nguyên").Cell(2,1).HasFormula,"KT fallback preserves unrelated workbook formulas");
            Check(book.Worksheet("Giữ nguyên").Cell(3,1).FormulaA1.Contains("FILTER",StringComparison.OrdinalIgnoreCase),"KT fallback retains dynamic FILTER formulas in the workbook");
        }
        using(var book=new XLWorkbook(kt))
        {
            Detail(book.Worksheet(1),4,"2022026","Công trình B","HƯNG YÊN","Tủ điện KT mới",4,new DateTime(2026,10,21));book.Save();
        }
        store.ApplyKt(both,kt);var all=store.ReadMaster(both);
        Check(all.DetailPackages.Count==4&&all.DetailPackages.Sum(x=>x.ItemCount)==5&&all.DetailPackages.Any(x=>x.Contract=="2022026"&&x.ArrivalDate=="2026-10-16"&&x.Items[0].Material=="Tủ điện cần giữ")&&all.DetailPackages.Any(x=>x.Contract=="2022026"&&x.ArrivalDate=="2026-10-21"&&x.Items[0].Quantity==4),"KT fallback imports missing contract/date packages without deleting or duplicating existing rows");
        var interleavedDates=Path.Combine(root,"interleaved-dates.xlsx");
        using(var book=new XLWorkbook())
        {
            var cont=book.AddWorksheet("Cont đi Hưng Yên");Headers(cont);Cont(cont,2,"1012026","Công trình A","HƯNG YÊN","Ray thay cho ngày 16",1);
            var detail=book.AddWorksheet("chi tiết");detail.Cell(2,2).Value="Ngày cont về";
            Detail(detail,3,"1012026","Công trình A","HƯNG YÊN","Ray ngày 16 A1",1,new DateTime(2026,10,16));
            Detail(detail,4,"1012026","Công trình A","","Ray ngày 17 cần giữ",2,new DateTime(2026,10,17));
            Detail(detail,5,"1012026","Công trình A","","Ray ngày 16 A2",3,new DateTime(2026,10,16));detail.Range("F3:F5").Merge();book.SaveAs(interleavedDates);
        }
        store.RecordArrival(interleavedDates,kt,store.ReadMaster(interleavedDates).ContPackages,new DateTime(2026,10,16),false);
        var keptDates=store.ReadMaster(interleavedDates).DetailPackages;
        Check(keptDates.Count==2&&keptDates.Single(x=>x.ArrivalDate=="2026-10-17").Items.Single().Material=="Ray ngày 17 cần giữ","Re-entering one arrival day preserves interleaved rows of another day for the same contract");

        var exportRoot=SelfTests.CreateDemoWorkspace();var settings=new AppSettings{MasterPath=Path.Combine(exportRoot,"Tổng hợp.xlsx"),ProductExportPath=Path.Combine(exportRoot,"Thành phẩm xuất kho.xlsx")};
        var a=new MaterialLine{Contract="1012026",Project="Công trình A",Company="Cty",Material="Ray dẫn hướng A",Unit="mét",Quantity=1,Include=false};
        var b=new MaterialLine{Contract="2022026",Project="Công trình B",Company="Cty",Material="Tủ điện B",Unit="Bộ",Quantity=1};
        var slip=new Slip{Id="audit-inclusion",SlipCode="AUDIT-001",Status="delivered",Contracts=[a.Contract,b.Contract],Lines=[a,b],ContractPackages=[new Package{Id="snapshot-a",Contract=a.Contract,Project=a.Project,Company=a.Company,Items=[a.Copy()]},new Package{Id="snapshot-b",Contract=b.Contract,Project=b.Project,Company=b.Company,Items=[b.Copy()]}]};
        store.SyncExportLedgers(settings,[slip]);
        Check(store.ReadShipments(settings).Where(x=>x.SlipId==slip.Id).Select(x=>x.Contract).SequenceEqual(new[]{"2022026"}),"Unchecked contract retained in snapshots is not marked exported in canonical ledger");
        var ledger=Path.Combine(exportRoot,"XUẤT HÀNG","Sổ theo dõi xuất hàng.xlsx");
        using(var book=new XLWorkbook(ledger))
        {
            Check(book.Worksheet("Đơn đã xuất").RowsUsed().Skip(1).All(x=>x.Cell(3).GetString()=="2022026"),"Orders worksheet uses only contracts with included materials");
            Check(book.Worksheet("Kiện Hưng Yên").RowsUsed().Skip(1).All(x=>x.Cell(3).GetString()=="2022026"),"Package export worksheet excludes entirely unchecked contracts");
        }
        Check(slip.ContractPackages.Count==2,"Full original package snapshots remain attached to saved slip");
        var manual=new Slip{Id="audit-manual",SlipCode="AUDIT-002",Status="delivered",Contracts=["3032026"],Lines=[new MaterialLine{Contract="3032026",Project="Bổ sung thủ công",Company="Cty",Material="Ray dẫn hướng bổ sung",Unit="mét",Quantity=2}]};
        store.SyncExportLedgers(settings,[slip,manual]);
        using(var book=new XLWorkbook(ledger))Check(book.Worksheet("Đơn đã xuất").RowsUsed().Skip(1).Any(x=>x.Cell(3).GetString()=="3032026"),"Manually added included contract appears in orders even without package snapshot");

        var source=Path.Combine(root,"CONT ĐI");var sourceDay=Path.Combine(source,"27.10.2026");Directory.CreateDirectory(sourceDay);
        using(var book=new XLWorkbook())
        {
            var sheet=book.AddWorksheet("Kiện");for(int c=0;c<5;c++)sheet.Cell(1,c+1).Value=new[]{"Kiện","Tên vật tư","ĐVT","Số lượng","Ghi chú"}[c];sheet.Cell(2,1).Value="HƯNG YÊN";sheet.Cell(2,2).Value="Ray dẫn hướng";sheet.Cell(2,3).Value="mét";sheet.Cell(2,4).Value=5;book.SaveAs(Path.Combine(sourceDay,"12342026.xlsx"));
        }
        using(var book=new XLWorkbook())
        {
            var sheet=book.AddWorksheet("Lịch");sheet.Cell(1,3).Value="Dự án A (1)";sheet.Cell(1,4).Value="Cty";sheet.Cell(1,5).Value="12342026";sheet.Cell(2,3).Value="Dự án B (2)";sheet.Cell(2,4).Value="Cty";sheet.Cell(2,5).Value="12342026";book.SaveAs(Path.Combine(root,"HÀNG THEO CONT.xlsx"));
        }
        var scanned=store.ScanSources(new AppSettings{MasterPath=interleaved,KtPath=kt,DataSources=[source]});
        Check(scanned.Warnings.Any(x=>x.Contains("nhiều công trình"))&&scanned.Packages.Single().Project.StartsWith("Chưa đối chiếu"),"Ambiguous same-number source warns rather than selecting the first project");
        File.Copy(Path.Combine(sourceDay,"12342026.xlsx"),Path.Combine(sourceDay,"12342026(2).xlsx"));
        var tagged=store.ScanSources(new AppSettings{MasterPath=interleaved,KtPath=kt,DataSources=[source]});
        Check(tagged.Packages.Single(x=>x.SourceFile=="12342026(2).xlsx").Project=="Dự án B (2)","Explicit source filename suffix resolves the correct separate project");
        var badSource=Path.Combine(root,"invalid-source");Directory.CreateDirectory(badSource);
        using(var book=new XLWorkbook())
        {
            var sheet=book.AddWorksheet("Kiện");for(int c=0;c<5;c++)sheet.Cell(1,c+1).Value=new[]{"Kiện","Tên vật tư","ĐVT","Số lượng","Ghi chú"}[c];sheet.Cell(2,1).Value="HƯNG YÊN";sheet.Cell(2,2).Value="Ray dẫn hướng";sheet.Cell(2,3).Value="mét";sheet.Cell(2,4).Value="1,2,3";book.SaveAs(Path.Combine(badSource,"88882026.xlsx"));
        }
        var badScan=store.ScanSources(new AppSettings{MasterPath=interleaved,KtPath=kt,DataSources=[badSource]});
        Check(badScan.Warnings.Any(x=>x.Contains("số lượng"))&&badScan.Packages.Single().Items.Single().Quantity==null,"Invalid source quantity is warned rather than silently replacing valid summary data");
        using(var book=new XLWorkbook(Path.Combine(badSource,"88882026.xlsx")))
        {
            book.Worksheet(1).Cell(2,1).Value="HÀ NỘI";book.Save();
        }
        var otherScan=store.ScanSources(new AppSettings{MasterPath=interleaved,KtPath=kt,DataSources=[badSource]});
        Check(otherScan.Packages.Count==0&&!otherScan.Warnings.Any(x=>x.Contains("số lượng")),"Invalid quantities outside Hưng Yên do not block the Hưng Yên source");
        return Task.FromResult(checks);
    }
    private static void Headers(IXLWorksheet sheet)
    {
        var values=new[]{"Ngày cont đi","Công trình","Công ty","Hợp đồng","Kiện","Tên vật tư","ĐVT","Số lượng","Ghi chú"};for(int col=0;col<values.Length;col++)sheet.Cell(1,col+1).Value=values[col];
    }
    private static void Cont(IXLWorksheet sheet,int row,string contract,string project,string marker,string material,decimal quantity)
    {
        sheet.Cell(row,1).Value=new DateTime(2026,10,15);sheet.Cell(row,1).Style.DateFormat.Format="dd/MM/yyyy";sheet.Cell(row,2).Value=project;sheet.Cell(row,3).Value="Cty";sheet.Cell(row,4).Value=contract;sheet.Cell(row,5).Value=marker;sheet.Cell(row,6).Value=material;sheet.Cell(row,7).Value="Bộ";sheet.Cell(row,8).Value=(double)quantity;
    }
    private static void Detail(IXLWorksheet sheet,int row,string contract,string project,string marker,string material,decimal quantity,DateTime date)
    {
        sheet.Cell(row,2).Value=date;sheet.Cell(row,2).Style.DateFormat.Format="dd/MM/yyyy";sheet.Cell(row,3).Value=project;sheet.Cell(row,4).Value="Cty";sheet.Cell(row,5).Value=contract;sheet.Cell(row,6).Value=marker;sheet.Cell(row,7).Value=material;sheet.Cell(row,8).Value="Bộ";sheet.Cell(row,9).Value=(double)quantity;
    }
}
