using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Exercise the three production-list schemas using only temporary Excel files.</summary>
public static class EquipmentListDiagnostics
{
    public static List<string> Run(string testWorkspace)
    {
        var results=new List<string>();
        void Check(bool ok,string name)
        {
            if(!ok)throw new InvalidOperationException("Equipment list test failed: "+name);
            results.Add("Production equipment list: "+name);
        }
        var folder=Path.Combine(testWorkspace,"production-lists-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var electric=Path.Combine(folder,"TỦ ĐIỆN.xlsx");
        var motor=Path.Combine(folder,"MÁY KÉO.xlsx");
        var car=Path.Combine(folder,"Đầu cửa car T10.2026.xlsx");
        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("Lịch tủ điện tổng hợp");
            ws.Cell(1,1).Value="Bàn giao tủ điện xuất kho Hưng Yên";
            foreach(var (c,v) in new[]{(1,"STT"),(2,"Công Ty"),(3,"Công Trình"),(4,"SHĐ"),
                (5,"Seri"),(6,"Điện áp phanh"),(7,"Loại Máy Kéo"),(8,"Tủ điện"),(9,"Lịch")})
                ws.Cell(2,c).Value=v;
            ws.Cell(3,1).Value=1;ws.Cell(3,2).Value="HISA";ws.Cell(3,3).Value="Công trình T1";
            ws.Cell(3,4).Value=12342026;ws.Cell(3,5).Value=2504001;
            ws.Cell(3,6).Value=100;ws.Cell(3,7).Value="Máy kéo Fuji";
            ws.Cell(3,9).Value="Dự kiến tháng 10";
            wb.AddWorksheet("TEM").Cell(1,1).Value="Không được trộn template";
            wb.SaveAs(electric);
        }
        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("DS máy kéo");
            foreach(var (c,v) in new[]{(1,"STT"),(2,"Tên công trình"),(3,"Công ty"),
                (4,"HĐ"),(5,"Tên vật tư"),(6,"ĐVT"),(7,"SL"),(8,"Số Lô"),
                (9,"số seri"),(10,"Ngày cont đi")})
                ws.Cell(2,c).Value=v;
            ws.Cell(3,1).Value=1;ws.Cell(3,2).Value="Công trình T1";ws.Cell(3,3).Value="HISA";
            ws.Cell(3,4).Value="12342026";ws.Cell(3,5).Value="Máy kéo Montanary";
            ws.Cell(3,6).Value="Cái";ws.Cell(3,7).Value=1;ws.Cell(3,8).Value="Lô 999";
            ws.Cell(3,9).Value=123456;ws.Cell(3,10).Value="DXH";
            ws.Cell(2,11).Value="Ngày xuất kho";ws.Cell(3,11).Value="Đã làm";
            ws.Cell(4,1).Value=2;ws.Cell(4,2).Value="Công trình T2";ws.Cell(4,3).Value="HISA";
            ws.Cell(4,4).Value="12342026";ws.Cell(4,5).Value="Máy kéo T2";
            ws.Cell(4,6).Value="Cái";ws.Cell(4,7).Value=2;
            wb.AddWorksheet("In tem auto").Cell(1,1).Value="#REF!";
            wb.SaveAs(motor);
        }
        using(var wb=new XLWorkbook())
        {
            foreach(var name in new[]{"01-10","03-10"})
            {
                var ws=wb.AddWorksheet(name);
                foreach(var (c,v) in new[]{(1,"TT"),(2,"Ngày xuất xưởng"),
                    (3,"Tên công trình"),(4,"Tên cty"),(5,"Số HĐ"),
                    (6,"Tên Vật tư"),(7,"Mã VT"),(8,"Mã Kho"),(9,"ĐVT"),(10,"SL")})
                    ws.Cell(2,c).Value=v;
                ws.Cell(3,1).Value=1;ws.Cell(3,2).Value=new DateTime(2026,10,1);
                ws.Cell(3,3).Value="Công trình T1";ws.Cell(3,4).Value="HISA";
                ws.Cell(3,5).Value="12342026";ws.Cell(3,6).Value="Đầu cửa car 700";
                ws.Cell(3,7).Value="C10408";ws.Cell(3,8).Value="kho 1";
                ws.Cell(3,9).Value="Bộ";ws.Cell(3,10).Value=1;
                ws.Cell(4,1).Value="Tổng";ws.Cell(4,10).Value=1;
            }
            wb.AddWorksheet("TEM").Cell(1,1).Value="Bản in không được quét";
            wb.SaveAs(car);
        }
        var paths=new AppSettings{
            ElectricalCabinetListPath=electric,TractionMachineListPath=motor,CarDoorListPath=car
        };
        var before=new[]{new FileInfo(electric).Length,new FileInfo(motor).Length,new FileInfo(car).Length};
        var catalog=EquipmentListReader.Load(paths);
        Check(catalog.Warnings.Count==0 && catalog.Rows.Count==5,
            "Three Excel layouts parsed; no summary rows or print/template sheets");
        Check(catalog.Rows.Count(x=>x.Category=="Đầu cửa car")==2 &&
              catalog.Rows.Count(x=>x.Category=="Máy kéo")==2 &&
              catalog.Rows.Count(x=>x.Category=="Tủ điện")==1,
            "Day-based car sheets separated; electrical and machine lists read only canonical first sheet");
        var chosen=new AiScheduleLine{Contract="12342026",Project="Công trình T1",Company="HISA"};
        var matched=catalog.Find(chosen);
        Check(matched.Count==5 && matched.Count(x=>x.Verification.StartsWith("Khớp"))==4 &&
              matched.Single(x=>x.Material=="Máy kéo T2").Verification.Contains("Chỉ khớp số HĐ"),
            "Same contract T1/T2 must be marked for manual verification, not silently combined");
        Check(matched.Single(x=>x.Category=="Tủ điện").Serial=="2504001" &&
              matched.Single(x=>x.Material=="Máy kéo Montanary").Serial=="123456" &&
              matched.Single(x=>x.Material=="Máy kéo Montanary").Lot=="Lô 999",
            "Serial number and batch number are preserved as searchable text");
        Check(matched.Count(x=>x.Category=="Đầu cửa car" && x.ProductionDate=="2026-10-01" &&
              x.Quantity==1 && x.Code=="C10408")==2,
            "Real manufacturing dates, quantities and material codes come from door workbook");
        Check(catalog.Rows.Single(x=>x.Category=="Máy kéo" &&
                x.Serial=="123456").MotorColumnK=="Đã làm",
            "Production machine status is read from column K, not old column N");
        Check(matched.Single(x=>x.Category=="Tủ điện").Quantity is null,
            "Missing electrical cabinet quantity is NOT fabricated as one");
        var again=EquipmentListReader.Load(paths);
        Check(again.Rows.Count==catalog.Rows.Count &&
              again.Find(chosen).Count==matched.Count,
            "Cached lookups preserve matching records across repeated reads");
        Check(new[]{new FileInfo(electric).Length,new FileInfo(motor).Length,new FileInfo(car).Length}
                .SequenceEqual(before),
            "Read-only list import does not change source workbook files");
        // The canonical master Excel and production workbooks live in separate
        // directories. HISA should read from wherever the user actually stores
        // the three lists, without relocating or rewriting the originals.
        var masterFolder=Path.Combine(folder,"Kho chính");
        var contFolder=Path.Combine(folder,"CONT ĐI");
        Directory.CreateDirectory(masterFolder);
        Directory.CreateDirectory(contFolder);
        var master=Path.Combine(masterFolder,"Tổng hợp.xlsx");
        using(var workbook=new XLWorkbook())
        {
            workbook.AddWorksheet("Cont đi Hưng Yên");
            workbook.SaveAs(master);
        }
        paths.MasterPath=master;
        paths.DataSources=[contFolder];
        EquipmentListReader.ValidateIndependentSources(paths);
        Check(EquipmentListReader.ConfiguredPaths(paths).Count()==3 &&
              EquipmentListReader.IsConfiguredEquipmentFile(motor,paths),
            "Three external Excel paths remain independently configured from master warehouse path");
        var originalEquipmentPaths=EquipmentListReader.ConfiguredPaths(paths).ToArray();
        // A moved master data source does not relocate or overwrite the
        // independently configured production catalogues.
        var secondMaster=Path.Combine(folder,"Kho chính khác");
        Directory.CreateDirectory(secondMaster);
        paths.MasterPath=Path.Combine(secondMaster,"Tổng hợp.xlsx");
        EquipmentListReader.ValidateIndependentSources(paths);
        Check(EquipmentListReader.ConfiguredPaths(paths).SequenceEqual(originalEquipmentPaths) &&
              new[]{new FileInfo(electric).Length,new FileInfo(motor).Length,new FileInfo(car).Length}
                  .SequenceEqual(before),
            "Changing main workbook folder preserves all production file paths and bytes");
        paths.MasterPath=master;
        bool Rejects(AppSettings input)
        {
            try{EquipmentListReader.ValidateIndependentSources(input);return false;}
            catch(InvalidOperationException){return true;}
        }
        var sameDirectory=Path.Combine(masterFolder,"MÁY KÉO.xlsx");
        File.Copy(motor,sameDirectory);
        var incorrect=new AppSettings
        {
            MasterPath=master,DataSources=[contFolder],TractionMachineListPath=sameDirectory
        };
        Check(Rejects(incorrect) && File.Exists(sameDirectory),
            "Selecting a machine list alongside Tổng hợp.xlsx is rejected without moving the Excel file");
        // Older installations may have equipment files inside CONT ĐI:
        // source scan must ignore the catalogue even before paths are fixed.
        var mixed=Path.Combine(contFolder,"MÁY KÉO.xlsx");
        File.Copy(motor,mixed);
        var legacy=new AppSettings
        {
            MasterPath=master,DataSources=[contFolder],TractionMachineListPath=mixed
        };
        Check(Rejects(legacy),
            "Production Excel inside CONT ĐI is not accepted as independent source");
        var scanned=new ExcelStore().ScanSources(legacy);
        Check(scanned.FileCount==0 && scanned.Packages.Count==0 &&
              File.Exists(mixed),
            "Existing nested equipment Excel is excluded from container scanning and never touched");
        var duplicated=new AppSettings
        {
            MasterPath=master,DataSources=[contFolder],
            ElectricalCabinetListPath=electric,TractionMachineListPath=electric
        };
        Check(Rejects(duplicated),
            "Using the same physical file as two production catalogues is rejected");
                var empty=EquipmentListReader.Load(new AppSettings());
        Check(empty.Rows.Count==0 && empty.Warnings.Count==0,
            "No linked production lists does not break the Zalo workflow");
        return results;
    }
}
