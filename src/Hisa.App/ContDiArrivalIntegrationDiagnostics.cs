using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>End-to-end source folder to master detail test on fake Excel only.</summary>
public static class ContDiArrivalIntegrationDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var checks=new List<string>();
        void Check(bool ok,string label)
        {
            if(!ok)throw new InvalidOperationException("CONT ĐI integration: "+label);
            checks.Add("CONT ĐI: "+label);
        }
        var workspace=SelfTests.CreateDemoWorkspace();
        var source=Path.Combine(workspace,"Container");
        Directory.CreateDirectory(source);
        var settings=new AppSettings
        {
            MasterPath=Path.Combine(workspace,"Tổng hợp.xlsx"),
            KtPath=Path.Combine(workspace,"KT tổng hợp.xlsx"),
            ProductExportPath=Path.Combine(workspace,"Thành phẩm xuất kho.xlsx"),
            DataSources=[source],AutoRefresh=false,AutoSyncCont=false,AutoSyncKt=false
        };
        File.WriteAllText(Path.Combine(workspace,"native_data","settings.json"),
            System.Text.Json.JsonSerializer.Serialize(settings,WarehouseService.Json));
        using var service=new WarehouseService(workspace);
        await service.InitializeAsync();
        Check(service.ContDiLookupSnapshot.ContPackages.Count==0,
            "Startup never scans Container or uses historical master Cont rows");
        await service.RefreshContDiForArrivalAsync();
        Check(service.ContDiLookupSnapshot.ContPackages.Count==0,
            "Empty Container directory cannot authorize historical master packages");

        var month=Path.Combine(source,"29.10");
        Directory.CreateDirectory(month);
        var sent=Path.Combine(source,"HANGGUI_29.xlsx");
        File.WriteAllText(sent,"Do not import sent goods");
        var original=Path.Combine(month,"29102026.xlsx");
        using(var book=new XLWorkbook())
        {
            var ws=book.AddWorksheet("Kho");
            ws.Cell(1,1).Value="Kiện";ws.Cell(1,2).Value="Tên vật tư";
            ws.Cell(1,3).Value="ĐVT";ws.Cell(1,4).Value="Số lượng";
            ws.Cell(2,1).Value="HƯNG YÊN";ws.Cell(2,2).Value="Máy kéo HISA";
            ws.Cell(2,3).Value="Bộ";ws.Cell(2,4).Value=2;
            ws.Cell(3,2).Value="Ray dẫn hướng";ws.Cell(3,3).Value="mét";
            ws.Cell(3,4).Value=12;
            book.SaveAs(original);
        }
        // A separate schedule metadata file maps the source contract to its
        // correct project/company, rather than guessing from its numeric name.
        using(var book=new XLWorkbook())
        {
            var ws=book.AddWorksheet("Ngày");
            ws.Cell(1,3).Value="Công trình";ws.Cell(1,4).Value="Đơn vị";
            ws.Cell(1,5).Value="Hợp đồng";
            ws.Cell(2,3).Value="Dự án Hưng Yên T1";
            ws.Cell(2,4).Value="Công ty A";ws.Cell(2,5).Value="29102026";
            book.SaveAs(Path.Combine(workspace,"Lịch xuất hàng 29.10.xlsx"));
        }
        await service.RefreshContDiForArrivalAsync();
        var fresh=service.ContDiLookupSnapshot;
        Check(fresh.ContPackages.Count==1 &&
              fresh.ContPackages[0].SourceFile=="29102026.xlsx" &&
              fresh.ContPackages[0].Items.Count==2,
            "Fresh Container workbook supplies exact Hưng Yên goods and source filename");
        const string actualDay="2026-11-03";
        var requests=ContDiArrivalLookup.FromContracts("29102026\n88882026");
        var found=ContDiArrivalLookup.Preview(requests,fresh,actualDay);
        Check(found.Count(x=>x.Status==ContDiArrivalLookup.Ready)==1 &&
              found.Any(x=>x.Contract=="88882026"&&x.SourcePackageId.Length==0),
            "Manual contract search matches new physical source and reports missing contract");
        var chosen=found.Where(x=>x.Status==ContDiArrivalLookup.Ready).ToList();
        var selected=ContDiArrivalLookup.SelectVerified(chosen,requests,fresh,actualDay);
        var output=await service.RecordVerifiedArrivalAsync(selected,
            new DateTime(2026,11,3),false);
        Check(output.Packages==1&&output.Contracts==1&&output.Lines==2,
            "Selected physical Container package copies only its Hưng Yên materials into master");
        var after=new ExcelStore().ReadMaster(service.Settings.MasterPath);
        Check(after.DetailPackages.Any(x=>x.Contract=="29102026"&&
              x.ArrivalDate==actualDay&&x.OriginPackageId==selected[0].Id&&
              x.Items.Count==2),
            "Master chi tiết records confirmed actual arrival date, original source ID and two goods");
        var archived=Path.Combine(workspace,"Đã nhập kho","29.10","29102026.xlsx");
        Check(File.Exists(archived)&&!File.Exists(original),
            "Confirmed workbook moved to sibling Đã nhập kho preserving the source date folder");
        Check(!File.Exists(sent) && File.Exists(Path.Combine(workspace,"Hàng gửi","HANGGUI_29.xlsx")),
            "HANGGUI file is excluded from materials and moved to sibling Hàng gửi");
        using(var workbook=new XLWorkbook(service.Settings.MasterPath))
        {
            var detail=workbook.Worksheet("chi tiết");
            var arrival=after.DetailPackages.Single(x=>x.OriginPackageId==selected[0].Id);
            var line=arrival.SourceRowStart;
            Check(detail.Cell(line,2).DataType==XLDataType.DateTime &&
                  detail.Cell(line,5).DataType==XLDataType.Number &&
                  detail.Cell(line,5).GetDouble()==29102026,
                "Detail B is native Excel date and E is numeric contract");
        }
        await service.RefreshContDiForArrivalAsync();
        Check(service.ContDiLookupSnapshot.ContPackages.Count==0 &&
              service.Snapshot.DetailPackages.Any(x=>x.OriginPackageId==selected[0].Id),
            "Archived workbook is excluded from active Container scan; saved receipt remains");

        // A contract workbook may have multiple separate Hưng Yên packages:
        // a numeric marker is NOT a Hưng Yên destination. Moving the file
        // after receiving the first package would lose the pending second one.
        var partialFile=Path.Combine(month,"32452026.xlsx");
        using(var book=new XLWorkbook())
        {
            var ws=book.AddWorksheet("Kho");
            ws.Cell(1,1).Value="Kiện";ws.Cell(1,2).Value="Tên vật tư";
            ws.Cell(1,3).Value="ĐVT";ws.Cell(1,4).Value="Số lượng";
            ws.Cell(2,1).Value="HƯNG YÊN";ws.Cell(2,2).Value="Máy kéo";
            ws.Cell(2,3).Value="Bộ";ws.Cell(2,4).Value=1;
            ws.Cell(3,1).Value="1";ws.Cell(3,2).Value="Kiện đánh số không nhập";
            ws.Cell(3,3).Value="Bộ";ws.Cell(3,4).Value=5;
            ws.Cell(4,1).Value="hung yen";ws.Cell(4,2).Value="Tủ điện";
            ws.Cell(4,3).Value="Bộ";ws.Cell(4,4).Value=2;
            book.SaveAs(partialFile);
        }
        using(var book=new XLWorkbook(Path.Combine(workspace,"Lịch xuất hàng 29.10.xlsx")))
        {
            var ws=book.Worksheet("Ngày");
            ws.Cell(3,3).Value="Dự án Hưng Yên T2";
            ws.Cell(3,4).Value="Công ty A";
            ws.Cell(3,5).Value="32452026";
            book.Save();
        }
        await service.RefreshContDiForArrivalAsync();
        var partialRequest=ContDiArrivalLookup.FromContracts("32452026");
        var partialRows=ContDiArrivalLookup.Preview(partialRequest,service.ContDiLookupSnapshot,actualDay);
        Check(partialRows.Count(x=>x.Status==ContDiArrivalLookup.Ready)==2 &&
              partialRows.All(x=>!x.MaterialSummary.Contains("Kiện đánh số")),
            "Two marked Hưng Yên packages remain separate; numeric package marker is excluded");
        var firstPackage=ContDiArrivalLookup.SelectVerified([partialRows[0]],partialRequest,
            service.ContDiLookupSnapshot,actualDay);
        await service.RecordVerifiedArrivalAsync(firstPackage,new DateTime(2026,11,3),false);
        Check(File.Exists(partialFile) &&
              service.Snapshot.DetailPackages.Count(x=>x.Contract=="32452026"&&x.OriginPackageId.Length>0)==1,
            "Receiving only first Hưng Yên package never moves or imports pending second package");
        await service.RefreshContDiForArrivalAsync();
        partialRows=ContDiArrivalLookup.Preview(partialRequest,service.ContDiLookupSnapshot,actualDay);
        var second=partialRows.Single(x=>x.Status==ContDiArrivalLookup.Ready);
        var secondPackage=ContDiArrivalLookup.SelectVerified([second],partialRequest,
            service.ContDiLookupSnapshot,actualDay);
        await service.RecordVerifiedArrivalAsync(secondPackage,new DateTime(2026,11,3),false);
        Check(!File.Exists(partialFile) &&
              File.Exists(Path.Combine(workspace,"Đã nhập kho","29.10","32452026.xlsx")) &&
              service.Snapshot.DetailPackages.Count(x=>x.Contract=="32452026"&&x.OriginPackageId.Length>0)==2,
            "Source workbook archives only after all its Hưng Yên package IDs are received");
        return checks;
    }
}
