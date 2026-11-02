using System.IO;
using ClosedXML.Excel;

namespace Hisa.App;

/// <summary>Repeatable fixtures. Only files below the caller-provided test folder are touched.</summary>
public static class ExcelDiagnostics
{
    public static List<string> Run(string testRoot)
    {
        var root=Path.Combine(Path.GetFullPath(testRoot),"excel-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var evidence=new List<string>();var store=new ExcelStore { BackupDirectory=Path.Combine(root,"Backups","Excel") };var master=Path.Combine(root,"Tổng hợp.xlsx");var kt=Path.Combine(root,"KT tổng hợp.xlsx");
        using(var wb=new XLWorkbook())
        {
            var cont=wb.AddWorksheet("Cont đi Hưng Yên");Headers(cont,1,["Ngày cont đi","Công trình","Công ty","Số HĐ","Kiện","Tên vật tư","ĐVT","Số lượng","Ghi chú"]);
            int row=2;
            for(int block=0;block<2;block++)
            {
                int start=row;
                foreach(var material in new[]{"Máy kéo","Ray dẫn hướng T78"})
                {
                    cont.Cell(row,1).Value=new DateTime(2026,10,15);cont.Cell(row,1).Style.NumberFormat.Format="dd/MM/yyyy";
                    cont.Cell(row,2).Value="Công trình A";cont.Cell(row,3).Value="Công ty A";cont.Cell(row,4).Value=5492026;cont.Cell(row,4).Style.NumberFormat.Format="00000000";
                    cont.Cell(row,5).Value=row==start?"HƯNG YÊN":"";cont.Cell(row,6).Value=material;cont.Cell(row,7).Value="Bộ";cont.Cell(row,8).Value=1;row++;
                }
                cont.Range(start,5,row-1,5).Merge();
            }
            cont.Cell(row,4).Value=99992026;cont.Cell(row,5).Value="HÀ NỘI";cont.Cell(row,6).Value="Tủ điện";cont.Cell(row,7).Value="Bộ";
            var detail=wb.AddWorksheet("chi tiết");detail.Cell(2,2).Value="Ngày cont về";
            row=3;
            for(int block=0;block<3;block++)
            {
                int start=row;
                foreach(var material in new[]{"Máy kéo","Ray dẫn hướng T78"})
                {
                    DetailRow(detail,row,block==2?new DateTime(2026,10,17):new DateTime(2026,10,16),"05492026","Công trình A","Công ty A",row==start?"HƯNG YÊN":"",material,1);row++;
                }
                detail.Range(start,6,row-1,6).Merge();
            }
            DetailRow(detail,row,new DateTime(2026,10,16),"99992026","Hà Nội","A","HÀ NỘI","Tủ điện",99);
            var keep=wb.AddWorksheet("Không được thay đổi");keep.Cell(1,1).Value="Dữ liệu riêng";keep.Cell(2,1).FormulaA1="1+2";
            wb.SaveAs(master);
        }
        var snap=store.ReadMaster(master);
        Check(snap.ContPackages.Count==2,"Cont: giữ hai khối HY giống hợp đồng/ngày",evidence);
        Check(snap.DetailPackages.Count==3,"Chi tiết: giữ ba kiện qua hai ngày cont về",evidence);
        Check(snap.Packages.Count==3,"Ghép cont / chi tiết một-một, không nhân đôi hay gộp kiện",evidence);
        Check(snap.Packages.All(x=>x.Contract=="05492026"),"Giữ định dạng số 0 đầu và chỉ lấy Hưng Yên",evidence);
        Check(snap.Packages.Sum(x=>x.Items.Count)==6,"Giữ đủ danh mục trong từng kiện",evidence);

        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("chi tiết (2)");ws.Cell(2,2).Value="Ngày cont về";
            DetailRow(ws,3,new DateTime(2026,10,20),"05492026","Công trình A","Công ty A","HƯNG YÊN","Máy kéo",5);
            DetailRow(ws,4,new DateTime(2026,10,20),"05492026","Công trình A","Công ty A","","Ray dẫn hướng T78",10);
            ws.Range(3,6,4,6).Merge();
            wb.SaveAs(kt);
        }
        var compared=store.CompareWithKt(kt,snap.ContPackages);
        Check(compared.Count==1&&compared[0].Status=="mismatch"&&compared[0].KtOnly.Count==2,"KT đối chiếu theo vật tư, đơn vị và số lượng",evidence);
        var arrived=store.RecordArrival(master,kt,snap.ContPackages,new DateTime(2026,10,27),false);
        Check(arrived.Packages==2&&arrived.Lines==4,"Nhập kho nhiều kiện với ngày cont về",evidence);
        var after=store.ReadMaster(master);
        Check(after.DetailPackages.Count==5&&after.DetailPackages.Count(x=>x.ArrivalDate=="2026-10-27")==2,"Nhập kho giữ nguyên các ngày cont về trước",evidence);
        store.RecordArrival(master,kt,snap.ContPackages,new DateTime(2026,10,27),false);
        Check(store.ReadMaster(master).DetailPackages.Count==5,"Nhập lại cùng ngày không tạo kiện trùng",evidence);
        Check(Directory.Exists(store.BackupDirectory)&&Directory.EnumerateFiles(store.BackupDirectory,"*.xlsx").Any()&&!Directory.Exists(Path.Combine(root,"HISA backups")),"Tạo sao lưu Excel trong thư mục Backups riêng, không ghi cạnh nguồn",evidence);
        store.ApplyKt(master,kt);
        after=store.ReadMaster(master);
        Check(after.DetailPackages.Count==6&&after.DetailPackages.Any(x=>x.ArrivalDate=="2026-10-20"&&x.Items.Any(i=>i.Quantity==5))&&after.DetailPackages.Any(x=>x.ArrivalDate=="2026-10-16"),"KT chỉ bổ sung kiện còn thiếu, giữ nguyên dữ liệu cont đã nhập và ngày/số lượng của KT",evidence);
        using(var wb=new XLWorkbook(master)) Check(wb.Worksheet("Không được thay đổi").Cell(1,1).GetString()=="Dữ liệu riêng"&&wb.Worksheet("Không được thay đổi").Cell(2,1).HasFormula,"Giữ dữ liệu và công thức các trang không liên quan",evidence);

        var source=Path.Combine(root,"CONT ĐI");Directory.CreateDirectory(source);
        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("HY");Headers(ws,1,["Kiện","Tên vật tư","ĐVT","Số lượng","Ghi chú"]);
            Headers(ws,2,["HƯNG YÊN","Máy kéo","Bộ","1",""]);Headers(ws,3,["HƯNG YÊN","Ray dẫn hướng T78","mét","10",""]);
            var other=wb.AddWorksheet("HN");Headers(other,1,["Kiện","Tên vật tư","ĐVT","Số lượng","Ghi chú"]);Headers(other,2,["HÀ NỘI","Tủ điện","Bộ","99",""]);
            wb.SaveAs(Path.Combine(source,"29102026.xlsx"));
        }
        var settings=new AppSettings {MasterPath=master,KtPath=kt,ProductExportPath=Path.Combine(root,"Thành phẩm xuất kho.xlsx"),DataSources=[source]};
        var scan=store.ScanSources(settings);
        Check(scan.Packages.Count==2&&scan.Packages.Sum(x=>x.Items.Count)==2&&scan.Packages.All(x=>x.Items.All(i=>i.Material!="Tủ điện")),"Quét đa trang: chỉ lấy hai kiện HY, loại Hà Nội"+$" (files={scan.FileCount}, packages={scan.Packages.Count}, warnings={string.Join(";",scan.Warnings)})",evidence);
        Check(store.ScanSources(settings).ChangedFiles==0,"Lần quét kế tiếp dùng bộ nhớ đệm, không đọc lại file",evidence);
        store.WriteContSummary(master,scan.Packages);
        Check(store.ReadMaster(master).ContPackages.Count==2,"Ghi tổng hợp giữ ranh giới hai kiện Hưng Yên",evidence);
        store.WriteContSummary(master,[]);
        Check(store.ReadMaster(master).ContPackages.Count==2,"Nguồn rỗng không xóa dữ liệu tổng hợp",evidence);

        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("Sheet1");ws.Cell(1,1).Value="Theo dõi";Headers(ws,2,["STT","Tên công trình","Công ty","Hợp đồng","Ngày xuất","Ghi chú"]);
            ws.Cell(3,2).Value="Công trình cũ";ws.Cell(3,3).Value="Công ty cũ";ws.Cell(3,4).Value="11112026";ws.Cell(3,5).Value=new DateTime(2026,10,1);ws.Cell(3,5).Style.NumberFormat.Format="dd/MM/yyyy";
            wb.SaveAs(settings.ProductExportPath);
        }
        var onlyMotor=scan.Packages.SelectMany(p=>p.Items.Select(i=>
        {
            var line=i.Copy();
            line.PackageId=p.Id;
            line.Include=Rules.Classify(i.Material)=="Máy kéo";
            return line;
        })).ToList();
        var slip=new Slip {Id="fixture-slip",SlipCode="BB-HY-001",Contracts=["29102026"],
            HandoverDate="2026-10-27",Status="delivered",
            ContractPackages=scan.Packages,Lines=onlyMotor};
        store.SyncExportLedgers(settings,[slip]);
        using(var ledger=new XLWorkbook(Path.Combine(root,"XUẤT HÀNG","Sổ theo dõi xuất hàng.xlsx")))
        {
            var deliveredMaterials=ledger.Worksheet("Vật tư bàn giao");
            var deliveredPackages=ledger.Worksheet("Kiện Hưng Yên");
            Check(Enumerable.Range(2,Math.Max(0,(deliveredMaterials.LastRowUsed()?.RowNumber()??1)-1))
                  .All(n=>!Rules.Code(deliveredMaterials.Cell(n,7).GetString()).Contains("RAYDANHUONG")) &&
                  Enumerable.Range(2,Math.Max(0,(deliveredPackages.LastRowUsed()?.RowNumber()??1)-1))
                  .All(n=>!Rules.Code(deliveredPackages.Cell(n,9).GetString()).Contains("RAYDANHUONG")) &&
                  deliveredMaterials.LastRowUsed()?.RowNumber()>1,
                "Excluded ray group is absent from both handover material and physical-package export sheets",evidence);
        }
        var shipped=store.ReadShipments(settings);
        Check(shipped.Count==2&&shipped.Any(x=>x.SlipId==slip.Id)&&shipped.Any(x=>x.Contract=="11112026"),"Sổ thành phẩm thêm phiếu, giữ nguyên lịch sử cũ",evidence);
        store.SyncExportLedgers(settings,[slip]);
        Check(store.ReadShipments(settings).Count==2,"Đồng bộ lại không tạo dòng thành phẩm trùng",evidence);
        slip.Status="cancelled";store.SyncExportLedgers(settings,[slip]);
        Check(store.ReadShipments(settings).Count==1&&store.ReadShipments(settings)[0].Contract=="11112026","Hủy phiếu chỉ gỡ dòng của phiếu, giữ lịch sử cũ",evidence);
        var contractSources=Path.Combine(root,"source-contract-fixtures");Directory.CreateDirectory(contractSources);
        foreach(var (name,marker) in new[]{("BG47011026.xlsx","HƯNG YÊN"),("FUJITECHNOLOGY23502026.xlsx","")})
        {
            using var wb=new XLWorkbook();var ws=wb.AddWorksheet("Kiện");Headers(ws,1,["Kiện","Tên vật tư","ĐVT","Số lượng","Ghi chú"]);Headers(ws,2,[marker,"Máy kéo","Bộ","1",""]);wb.SaveAs(Path.Combine(contractSources,name));
        }
        using(var wb=new XLWorkbook())
        {
            var ws=wb.AddWorksheet("Tháng 10");ws.Cell(1,1).Value="Ngày Xuất Hàng: 27/10/2026";ws.Cell(2,1).Value="CONT HƯNG YÊN";Headers(ws,3,["1","Cty Thành Triều","Fujitechnology","25302026"]);wb.SaveAs(Path.Combine(contractSources,"THANG10-2026.xlsx"));
        }
        var contractScan=store.ScanSources(new AppSettings {DataSources=[contractSources],MasterPath=master,KtPath=kt});
        Check(contractScan.Packages.Any(x=>x.Contract=="470110.26"),"Mã BG dạng gọn được đổi đúng số hợp đồng thập phân",evidence);
        Check(contractScan.Packages.Any(x=>x.Contract=="25302026"&&x.Project=="Cty Thành Triều")&&!contractScan.Packages.Any(x=>x.Contract=="23502026"),"Tên file sai mã được đối chiếu manifest chỉ khi công ty duy nhất",evidence);
        return evidence;
    }
    private static void Check(bool condition,string message,List<string> evidence)
    { if(!condition)throw new InvalidOperationException("Excel fixture failed: "+message);evidence.Add("PASS: "+message); }
    private static void Headers(IXLWorksheet ws,int row,string[] values)
    { for(int col=0;col<values.Length;col++)ws.Cell(row,col+1).Value=values[col]; }
    private static void DetailRow(IXLWorksheet ws,int row,DateTime day,string contract,string project,string company,string marker,string material,int quantity)
    {
        ws.Cell(row,2).Value=day;ws.Cell(row,2).Style.NumberFormat.Format="dd/MM/yyyy";ws.Cell(row,3).Value=project;ws.Cell(row,4).Value=company;
        ws.Cell(row,5).Value=double.Parse(contract,System.Globalization.CultureInfo.InvariantCulture);ws.Cell(row,5).Style.NumberFormat.Format=new string('0',contract.Length);
        ws.Cell(row,6).Value=marker;ws.Cell(row,7).Value=material;ws.Cell(row,8).Value="Bộ";ws.Cell(row,9).Value=quantity;
    }
}
