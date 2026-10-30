using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Hisa.App;

/// <summary>Audit regressions use only newly created temporary workspaces.</summary>
public static class AuditStateDiagnostics
{
    public static async Task<List<string>> RunAsync()
    {
        var evidence=new List<string>();
        void Check(bool ok,string text){if(!ok)throw new InvalidOperationException("FAIL: "+text);evidence.Add(text);}
        async Task Reject(Func<Task> action,string text)
        {
            try{await action();}catch(ArgumentException){evidence.Add(text);return;}catch(InvalidOperationException){evidence.Add(text);return;}
            throw new InvalidOperationException("FAIL: "+text);
        }
        var root=SelfTests.CreateDemoWorkspace();using var service=new WarehouseService(root);await service.InitializeAsync();
        var sourceFile=Path.Combine(root,"single-source.xlsx");File.WriteAllText(sourceFile,"not an Excel workbook");
        var fileSourceSettings=WarehouseService.Clone(service.Settings);fileSourceSettings.DataSources=[sourceFile];
        try{await service.SaveSettingsAsync(fileSourceSettings);throw new InvalidOperationException("A source file was accepted");}
        catch(DirectoryNotFoundException){evidence.Add("Source configuration rejects individual Excel files");}
        var sourceFolder=Path.Combine(root,"source-folder");Directory.CreateDirectory(sourceFolder);
        var folderSettings=WarehouseService.Clone(service.Settings);folderSettings.DataSources=[sourceFolder];await service.SaveSettingsAsync(folderSettings);
        Check(service.Settings.DataSources.SequenceEqual(new[]{sourceFolder}),"Source configuration saves a selected folder");
        var report=service.Search(new SearchFilter{ContractsText="05492026",ArrivalDate="2026-10-16",Categories=["Máy kéo"]});
        var draft=service.CreateDraft(report,"contract_delivery","2026-11-01");SerialTestData.Fill(draft,"AUDIT");
        var nullSerial=WarehouseService.Clone(draft);nullSerial.MotorSerials[0].SerialNumber=null!;
        Check(MotorSerials.BuildRows(nullSerial)[0].SerialNumber=="","Null persisted serial is treated as blank when reopening its table");
        MotorSerials.Validate(nullSerial,requireAll:false);
        Check(nullSerial.MotorSerials[0].SerialNumber==""&&MotorSerials.SerialText(nullSerial,nullSerial.Lines[0]).Length>0,"Partial serial and print helpers tolerate a null serial field");
        try{MotorSerials.Validate(nullSerial);throw new Exception("Missing serial was accepted");}catch(ArgumentException){evidence.Add("Null serial still blocks actual motor export");}
        draft.Lines[0].Quantity=1.5m;
        var waiting=await service.SaveSlipAsync(draft);
        Check(!waiting.ProductExportPending,"Fractional waiting draft with old serials does not block global ledger sync");
        Check(!MotorSerials.ActiveRows(waiting).Any(x=>x.LineId==waiting.Lines[0].Id),"Draft-only invalid motor count is excluded from serial audit output");

        var manual=service.CreateDraft(service.Search(new SearchFilter{ContractsText="05492026",ProjectTargets=[new ContractRow{Contract="05492026",Project="Công trình B",Company="Công ty A"}]}),"internal_summary","2026-11-03");
        manual.Lines.Add(new MaterialLine{Contract="20012026",Project="Công trình C",Company="Công ty A",Material="Tủ điện bổ sung",Unit="Bộ",Quantity=1});
        manual.Status="delivered";manual=await service.SaveSlipAsync(manual);
        Check(manual.Contracts.Any(c=>Rules.Contract(c)==Rules.Contract("20012026")),"Adding a manual material for another contract updates the slip contract list");
        Check(service.BuildDaily("2026-11-03").Contracts.Rows.Any(x=>Rules.Contract(x.Contract)==Rules.Contract("20012026")),"Daily report includes manually added contract material");
        Check(!manual.ProductExportPending,"Unrelated valid export succeeds despite an incomplete waiting motor draft");
        var template=new PrintTemplate{Id=Guid.NewGuid().ToString("N"),Name="Mẫu kiểm tra nền"};
        await Task.Run(()=>service.SaveTemplateAsync(template));
        Check(service.Templates.Any(x=>x.Id==template.Id),"Public template save validates WPF layout safely from a background MTA caller");

        var blank=service.CreateDraft(service.Search(new SearchFilter{ContractsText="05492026",ProjectTargets=[new ContractRow{Contract="05492026",Project="Công trình B",Company="Công ty A"}]}),"internal_summary","2026-11-04");
        blank.Lines.Add(new MaterialLine{Material="Tủ điện bổ sung",Unit="Bộ",Quantity=1});blank=await service.SaveSlipAsync(blank);
        Check(blank.Lines.Last().Contract==blank.Contracts[0]&&blank.Lines.Last().Project=="Công trình B","A manual blank contract is filled only when the identity is unambiguous");
        var motorIdentity=service.CreateDraft(service.Search(new SearchFilter{ContractsText="05492026",ArrivalDate="2026-10-16",Categories=["Máy kéo"]}),"internal_summary","2026-11-04");
        foreach(var line in motorIdentity.Lines){line.Project="";line.Company="";}
        SerialTestData.Fill(motorIdentity,"INFERRED");motorIdentity=await service.SaveSlipAsync(motorIdentity);
        Check(motorIdentity.Lines.All(x=>x.Project=="Công trình A")&&motorIdentity.MotorSerials.All(x=>x.Project=="Công trình A"),"Filling unambiguous missing project keeps existing motor serial metadata aligned");
        var mixed=service.CreateDraft(service.Search(new SearchFilter{ContractsText="05492026\n20012026"}),"internal_summary","2026-11-05");
        foreach(var line in mixed.Lines.Where(x=>Rules.Contract(x.Contract)==Rules.Contract("05492026")))line.Include=false;
        mixed.Status="delivered";mixed=await service.SaveSlipAsync(mixed);
        Check(mixed.ContractPackages.Any(x=>Rules.Contract(x.Contract)==Rules.Contract("05492026")),"Unchecked source snapshots remain retained for audit");
        Check(!service.Search(new SearchFilter{ContractsText="05492026"}).Rows.Any(x=>x.Slips.Any(s=>s.Id==mixed.Id)),"Unchecked contract is not falsely reported as exported by full snapshots");
        Check(service.BuildDaily("2026-11-05").Contracts.Rows.All(x=>Rules.Contract(x.Contract)!=Rules.Contract("05492026")),"Daily exported contract scope excludes unchecked source snapshots");

        var zip=Path.Combine(root,"backup.zip");await service.BackupAsync(zip);
        await service.SaveFlagsAsync("20012026","Công trình C","delayed","Ghi chú sau bản sao lưu đầu");
        await service.BackupAsync(zip);
        using(var archive=ZipFile.OpenRead(zip))
        using(var reader=new StreamReader(archive.GetEntry("contract_flags.json")!.Open()))
            Check(reader.ReadToEnd().Contains("Ghi chú sau bản sao lưu đầu"),"Approved overwrite replaces an existing backup with a complete fresh archive");
        var before=await File.ReadAllBytesAsync(zip);
        using(var held=new FileStream(service.Settings.ProductExportPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            try{await service.BackupAsync(zip);throw new Exception("Locked workbook backup unexpectedly succeeded");}
            catch(IOException){evidence.Add("Locked workbook returns a backup error without claiming success");}
        }
        var after=await File.ReadAllBytesAsync(zip);
        Check(before.SequenceEqual(after),"A failed backup preserves the previous valid ZIP byte for byte");
        Check(!Directory.EnumerateFiles(root,"backup.zip.*.tmp").Any(),"Incomplete temporary backup is cleaned up");
        await Reject(()=>service.BackupAsync(service.Settings.MasterPath),"Backup cannot overwrite the source workbook with ZIP bytes");
        var change=service.ChangeSlipStatusAsync(waiting.Id,"delayed","Kiểm tra sao lưu đồng thời");
        var backup=service.BackupAsync(zip);await Task.WhenAll(change,backup);
        using(var archive=ZipFile.OpenRead(zip))
        {
            using var slipReader=new StreamReader(archive.GetEntry("slips.json")!.Open());
            using var flagReader=new StreamReader(archive.GetEntry("contract_flags.json")!.Open());
            var slips=JsonSerializer.Deserialize<List<Slip>>(slipReader.ReadToEnd(),WarehouseService.Json)!;
            var flags=JsonSerializer.Deserialize<List<ContractFlag>>(flagReader.ReadToEnd(),WarehouseService.Json)!;
            Check(slips.Single(s=>s.Id==waiting.Id).Status=="delayed"&&flags.Any(f=>f.SlipId==waiting.Id),"Backup serializes with state writes and keeps slip and generated flags consistent");
        }

        var staleRoot=SelfTests.CreateDemoWorkspace();
        File.WriteAllText(Path.Combine(staleRoot,"native_data","contract_flags.json"),JsonSerializer.Serialize(new[]{new ContractFlag{Contract="05492026",Project="Công trình A",Company="Công ty A",Status="delayed",SlipId="fixture-pending"}},WarehouseService.Json));
        using var stale=new WarehouseService(staleRoot);await stale.InitializeAsync();
        Check(!stale.Search(new SearchFilter{ContractsText="05492026"}).Rows.Any(x=>x.Flags.Any(f=>f.SlipId=="fixture-pending")),"Stale generated delay does not override its current waiting slip in reports");
        var oldReport=stale.Search(new SearchFilter{ContractsText="05492026"});
        File.Move(stale.Settings.MasterPath,stale.Settings.MasterPath+".removed");await stale.RefreshAsync(false);
        await Reject(()=>stale.SavePlanAsync(oldReport,"2026-11-07","internal_summary"),"Stale valid report cannot create a plan after the master source becomes unavailable");
        try{stale.CreateDraft(oldReport,"internal_summary","2026-11-07");throw new Exception("Stale source created a draft");}catch(InvalidOperationException){evidence.Add("Draft creation checks current source availability instead of trusting old report flags");}
        return evidence;
    }
}
