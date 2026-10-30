using System.IO;

namespace Hisa.App;

/// <summary>State regression checks use a separate temporary workspace only.</summary>
public static class SlipStateDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException("FAIL: "+label);checks.Add("PASS: "+label);}
        async Task Reject(Func<Task> action,string label)
        {
            try{await action();}catch(ArgumentException){checks.Add("PASS: "+label);return;}
            throw new InvalidOperationException("FAIL: "+label);
        }
        var workspace=SelfTests.CreateDemoWorkspace();
        using var service=new WarehouseService(workspace);
        await service.InitializeAsync();
        Check(service.LastError.Length==0,"State fixture loads isolated native workbooks");
        var report=service.Search(new SearchFilter{ContractsText="05492026",ArrivalDate="2026-10-16",ProjectTargets=[new ContractRow{Contract="05492026",Project="Công trình A",Company="Công ty A"}],Categories=["Máy kéo"]});
        var draft=service.CreateDraft(report,"contract_delivery","2026-10-27");
        SerialTestData.Fill(draft,"STATE");
        var saved=await service.SaveSlipAsync(draft);var id=saved.Id;var code=saved.SlipCode;var created=saved.CreatedAt;var initialEvents=saved.StatusHistory.Count;var packageCount=saved.ContractPackages.Count;
        Slip Current()=>WarehouseService.Clone(service.Slips.Single(x=>x.Id==id));
        var edit=Current();edit.Name="Sửa tên và nội dung phiếu";edit.SlipCode="";edit.CreatedAt="2020-01-01";
        saved=await service.SaveSlipAsync(edit);
        Check(saved.SlipCode==code&&saved.CreatedAt==created&&saved.StatusHistory.Count==initialEvents,"Editing a waiting slip preserves code, creation date and status history");
        var bad=Current();bad.SlipCode="MA-KHAC";await Reject(async()=>{await service.SaveSlipAsync(bad);},"Existing slip code cannot be changed");
        bad=Current();bad.Status="delayed";bad.DelayReason="";await Reject(async()=>{await service.SaveSlipAsync(bad);},"New temporary delay requires a reason");
        bad=Current();bad.Status="cancelled";bad.CancelReason="";await Reject(async()=>{await service.SaveSlipAsync(bad);},"New cancellation requires a reason");
        await service.SaveFlagsAsync("05492026","Công trình B","delayed","Cảnh báo thủ công công trình khác");
        edit=Current();edit.Status="delayed";edit.DelayReason="Chờ xe nhận hàng";saved=await service.SaveSlipAsync(edit);
        var delayedAt=saved.DelayedAt;var delayedEvents=saved.StatusHistory.Count;
        Check(service.Flags.Count(x=>x.SlipId==id)==1&&service.Flags.Single(x=>x.SlipId==id).Project=="Công trình A","Editor transition to temporary delay creates exact project warning");
        Check(saved.StatusHistory.Last().Reason=="Chờ xe nhận hàng"&&saved.StatusHistory.Last().Status=="delayed","Delay history stores reason and timestamp");
        edit=Current();edit.DelayReason="Hẹn nhận vào ngày mai";saved=await service.SaveSlipAsync(edit);
        Check(saved.StatusHistory.Count==delayedEvents&&saved.DelayedAt==delayedAt&&service.Flags.Single(x=>x.SlipId==id).Note=="Hẹn nhận vào ngày mai","Same-state reason edit updates warning without duplicating transition");
        await service.ChangeSlipStatusAsync(id,"planned");
        Check(!service.Flags.Any(x=>x.SlipId==id)&&service.Flags.Any(x=>x.Project=="Công trình B"&&x.SlipId==""),"Returning to waiting clears only this slip's generated warnings");
        edit=Current();edit.Status="delivered";saved=await service.SaveSlipAsync(edit);var deliveryAt=saved.DeliveredAt;
        Check(new ExcelStore().ReadShipments(service.Settings).Any(x=>x.SlipId==id),"Editor transition to delivered writes canonical product ledger");
        edit=Current();edit.ContractPackages=[];edit.Note="Sửa ghi chú phiếu đã xuất";saved=await service.SaveSlipAsync(edit);
        Check(saved.ContractPackages.Count==packageCount&&saved.DeliveredAt==deliveryAt,"Editing delivered slip retains full packages and original delivery timestamp");
        edit=Current();edit.Status="cancelled";edit.CancelReason="Khách hủy kế hoạch";saved=await service.SaveSlipAsync(edit);var cancelledAt=saved.CancelledAt;var cancelEvents=saved.StatusHistory.Count;
        var ledger=new ExcelStore().ReadShipments(service.Settings);
        Check(saved.PreviousStatus=="delivered"&&!ledger.Any(x=>x.SlipId==id)&&ledger.Any(x=>Rules.Contract(x.Contract)=="99992026"),"Editor cancellation removes own export rows and preserves legacy rows");
        saved=await service.SaveSlipAsync(Current());await service.ChangeSlipStatusAsync(id,"cancelled","Khách hủy kế hoạch");saved=Current();
        Check(saved.PreviousStatus=="delivered"&&saved.CancelledAt==cancelledAt&&saved.StatusHistory.Count==cancelEvents,"Repeated cancellation does not corrupt restore state or add duplicate history");
        await service.ChangeSlipStatusAsync(id,"restore");
        Check(Current().Status=="delivered"&&new ExcelStore().ReadShipments(service.Settings).Any(x=>x.SlipId==id),"Restore returns to the actual prior state and restores export row");
        await service.ChangeSlipStatusAsync(id,"delayed","Hoãn từ nút trạng thái");edit=Current();edit.Status="planned";await service.SaveSlipAsync(edit);
        Check(!service.Flags.Any(x=>x.SlipId==id)&&service.Flags.Any(x=>x.Project=="Công trình B"&&x.SlipId==""),"Editor and dedicated status buttons share flag cleanup behavior");
        edit=Current();edit.Status="delivered";foreach(var line in edit.Lines)line.Include=false;
        await Reject(async()=>{await service.SaveSlipAsync(edit);},"Cannot export a slip without included material lines");
        edit=Current();edit.Status="delivered";
        using(var held=new FileStream(service.Settings.ProductExportPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            saved=await service.SaveSlipAsync(edit);
            Check(saved.ProductExportPending&&service.Slips.Single(x=>x.Id==id).Status=="delivered","Locked Excel ledger preserves saved state and marks export pending");
        }
        await service.RefreshAsync(false);
        Check(!service.Slips.Single(x=>x.Id==id).ProductExportPending&&new ExcelStore().ReadShipments(service.Settings).Any(x=>x.SlipId==id),"Pending canonical ledger write retries after Excel lock is released");
        using var reopened=new WarehouseService(workspace);await reopened.InitializeAsync();
        Check(reopened.Slips.Single(x=>x.Id==id).SlipCode==code&&reopened.Slips.Single(x=>x.Id==id).Status=="delivered","Four-state workflow persists across native application reopen");
        Check(Rules.StatusLabel("planned")=="Chờ xuất"&&Rules.StatusLabel("delivered")=="Đã xuất"&&Rules.StatusLabel("delayed")=="Tạm hoãn"&&Rules.StatusLabel("cancelled")=="Đã hủy","Four state labels match the Vietnamese application");
        return checks;
    }
}
