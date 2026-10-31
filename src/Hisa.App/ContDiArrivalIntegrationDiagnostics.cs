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
        var source=Path.Combine(workspace,"CONT ĐI");
        using var service=new WarehouseService(workspace);
        await service.InitializeAsync();
        Check(service.ContDiLookupSnapshot.ContPackages.Count==0,
            "Existing master Cont summary does not count as a physically scanned source");
        await service.RefreshContDiForArrivalAsync();
        Check(service.ContDiLookupSnapshot.ContPackages.Count==0,
            "Empty CONT ĐI directory cannot authorize old master packages");

        var month=Path.Combine(source,"29.10");
        Directory.CreateDirectory(month);
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
            "Fresh CONT ĐI workbook supplies exact Hưng Yên goods and source filename");
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
            "Selected physical package copies only its Hưng Yên materials into master");
        var after=new ExcelStore().ReadMaster(service.Settings.MasterPath);
        Check(after.DetailPackages.Any(x=>x.Contract=="29102026"&&
              x.ArrivalDate==actualDay&&x.OriginPackageId==selected[0].Id&&
              x.Items.Count==2),
            "Master chi tiết records confirmed actual arrival date, original source ID and two goods");
        await service.RefreshContDiForArrivalAsync();
        Check(ContDiArrivalLookup.Preview(requests,service.ContDiLookupSnapshot,actualDay)
              .Any(x=>x.Status=="Kiện đã nhập kho"),
            "Re-scan flags already imported source and cannot import it twice");
        return checks;
    }
}
