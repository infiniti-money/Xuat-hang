using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>All writes are to a disposable Excel workbook, never warehouse files.</summary>
public static class DistinctPackageExcelDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool pass,string message)
        {
            if(!pass)throw new InvalidOperationException("Excel package identity failure: "+message);
            checks.Add("Cont identity: "+message);
        }
        var folder=Path.Combine(Path.GetTempPath(),"hisa-package-identity-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var master=Path.Combine(folder,"Tổng hợp.xlsx");
            using(var wb=new XLWorkbook())
            {
                var cont=wb.AddWorksheet("Cont đi Hưng Yên");
                cont.Cell(1,1).Value="Ngày cont đi";cont.Cell(1,2).Value="Tên công trình";
                cont.Cell(1,3).Value="Tên công ty";cont.Cell(1,4).Value="Số hợp đồng";
                cont.Cell(1,5).Value="Kiện hàng";cont.Cell(1,6).Value="Tên vật tư";
                cont.Cell(1,7).Value="ĐVT";cont.Cell(1,8).Value="Số lượng";
                var details=wb.AddWorksheet("chi tiết");details.Cell(2,2).Value="Ngày cont về";
                wb.SaveAs(master);
            }
            var manualMaster=Path.Combine(folder,"Tổng hợp-thucong.xlsx");
            File.Copy(master,manualMaster);
            const string day="2026-10-31";
            Package Make(string id,string cont,string project,decimal qty,string sourceDay="2026-10-29")
            {
                return new Package {
                    Id=id,OriginPackageId=id,ContainerNumber=cont,
                    Contract="55052026",Project=project,Company="HISA",SourceDate=sourceDay,
                    Items=[new MaterialLine {
                        Contract="55052026",Project=project,Company="HISA",
                        Material="Đầu cửa tầng 700",Unit="Bộ",Quantity=qty,IsHungYen=true
                    }]
                };
            }
            var first=Make("pkg-cont-01","CONT-01","Công trình A",1,"2026-10-26");
            var second=Make("pkg-cont-02","CONT-02","Công trình A",1,"2026-10-27");
            var third=Make("pkg-cont-03","CONT-03","Công trình A",2,"2026-10-28");
            var t1=Make("pkg-t1","CONT-04","Công trình B T1",1);
            var t2=Make("pkg-t2","CONT-04","Công trình B T2",1);
            var excel=new ExcelStore();
            var date=DateTime.ParseExact(day,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
            // Manual input historically replaced all same-identity packages on the same
            // arrival day. It must now append both departures instead of overwriting.
            var manualOne=excel.RecordArrival(manualMaster,"", [first],date,useKt:false);
            var manualTwo=excel.RecordArrival(manualMaster,"", [second],date,useKt:false);
            var manualSaved=excel.ReadMaster(manualMaster).DetailPackages;
            Check(manualOne.Packages==1 && manualTwo.Packages==1 &&
                manualSaved.Count==2 &&
                manualSaved.Any(x=>x.Project=="Công trình A (26.10)") &&
                manualSaved.Any(x=>x.Project=="Công trình A (27.10)"),
                "manual arrival keeps same contract/material imported from two departure days");
            var manualRepeat=excel.RecordArrival(manualMaster,"",[first],date,useKt:false);
            Check(manualRepeat.Packages==0 &&
                excel.ReadMaster(manualMaster).DetailPackages.Count==2,
                "manual reimport of existing source package does not overwrite another departure");

            excel.RecordSelectedArrival(master,[first],date);
            excel.RecordSelectedArrival(master,[second],date);
            excel.RecordSelectedArrival(master,[third,t1,t2],date);
            var saved=excel.ReadMaster(master).DetailPackages;
            Check(saved.Count==5,"five packages survive independent imports under same contract/date");
            Check(saved.Count(x=>Rules.ProjectCode(x.Project)==Rules.ProjectCode("Công trình A"))==3 &&
                saved.Where(x=>Rules.ProjectCode(x.Project)==Rules.ProjectCode("Công trình A"))
                    .Select(x=>x.OriginPackageId).Distinct().Count()==3,
                "identical contract/project/quantity from different containers remains separate");
            Check(saved.Any(x=>x.OriginPackageId=="pkg-cont-03" && x.Items.Single().Quantity==2),
                "different material quantities keep their package boundary");
            Check(saved.Any(x=>Rules.ProjectCode(x.Project)==Rules.ProjectCode("Công trình B T1")) &&
                saved.Any(x=>Rules.ProjectCode(x.Project)==Rules.ProjectCode("Công trình B T2")),
                "T1 and T2 do not merge despite identical contract and quantity");
            Check(saved.Any(x=>x.Project=="Công trình A (26.10)" &&
                x.SourceDate=="2026-10-26" && x.ContainerNumber=="CONT-01") &&
                saved.Any(x=>x.Project=="Công trình A (27.10)" &&
                x.SourceDate=="2026-10-27" && x.ContainerNumber=="CONT-02"),
                "same contract and quantity but different departure days show distinct (dd.MM) labels");
            Check(saved.Any(x=>x.Project=="Công trình B T1 (29.10)") &&
                saved.Any(x=>x.Project=="Công trình B T2 (29.10)"),
                "suffix keeps T1 and T2 alongside visible departure date");
            Check(Rules.Identity("55052026","Công trình B T1 (29.10)","HISA") ==
                Rules.Identity("55052026","Công trình B T1","HISA") &&
                Rules.Identity("55052026","Công trình B T1","HISA") !=
                Rules.Identity("55052026","Công trình B T2 (29.10)","HISA"),
                "generated departure date does not change project matching or merge tower names");
            Check(Rules.ProjectWithContDate("Công trình B (Thang 1)","2026-10-26") ==
                "Công trình B (Thang 1) (26.10)" &&
                Rules.ProjectWithContDate("Công trình B (1)","2026-10-27") ==
                "Công trình B (1) (27.10)",
                "parenthetical tower numbering stays intact when adding short date");
            Check(saved.Any(x=>x.ContainerNumber=="CONT-01") &&
                saved.Any(x=>x.ContainerNumber=="CONT-02"),
                "container numbers round-trip through hidden Excel metadata");
            using(var wb=new XLWorkbook(master))
            {
                var ws=wb.Worksheet("chi tiết");
                Check(ws.Cell(2,27).GetString()=="HISA_CONTAINER" &&
                    ws.Cell(2,28).GetString()=="HISA_SOURCE_PACKAGE" &&
                    ws.Cell(2,29).GetString()=="HISA_CONT_DEPARTURE" &&
                    ws.Column(27).IsHidden && ws.Column(28).IsHidden && ws.Column(29).IsHidden,
                    "metadata kept hidden in AA/AB without changing user-visible A:J columns");
            }
            var rejected=false;
            try { excel.RecordSelectedArrival(master,[second],date); }
            catch(InvalidOperationException) { rejected=true; }
            Check(rejected,"reimport of an already logged source package is rejected");
            Check(excel.ReadMaster(master).DetailPackages.Count==5,
                "failed reimport does not remove or overwrite any existing package");
            return checks;
        }
        finally { try { Directory.Delete(folder,true); }catch(IOException){} }
    }
}
