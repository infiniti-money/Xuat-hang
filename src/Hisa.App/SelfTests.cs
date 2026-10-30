using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Regression tests run only against isolated temporary workbooks; --benchmark is read-only.</summary>
public static class SelfTests
{
    public class TestResult
    {
        public bool Success { get; set; }
        public List<string> Checks { get; set; } = [];
        public string TestWorkspace { get; set; } = "";
        public double LoadMilliseconds { get; set; }
        public double SearchMilliseconds { get; set; }
        public string Error { get; set; } = "";
    }
    private static void Check(bool condition,string label,TestResult result)
    {
        if(!condition)throw new InvalidOperationException("FAIL: "+label);
        result.Checks.Add(label);
    }
    public static string CreateDemoWorkspace()
    {
        var root=Path.Combine(Path.GetTempPath(),"HISA-native-demo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"CONT ĐI"));
        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("Cont đi Hưng Yên");var headers=new[]{"Ngày cont đi","Tên công trình","Tên công ty","Số hợp đồng","Kiện hàng","Tên vật tư","ĐVT","Số lượng","Ghi chú"};
            for(int c=0;c<headers.Length;c++)ws.Cell(1,c+1).Value=headers[c];
            int r=2;
            foreach(var block in new[]{("05492026","Công trình A",new DateTime(2026,10,15),"Máy kéo M1",2m),("05492026","Công trình A",new DateTime(2026,10,15),"Máy kéo M2",3m),("05492026","Công trình B",new DateTime(2026,10,15),"Tủ điện Hisa",4m),("20012026","Công trình C",new DateTime(2026,10,16),"Đầu cửa tầng 700",3m),("123(1)","Công trình riêng (1)",new DateTime(2026,10,16),"Đầu cửa car",1m),("1231","Công trình khác",new DateTime(2026,10,16),"Bass nối T78",5m)})
            {
                WriteRow(ws,r,block.Item3,block.Item2,"Công ty A",block.Item1,"HƯNG YÊN",block.Item4,"Bộ",block.Item5);
                WriteRow(ws,r+1,block.Item3,block.Item2,"Công ty A",block.Item1,"","Ray dẫn hướng T78","mét",10);
                ws.Range(r,5,r+1,5).Merge();r+=2;
            }
            WriteRow(ws,r,new DateTime(2026,10,16),"Hà Nội","Công ty A","88882026","HÀ NỘI","Tủ điện","Bộ",99);
            var dt=wb.AddWorksheet("chi tiết");dt.Cell(2,2).Value="Ngày cont về";r=3;
            foreach(var block in new[]{(new DateTime(2026,10,16),"Máy kéo M1",2m),(new DateTime(2026,10,16),"Máy kéo M2",3m),(new DateTime(2026,10,17),"Máy kéo M3",1m)})
            {
                WriteDetail(dt,r,block.Item1,"Công trình A","Công ty A","05492026","HƯNG YÊN",block.Item2,"Bộ",block.Item3);
                WriteDetail(dt,r+1,block.Item1,"Công trình A","Công ty A","05492026","","Ray dẫn hướng T78","mét",10);
                dt.Range(r,6,r+1,6).Merge();r+=2;
            }
            WriteDetail(dt,r,new DateTime(2026,10,16),"Công trình B","Công ty A","05492026","HƯNG YÊN","Tủ điện Hisa","Bộ",4);
            wb.SaveAs(Path.Combine(root,"Tổng hợp.xlsx"));
        }
        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("chi tiết (2)");ws.Cell(2,2).Value="Ngày cont về";
            WriteDetail(ws,3,new DateTime(2026,10,16),"Công trình A","Công ty A","05492026","HƯNG YÊN","Máy kéo M1","Bộ",2);
            WriteDetail(ws,4,new DateTime(2026,10,16),"Công trình A","Công ty A","05492026","","Ray dẫn hướng T78","mét",10);
            ws.Range("F3:F4").Merge();
            WriteDetail(ws,5,new DateTime(2026,10,16),"Công trình A","Công ty A","05492026","HƯNG YÊN","Máy kéo M2","Bộ",3);
            WriteDetail(ws,6,new DateTime(2026,10,16),"Công trình A","Công ty A","05492026","","Ray dẫn hướng T78","mét",10);
            ws.Range("F5:F6").Merge();wb.SaveAs(Path.Combine(root,"KT tổng hợp.xlsx"));
        }
        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("Sheet1");ws.Cell(1,2).Value="THÀNH PHẨM XUẤT KHO";var h=new[]{"STT","Tên công trình","Công ty","Hợp đồng","Ngày xuất","Ghi chú"};
            for(int c=0;c<h.Length;c++)ws.Cell(2,c+1).Value=h[c];ws.Cell(3,2).Value="Legacy project";ws.Cell(3,3).Value="Legacy company";ws.Cell(3,4).Value="99992026";ws.Cell(3,5).Value=new DateTime(2026,10,24);ws.Cell(3,5).Style.DateFormat.Format="dd/MM/yyyy";wb.SaveAs(Path.Combine(root,"Thành phẩm xuất kho.xlsx"));
        }
        var data=Path.Combine(root,"native_data");Directory.CreateDirectory(data);
        var initial=new[]{new Slip{Id="fixture-pending",SlipCode="TEST-001",Name="Phiếu đã lên trước",HandoverDate="2026-10-27",Status="planned",Contracts=["05492026"],Lines=[new MaterialLine{Contract="05492026",Project="Công trình A",Company="Công ty A",Material="Máy kéo M1",Unit="Bộ",Quantity=2}]},new Slip{Id="fixture-cancel",SlipCode="TEST-CANCEL",Name="Phiếu đã hủy",HandoverDate="2026-10-27",Status="cancelled",Contracts=["20012026"],Lines=[new MaterialLine{Contract="20012026",Project="Công trình C",Company="Công ty A",Material="Đầu cửa tầng",Unit="Bộ",Quantity=3}]}};
        File.WriteAllText(Path.Combine(data,"slips.json"),JsonSerializer.Serialize(initial,WarehouseService.Json));
        File.WriteAllText(Path.Combine(data,"settings.json"),JsonSerializer.Serialize(new AppSettings{MasterPath=Path.Combine(root,"Tổng hợp.xlsx"),KtPath=Path.Combine(root,"KT tổng hợp.xlsx"),ProductExportPath=Path.Combine(root,"Thành phẩm xuất kho.xlsx"),DataSources=[Path.Combine(root,"CONT ĐI")],AutoRefresh=false,AutoSyncCont=false},WarehouseService.Json));
        return root;
    }
    private static void WriteRow(IXLWorksheet ws,int r,DateTime day,string project,string company,string contract,string marker,string name,string unit,decimal qty)
    {
        ws.Cell(r,1).Value=day;ws.Cell(r,1).Style.DateFormat.Format="dd/MM/yyyy";ws.Cell(r,2).Value=project;ws.Cell(r,3).Value=company;
        if(decimal.TryParse(contract,out var n)){ws.Cell(r,4).Value=(double)n;if(contract.StartsWith('0'))ws.Cell(r,4).Style.NumberFormat.Format=new string('0',contract.Length);}else ws.Cell(r,4).Value=contract;
        ws.Cell(r,5).Value=marker;ws.Cell(r,6).Value=name;ws.Cell(r,7).Value=unit;ws.Cell(r,8).Value=(double)qty;
    }
    private static void WriteDetail(IXLWorksheet ws,int r,DateTime day,string project,string company,string contract,string marker,string name,string unit,decimal qty)
    {
        ws.Cell(r,2).Value=day;ws.Cell(r,2).Style.DateFormat.Format="dd/MM/yyyy";ws.Cell(r,3).Value=project;ws.Cell(r,4).Value=company;ws.Cell(r,5).Value=contract;ws.Cell(r,6).Value=marker;ws.Cell(r,7).Value=name;ws.Cell(r,8).Value=unit;ws.Cell(r,9).Value=(double)qty;
    }
    public static async Task<TestResult> RunAsync()
    {
        var result=new TestResult();
        try
        {
            Check(Rules.Contract("BG 00549.2600")=="549.26","BG and decimal normalization",result);
            Check(Rules.Contract("00123(1)")=="123(1)"&&Rules.Contract("123(1)")!=Rules.Contract("1231"),"Parenthesis suffix remains distinct",result);
            var root=CreateDemoWorkspace();result.TestWorkspace=root;
            using var service=new WarehouseService(root);
            await service.InitializeAsync();result.LoadMilliseconds=service.LastLoadMs;
            Check(service.LastError.Length==0,"Native startup reads workbook successfully",result);
            Check(service.Snapshot.ContPackages.Count==6,"Six Hưng Yên blocks; other destination excluded",result);
            Check(service.Snapshot.DetailPackages.Count==4,"Separate arrival dates and two same-day blocks",result);
            Check(service.Snapshot.Packages.Count==7,"One-to-one cont/detail package merge",result);
            var watch=Stopwatch.StartNew();
            var report=service.Search(new SearchFilter{ContractsText="05492026\n20012026\n99992026\n88882026",Categories=["Máy kéo","Tủ điện"]});result.SearchMilliseconds=watch.Elapsed.TotalMilliseconds;
            Check(report.RequestedCount==4&&report.Missing.Count==2&&report.ContractCount==2&&report.ProjectCount==3,"Multi-code exact lookup missing and project counts",result);
            Check(report.Rows.Count==2&&report.NoMaterialMatch.Count==1,"OR material groups retain distinct projects",result);
            Check(report.Categories.Count==6&&report.Categories.Single(x=>x.Category=="Tủ điện").ContractCount==1,"Six category counts before material narrowing",result);
            Check(report.WithSlips==2&&report.CancelledCount==1,"Existing and cancelled slip evidence",result);
            var day=service.Search(new SearchFilter{ContractsText="05492026",ArrivalDate="2026-10-16",Categories=["Máy kéo"]});
            Check(day.Rows.Count==1&&day.Rows[0].Packages.Count==2&&day.Rows[0].Materials.Count==2,"Exact arrival scope retains two package blocks",result);
            var target=service.Search(new SearchFilter{ContractsText="05492026",ProjectTargets=[new ContractRow{Contract="05492026",Project="Công trình B",Company="Công ty A"}]});
            Check(target.Rows.Count==1&&target.Rows[0].Project=="Công trình B","Same number different project remains separate",result);
            Check(service.Search(new SearchFilter{ContractsText="0549"}).Rows.Count==0,"No partial prefix contract collision",result);
            Check(service.Search(new SearchFilter{ContractsText="123(1)"}).Rows.Single().Project=="Công trình riêng (1)","Parenthesized contract resolution",result);
            var draft=service.CreateDraft(day,"contract_delivery","2026-10-27");
            SerialTestData.Fill(draft,"FLOW");
            Check(draft.ContractPackages.Count==2&&draft.Lines.Count==2&&draft.ContractPackages.Sum(x=>x.Items.Count)==4,$"Slip includes filtered lines and full scoped package snapshots (lines={draft.Lines.Count}, packages={draft.ContractPackages.Count}, items={draft.ContractPackages.Sum(x=>x.Items.Count)}, source={string.Join(";",day.Rows[0].Packages.Select(p=>p.Id+":"+p.Items.Count))})",result);
            var saved=await service.SaveSlipAsync(draft);Check(saved.SlipCode.Length>0&&saved.Status=="planned","Unique native slip code and planned save",result);
            await service.ChangeSlipStatusAsync(saved.Id,"delivered");
            Check(new ExcelStore().ReadShipments(service.Settings).Any(x=>x.SlipId==saved.Id),"Delivered writes canonical product ledger",result);
            await service.ChangeSlipStatusAsync(saved.Id,"cancelled","Fixture cancel");
            var ledger=new ExcelStore().ReadShipments(service.Settings);
            Check(!ledger.Any(x=>x.SlipId==saved.Id)&&ledger.Any(x=>Rules.Contract(x.Contract)=="99992026"),"Cancel removes only owned ledger rows and preserves legacy",result);
            await service.ChangeSlipStatusAsync(saved.Id,"restore");Check(service.Slips.Single(x=>x.Id==saved.Id).Status=="delivered","Restore returns prior delivered state",result);
            await service.ChangeSlipStatusAsync(saved.Id,"delayed","Chưa lấy hàng");
            Check(service.Search(new SearchFilter{ContractsText="05492026"}).Rows.Any(x=>x.Flags.Any(f=>f.SlipId==saved.Id)),"Delayed warning attached to exact contract project",result);
            var emptyDay=service.BuildDaily("2026-01-01");Check(emptyDay.Slips.Count==0&&emptyDay.Contracts.Rows.Count==0,"Empty day does not become all contracts",result);
            var daily=service.BuildDaily("2026-10-27");Check(daily.StatusCounts["cancelled"]==1&&daily.OutstandingDelayed.Any(x=>x.Id==saved.Id),"Daily current statuses and delayed backlog",result);
            Check(!daily.Contracts.Rows.Any(x=>x.Contract=="05492026"&&x.Project=="Công trình B"),"Daily contract scope excludes another project with same code",result);
            var printTemplate=service.Templates.Single(x=>x.Id=="contract_delivery");
            var longSlip=WarehouseService.Clone(saved);longSlip.Lines=Enumerable.Range(1,111).Select(i=>new MaterialLine{Contract="05492026",Project="Công trình A rất dài cần xuống dòng để in đủ nội dung",Company="Công ty A",Material=$"Máy kéo vật tư {i} · có mô tả chi tiết",Unit="Bộ",Quantity=i}).ToList();
            var doc=PrintService.BuildSlipDocument(longSlip,printTemplate);Check(doc.Pages.Count>1,"Native contract slip pagination for 111 rows",result);
            Check(PrintService.BuildReportDocument(report).Pages.Count>0&&PrintService.BuildDailyDocument(daily).Pages.Count>0,"Native contract and daily printable documents",result);
            var export=Path.Combine(root,"report.xlsx");await service.ExportReportAsync(export,report);Check(File.Exists(export),"XLSX detailed report export",result);
            var plansReport=service.Search(new SearchFilter{ContractsText="05492026\n77772026",Categories=["Máy kéo"]});plansReport.Missing.ForEach(x=>x.Selected=false);
            var plans=await service.SavePlanAsync(plansReport,"2026-10-28","contract_delivery");
            Check(plans.Count==1&&plans.All(x=>x.Status=="planned"),"Batch creates separate selected-project waiting slips",result);
            var zip=Path.Combine(root,"backup.zip");await service.BackupAsync(zip);Check(File.Exists(zip),"Backup includes native state and canonical workbooks",result);
            var link=Path.Combine(root,"Hưng Yên C#.lnk");WindowsShortcut.Create(link,Environment.ProcessPath!);Check(File.Exists(link),"Unicode Windows shortcut",result);
            result.Checks.AddRange(ExcelDiagnostics.Run(root));
            result.Checks.AddRange(ContFileDiagnostics.Run(root));
            service.Dispose();service.Dispose();Check(true,"Safe repeated shutdown",result);
            using(var reopened=new WarehouseService(root))
            {
                await reopened.InitializeAsync();
                Check(reopened.Search(new SearchFilter{ContractsText="99992026"}).Missing.Single().Shipments.Any(x=>x.Date=="2026-10-24"),"Cached startup retains canonical legacy history",result);
            }
            using(var changed=new XLWorkbook(Path.Combine(root,"Thành phẩm xuất kho.xlsx")))
            {
                changed.Worksheet(1).Cell(3,5).Value=new DateTime(2026,10,25);changed.Save();
            }
            using(var reopened=new WarehouseService(root))
            {
                await reopened.InitializeAsync();
                Check(reopened.Search(new SearchFilter{ContractsText="99992026"}).Missing.Single().Shipments.Any(x=>x.Date=="2026-10-25"),"Changed canonical ledger invalidates cached history",result);
            }
            result.Checks.AddRange(await SlipWarningDiagnostics.RunAsync());
            result.Checks.AddRange(await SlipStateDiagnostics.RunAsync());
            result.Checks.AddRange(InfoDiagnostics.Run());
            result.Checks.AddRange(await UiWorkflowDiagnostics.RunAsync());
            result.Checks.AddRange(await MotorSerialDiagnostics.RunAsync());
            result.Checks.AddRange(LogoDiagnostics.Run());
            result.Checks.AddRange(await AuditUiDiagnostics.RunAsync());
            result.Checks.AddRange(await AuditExcelDiagnostics.RunAsync());
            result.Checks.AddRange(AuditPrintDiagnostics.Run());
            result.Checks.AddRange(await AuditStateDiagnostics.RunAsync());
            result.Checks.AddRange(AiScheduleDiagnostics.Run());
            result.Checks.AddRange(GeminiCredentialDiagnostics.Run());
            result.Checks.AddRange(DispatchReadinessDiagnostics.Run());
            result.Success=true;
        }
        catch(Exception e){result.Error=e.ToString();}
        return result;
    }
}
