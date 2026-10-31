using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Hisa.App;

/// <summary>Native application state. Excel IO runs off the UI thread; readers use an indexed snapshot.</summary>
public sealed partial class WarehouseService : IDisposable
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly ExcelStore _excel = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _data;
    private readonly Dictionary<string,List<Package>> _byCode = [];
    private List<Slip> _slips = [];
    private List<PrintTemplate> _templates = [];
    private List<ContractFlag> _flags = [];
    private List<ShipmentRecord> _shipments = [];
    private List<string> _warnings = [];
    private WorkbookSnapshot _snapshot = new();
    private bool _initialized;
    private bool _dataAvailable;
    private string _sourceFingerprint = "";
    private string _masterFingerprint = "";
    private string _ktFingerprint = "";
    private string _ledgerFingerprint = "";
    private bool _exportsPending;
    private bool _sourceSyncPending;
    private bool _disposed;
    private bool _historyCacheValid;
    public string Workspace { get; }
    public string DataDirectory => _data;
    /// <summary>ZIP backups and automatic Excel snapshots live in the Documents library.</summary>
    public string BackupDirectory { get; }
    public AppSettings Settings { get; private set; }
    public WorkbookSnapshot Snapshot { get { lock(_stateLock) return _snapshot; } }
    public List<Slip> Slips { get { lock(_stateLock) return [.._slips]; } }
    public List<PrintTemplate> Templates { get { lock(_stateLock) return [.._templates]; } }
    public List<ContractFlag> Flags { get { lock(_stateLock) return [.._flags]; } }
    public List<ShipmentRecord> Shipments { get { lock(_stateLock) return [.._shipments]; } }
    public List<string> Warnings { get { lock(_stateLock) return [.._warnings]; } }
    public bool IsBusy { get; private set; }
    public string LastError { get; private set; } = "";
    public double LastLoadMs { get; private set; }
    public event EventHandler? Changed;
    public event Action<string>? Progress;

    public WarehouseService(string? workspace = null)
    {
        Workspace = workspace ?? FindWorkspace();
        var nativeRoot = FindNativeRoot();
        // Store real data under the signed-in user's Documents library, not Program Files.
        // An explicitly selected workspace is isolated (used by diagnostics/demo).
        _data = workspace != null
            ? Path.Combine(workspace, "native_data")
            : Path.Combine(UserDocumentsHome(), "APP KHO", "data");
        BackupDirectory = workspace != null
            ? Path.Combine(workspace, "Backups")
            : Path.Combine(UserDocumentsHome(), "Backups");
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(BackupDirectory);
        _excel.BackupDirectory=Path.Combine(BackupDirectory,"Excel");
        if(workspace==null)
        {
            MigrateNativeData(Path.Combine(Workspace,"APP KHO","data"),_data);
            MigrateNativeData(Path.Combine(nativeRoot,"APP KHO","data"),_data);
            MigrateNativeData(Path.Combine(nativeRoot,"data"),_data);
        }
        ImportLegacyOnce();
        Settings = Load<AppSettings>("settings.json") ?? DefaultSettings();
        NormalizeSettings(Settings);
        _slips = Load<List<Slip>>("slips.json") ?? [];
        _templates = Load<List<PrintTemplate>>("templates.json") ?? [];
        _flags = Load<List<ContractFlag>>("contract_flags.json") ?? [];
        NormalizeHistory();
        EnsureTemplates();
        Persist("settings.json", Settings);
    }
    private static void MigrateNativeData(string oldPath,string newPath)
    {
        if(!Directory.Exists(oldPath)||Path.GetFullPath(oldPath).Equals(Path.GetFullPath(newPath),StringComparison.OrdinalIgnoreCase))return;
        foreach(var file in Directory.EnumerateFiles(oldPath)){var target=Path.Combine(newPath,Path.GetFileName(file));if(!File.Exists(target))File.Copy(file,target);}
    }
    private string FindNativeRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (dir.Name is "HISA APP" or "HISA CSharp") return dir.FullName;
        return AppContext.BaseDirectory;
    }
    private static string UserDocumentsHome()
    {
        // Environment resolves OneDrive-redirected Documents / localized "Tài liệu" correctly.
        var documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if(string.IsNullOrWhiteSpace(documents))throw new InvalidOperationException("Không tìm thấy thư mục Tài liệu của Windows.");
        return Path.Combine(documents,"HISA Kho Vat Tu");
    }
    private static string FindWorkspace()
    {
        // Keep existing Excel source folders discoverable for portable/legacy installations.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName,"Tổng hợp.xlsx")) || Directory.Exists(Path.Combine(dir.FullName,"CONT ĐI"))) return dir.FullName;
        // Fresh Program Files installations must never write their state next to the EXE.
        return UserDocumentsHome();
    }
    private void ImportLegacyOnce()
    {
        if (File.Exists(Path.Combine(_data,"migration.json"))) return;
        var old = Path.Combine(Workspace,"Ứng dụng bàn giao vật tư","app_data");
        foreach (var name in new[]{"slips.json","templates.json","contract_flags.json"})
        {
            var source = Path.Combine(old,name); var target = Path.Combine(_data,name);
            if (File.Exists(source) && !File.Exists(target)) File.Copy(source,target);
        }
        Persist("migration.json",new { at=DateTime.Now, source=old, note="Nhập một lần; dữ liệu bản cũ được giữ nguyên." });
    }
    private AppSettings DefaultSettings()
    {
        var settings = new AppSettings { MasterPath = Path.Combine(Workspace,"Tổng hợp.xlsx"), KtPath=Path.Combine(Workspace,"KT tổng hợp.xlsx"),ProductExportPath=Path.Combine(Workspace,"Thành phẩm xuất kho.xlsx") };
        var cont = Path.Combine(Workspace,"CONT ĐI");
        if (Directory.Exists(cont)) settings.DataSources.Add(cont);
        // A standard Kho HISA root keeps the three canonical workbooks under
        // Kho chính, while CONT ĐI is the only active container scan root.
        var standard=HisaFolderLayout.Detect(Workspace,settings);
        if(standard!=null)HisaFolderLayout.Apply(settings,standard);
        var legacy = Path.Combine(Workspace,"Ứng dụng bàn giao vật tư","app_data","settings.json");
        try
        {
            if(File.Exists(legacy))
            {
                var node=JsonNode.Parse(File.ReadAllText(legacy));
                foreach(var value in node?["data_sources"]?.AsArray() ?? [])
                {
                    var path=value?.ToString()??"";
                    if(File.Exists(path) && Path.GetFileName(path).Equals("Tổng hợp.xlsx",StringComparison.OrdinalIgnoreCase)) settings.MasterPath=path;
                    else if((Directory.Exists(path)||File.Exists(path))&&!settings.DataSources.Contains(path)) settings.DataSources.Add(path);
                }
            }
        }
        catch(Exception e) { Log("migration",e); }
        return settings;
    }
    public AppSettings ResolveSettingsFromSourceFolder(string sourceFolder)
    {
        if(!Directory.Exists(sourceFolder))throw new DirectoryNotFoundException("Chọn một thư mục nguồn đang tồn tại.");
        var copy=Clone(Settings);var folder=Path.GetFullPath(sourceFolder);copy.DataSources=[folder];
        var standard=HisaFolderLayout.Detect(folder,copy);
        if(standard!=null)
        {
            HisaFolderLayout.Apply(copy,standard);
            return copy;
        }
        string? Find(string fileName)
        {
            var current=new DirectoryInfo(folder);
            for(var depth=0;current!=null&&depth<5;depth++,current=current.Parent)
            {
                var direct=Path.Combine(current.FullName,fileName);if(File.Exists(direct))return direct;
                try
                {
                    foreach(var child in current.EnumerateDirectories())
                    {
                        var nested=Path.Combine(child.FullName,fileName);if(File.Exists(nested))return nested;
                    }
                }
                catch(UnauthorizedAccessException){}
            }
            return null;
        }
        copy.MasterPath=Find("Tổng hợp.xlsx")??throw new FileNotFoundException("Không tìm thấy Tổng hợp.xlsx trong thư mục nguồn hoặc các thư mục cha gần đó.");
        var home=Path.GetDirectoryName(copy.MasterPath)??folder;
        copy.KtPath=Find("KT tổng hợp.xlsx")??Path.Combine(home,"KT tổng hợp.xlsx");
        copy.ProductExportPath=Find("Thành phẩm xuất kho.xlsx")??Path.Combine(home,"Thành phẩm xuất kho.xlsx");
        return copy;
    }
    private void NormalizeSettings(AppSettings settings)
    {
        settings.DataSources ??= [];
        settings.DataSources=settings.DataSources.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>
        {
            var path=x.Trim();
            if(Directory.Exists(path))return Path.GetFullPath(path);
            return File.Exists(path)?Path.GetDirectoryName(Path.GetFullPath(path))??path:path;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(settings.DataSources.Count>1)settings.DataSources=settings.DataSources.Take(1).ToList();
        if (string.IsNullOrWhiteSpace(settings.MasterPath)) settings.MasterPath=Path.Combine(Workspace,"Tổng hợp.xlsx");
        var baseDir=Path.GetDirectoryName(settings.MasterPath)??Workspace;
        if(string.IsNullOrWhiteSpace(settings.KtPath)) settings.KtPath=Path.Combine(baseDir,"KT tổng hợp.xlsx");
        if(string.IsNullOrWhiteSpace(settings.ProductExportPath)) settings.ProductExportPath=Path.Combine(baseDir,"Thành phẩm xuất kho.xlsx");
        settings.PollSeconds=Math.Clamp(settings.PollSeconds,5,3600);
    }
    private void NormalizeHistory()
    {
        foreach(var slip in _slips)
        {
            slip.Lines ??= []; slip.Contracts ??= []; slip.ContractOnly ??=[]; slip.ContractPackages ??= []; slip.StatusHistory ??= [];slip.MotorSerials??=[];
            if(string.IsNullOrEmpty(slip.Status)||slip.Status=="draft")slip.Status="planned";
            foreach(var p in slip.ContractPackages) p.Items ??= [];
            if(slip.Contracts.Count==0) slip.Contracts=slip.Lines.Select(x=>x.Contract).Concat(slip.ContractPackages.Select(x=>x.Contract)).Where(x=>x.Length>0).DistinctBy(Rules.Contract).ToList();
            foreach(var l in slip.Lines) if(l.Category.Length==0) l.Category=Rules.Classify(l.Material);
        }
    }
    private void EnsureTemplates()
    {
        if(!_templates.Any(x=>x.Id=="internal_summary")) _templates.Insert(0,new PrintTemplate());
        if(!_templates.Any(x=>x.Id=="contract_delivery")) _templates.Add(new PrintTemplate {Id="contract_delivery",Name="Bàn giao vật tư · theo hợp đồng",Layout="contract_delivery",Title="BIÊN BẢN BÀN GIAO VẬT TƯ",MinRows=10});
        if(!_templates.Any(x=>x.Id=="default")) _templates.Add(new PrintTemplate {Id="default",Name="Phiếu bàn giao vật tư",Layout="standard",Title="PHIẾU BÀN GIAO VẬT TƯ"});
    }
    private T? Load<T>(string name)
    {
        var path=Path.Combine(_data,name);
        try { return File.Exists(path)?JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json):default; }
        catch(Exception e) { Log("read-"+name,e); throw new InvalidDataException($"Không đọc được {name}. Dữ liệu gốc được giữ nguyên. Chi tiết: {e.Message}",e); }
    }
    private void Persist<T>(string name,T value)
    {
        var path=Path.Combine(_data,name); var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,JsonSerializer.Serialize(value,Json));
            if(File.Exists(path))File.Copy(path,path+".bak",true);
            File.Move(temp,path,true);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    public static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,Json),Json)!;
    public void Log(string operation,Exception e)
    {
        try { File.AppendAllText(Path.Combine(_data,"application.log"),$"{DateTime.Now:O} [{operation}] {e}\n"); } catch { }
    }
    private void Notify() => Changed?.Invoke(this,EventArgs.Empty);
    private void Say(string text) => Progress?.Invoke(text);

    public async Task InitializeAsync()
    {
        if(_initialized)return; _initialized=true;
        await Task.Run(()=>
        {
            try
            {
                var cache=Load<CacheEnvelope>("cache.json");
                if(cache!=null && string.Equals(cache.MasterPath,Settings.MasterPath,StringComparison.OrdinalIgnoreCase))
                {
                    SetSnapshot(cache.Snapshot);
                    if(cache.HistoryStamp.Length>0&&cache.HistoryStamp==HistoryFingerprint())
                    {
                        lock(_stateLock)_shipments=cache.Shipments;
                        _ledgerFingerprint=cache.HistoryStamp;_historyCacheValid=true;
                    }
                    Say("Đã mở dữ liệu lưu nhanh; đang kiểm tra nguồn Excel…");
                    lock(_stateLock)_warnings=["Đang dùng dữ liệu lưu nhanh; chờ xác nhận cập nhật từ Tổng hợp.xlsx."];
                }
            }
            catch(Exception e){ Log("cache",e); }
        });
        Notify();
        await RefreshCoreAsync(Settings.AutoSyncCont&&Settings.DataSources.Count>0,Settings.AutoSyncKt);
        _=Task.Run(MonitorAsync);
    }
    private void SetSnapshot(WorkbookSnapshot snapshot)
    {
        snapshot.Packages=OnlyHungYen(snapshot.Packages);
        snapshot.ContPackages=OnlyHungYen(snapshot.ContPackages);
        snapshot.DetailPackages=OnlyHungYen(snapshot.DetailPackages);
        lock(_stateLock)
        {
            _snapshot=snapshot;
            _byCode.Clear();
            foreach(var group in snapshot.Packages.GroupBy(x=>Rules.Contract(x.Contract)))_byCode[group.Key]=group.ToList();
        }
    }
    internal void UseSnapshotForDiagnostics(WorkbookSnapshot snapshot){SetSnapshot(snapshot);_dataAvailable=true;}
    private static List<Package> OnlyHungYen(IEnumerable<Package> packages)=>packages.Where(p=>!string.IsNullOrWhiteSpace(p.Contract)&&p.Items.Any(x=>x.IsHungYen)).ToList();
    private static string FileStamp(string path)
    {
        try { var f=new FileInfo(path);return f.Exists?$"{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}":""; } catch{return "";}
    }
    private string SourceFingerprint()
    {
        var stamps=new List<string>();
        foreach(var source in Settings.DataSources)
        {
            try
            {
                var files=Directory.Exists(source)?Directory.EnumerateFiles(source,"*.xlsx",SearchOption.AllDirectories):File.Exists(source)?[source]:Array.Empty<string>();
                stamps.AddRange(files.Where(p=>!Path.GetFileName(p).StartsWith("~$")&&!ContFileOrganizer.IsHangGuiFolder(p)&&!EquipmentListReader.IsConfiguredEquipmentFile(p,Settings)&&!p.Contains(Path.DirectorySeparatorChar+".hisa-backups"+Path.DirectorySeparatorChar)&&!p.Equals(Settings.MasterPath,StringComparison.OrdinalIgnoreCase)&&!p.Equals(Settings.KtPath,StringComparison.OrdinalIgnoreCase)&&!p.Equals(Settings.ProductExportPath,StringComparison.OrdinalIgnoreCase)&&!p.Contains(Path.DirectorySeparatorChar+"XUẤT HÀNG"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).Select(FileStamp));
                // Sibling received workbooks must participate in source fingerprints;
                // otherwise sync can accidentally drop historical source packages.
                if(Directory.Exists(source))
                {
                    var received=ContFileOrganizer.SiblingFolder(source,"Đã nhập kho");
                    if(Directory.Exists(received))
                        stamps.AddRange(Directory.EnumerateFiles(received,"*.xlsx",SearchOption.AllDirectories)
                            .Where(p=>!Path.GetFileName(p).StartsWith("~$")).Select(FileStamp));
                }
                var parent=Directory.Exists(source)?Directory.GetParent(source)?.FullName:Path.GetDirectoryName(source);
                if(parent!=null)stamps.AddRange(Directory.EnumerateFiles(parent,"Lịch xuất hàng*.xlsx").Concat(Directory.EnumerateFiles(parent,"HÀNG THEO CONT.xlsx")).Select(FileStamp));
            }
            catch(Exception e){Log("fingerprint",e);stamps.Add("ERROR|"+source);}
        }
        return string.Join("\n",stamps.Order(StringComparer.Ordinal));
    }
    private string HistoryFingerprint()
    {
        var files=new List<string>{Settings.ProductExportPath};
        var folder=Path.Combine(Path.GetDirectoryName(Settings.MasterPath)??Workspace,"XUẤT HÀNG");
        if(Directory.Exists(folder))files.AddRange(Directory.EnumerateFiles(folder,"*.xlsx").Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(p),@"^\d{2}-\d{2}-\d{4}$")));
        return string.Join("\n",files.Select(FileStamp).Order(StringComparer.Ordinal));
    }
    private void PersistCache()=>Persist("cache.json",new CacheEnvelope { MasterPath=Settings.MasterPath,Snapshot=Snapshot,Shipments=Shipments,HistoryStamp=_ledgerFingerprint });
    private async Task MonitorAsync()
    {
        while(!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Settings.PollSeconds),_stop.Token);
                if(!Settings.AutoRefresh || IsBusy)continue;
                var source=SourceFingerprint();var master=FileStamp(Settings.MasterPath);var kt=FileStamp(Settings.KtPath);var ledger=HistoryFingerprint();
                var sourceChanged=source!=_sourceFingerprint;
                var masterChanged=master!=_masterFingerprint;
                var ktChanged=kt!=_ktFingerprint;
                if(sourceChanged || masterChanged || ktChanged || ledger!=_ledgerFingerprint || _exportsPending || _sourceSyncPending)
                    await RefreshCoreAsync((sourceChanged||_sourceSyncPending) && Settings.AutoSyncCont,(sourceChanged||masterChanged||ktChanged) && Settings.AutoSyncKt);
            }
            catch(OperationCanceledException){break;}
            catch(Exception e){Log("monitor",e);LastError=e.Message;Notify();}
        }
    }
    public Task RefreshAsync(bool force=false)=>RefreshCoreAsync(force&&Settings.AutoSyncCont,force&&Settings.AutoSyncKt);
    private async Task RefreshCoreAsync(bool scanCont,bool syncKt)
    {
        await _gate.WaitAsync(); IsBusy=true;LastError="";Notify();
        var watch=Stopwatch.StartNew();
        try
        {
            await Task.Run(()=>
            {
                var warnings=new List<string>();
                SourceScanResult? sourceScan=null;
                var sourceFilesStable=false;
                var importSaved=false;
                string sourceStamp="";
                if(scanCont && Settings.DataSources.Count>0)
                {
                    Say("Quét file cont thay đổi và nhận diện kiện Hưng Yên…");
                    var progress=new Progress<OperationProgress>(p=>Say(p.Message));
                    var sourceBefore=SourceFingerprint();
                    sourceStamp=sourceBefore;
                    var scan=_excel.ScanSources(Settings,progress,_stop.Token);
                    sourceScan=scan;
                    warnings.AddRange(scan.Warnings);
                    var stable=sourceBefore==SourceFingerprint();
                    sourceFilesStable=stable && scan.Warnings.Count==0;
                    _sourceSyncPending=scan.Warnings.Count>0||!stable;
                    if(!stable)warnings.Add("Nguồn cont đang thay đổi; ứng dụng sẽ quét lại sau khi sao chép/cập nhật file hoàn tất.");
                    if(stable && scan.Warnings.Count==0 && scan.FileCount>0 && scan.Packages.Count>0 && File.Exists(Settings.MasterPath))
                    {
                        Say($"Cập nhật {scan.Packages.Count:N0} kiện từ {scan.FileCount:N0} tệp nguồn…");
                        _excel.WriteContSummary(Settings.MasterPath,scan.Packages);
                        importSaved=true;
                    }
                    else if(scan.Warnings.Count>0)warnings.Add("Nguồn có cảnh báo; giữ nguyên trang cont để tránh ghi thiếu dữ liệu.");
                    else if(scan.Packages.Count==0)warnings.Add("Không tìm thấy kiện Hưng Yên trong nguồn; giữ nguyên Tổng hợp.xlsx.");
                }
                if(syncKt && File.Exists(Settings.KtPath)&&File.Exists(Settings.MasterPath))
                {
                    Say("Đối chiếu ngày về và vật tư theo KT tổng hợp.xlsx…");
                    _excel.ApplyKt(Settings.MasterPath,Settings.KtPath);
                }
                Say("Đọc dữ liệu chính Tổng hợp.xlsx…");
                if(!File.Exists(Settings.MasterPath))throw new FileNotFoundException("Không tìm thấy dữ liệu chính Tổng hợp.xlsx. Chọn lại file trong Cấu hình nguồn.",Settings.MasterPath);
                var masterBefore=FileStamp(Settings.MasterPath);
                var loaded=_excel.ReadMaster(Settings.MasterPath);
                if(masterBefore!=FileStamp(Settings.MasterPath))throw new IOException("Tổng hợp.xlsx vừa thay đổi trong lúc đọc. Ứng dụng sẽ tự tải lại.");
                SetSnapshot(loaded);
                // Master Excel must first be written and read successfully. A file
                // merely recognized in CONT ĐI is NOT already physically received.
                // Only archive workbooks whose EVERY package has an actual receipt
                // OriginPackageId. Keep partially received workbooks in CONT ĐI.
                if(sourceScan!=null && sourceFilesStable && sourceStamp==SourceFingerprint())
                {
                    warnings.AddRange(ContFileOrganizer.RelocateLegacyFolders(
                        Settings.DataSources,sourceScan.Packages,loaded));
                    if(importSaved)
                    {
                        var confirmed=ContFileOrganizer.ConfirmedCandidates(
                            sourceScan.ImportedHungYenFiles,sourceScan.Packages,loaded);
                        warnings.AddRange(ContFileOrganizer.MoveFiles(confirmed,"Đã nhập kho"));
                    }
                    warnings.AddRange(ContFileOrganizer.MoveFiles(sourceScan.HangGuiFiles,"Hàng gửi"));
                }
                lock(_stateLock)_warnings=[..warnings,"Đang kiểm tra lịch sử xuất hàng và các cảnh báo trước khi lập phiếu."];
                Notify();
                warnings.AddRange(loaded.Warnings);
                if(_exportsPending || _slips.Any(x=>x.ProductExportPending))
                {
                    try { SyncExportsCore(); } catch(Exception e){warnings.Add(e.Message);Log("retry-export",e);}
                }
                try
                {
                    var historyStamp=HistoryFingerprint();
                    if(!_historyCacheValid||historyStamp!=_ledgerFingerprint)
                    {
                        Say("Kiểm tra lịch sử xuất hàng…");var shipments=_excel.ReadShipments(Settings);lock(_stateLock)_shipments=shipments;
                        _ledgerFingerprint=historyStamp;_historyCacheValid=true;
                    }
                }
                catch(Exception e){warnings.Add("Chưa đọc đủ lịch sử xuất: "+e.Message);Log("history",e);}
                _dataAvailable=true;
                lock(_stateLock)_warnings=warnings.Distinct().ToList();
                PersistCache();
                _sourceFingerprint=SourceFingerprint();_masterFingerprint=FileStamp(Settings.MasterPath);_ktFingerprint=FileStamp(Settings.KtPath);
            });
            LastLoadMs=watch.Elapsed.TotalMilliseconds;
            Say($"Sẵn sàng · {Snapshot.Packages.Count:N0} kiện · {Snapshot.Packages.Sum(x=>x.ItemCount):N0} dòng · {LastLoadMs/1000:N2} giây");
        }
        catch(Exception e)
        {
            _dataAvailable=false; LastError=e.Message;Log("refresh",e);
            lock(_stateLock)_warnings=[e.Message,"Giữ lại dữ liệu đã đọc gần nhất; chưa thể xác nhận các mã thiếu khi nguồn chưa sẵn sàng."];
            Say("Chưa cập nhật được nguồn · "+e.Message);
        }
        finally {IsBusy=false;_gate.Release();Notify();}
    }
    public ContractReport Search(SearchFilter filter)
    {
        var tokens=Rules.Tokens(filter.ContractsText);var wanted=tokens.Select(Rules.Contract).ToHashSet();
        List<Package> packages;List<Package> detail;List<Slip> slips;List<ContractFlag> flags;List<ShipmentRecord> shipments;List<string>warnings;
        lock(_stateLock)
        {
            packages=wanted.Count>0?wanted.Where(_byCode.ContainsKey).SelectMany(x=>_byCode[x]).ToList():[.._snapshot.Packages];
            detail=[.._snapshot.DetailPackages];slips=[.._slips];flags=[.._flags];shipments=[.._shipments];warnings=[.._warnings];
        }
        var result=new ContractReport { Filter=Clone(filter),RequestedCount=tokens.Count,DataAvailable=_dataAvailable,Warnings=warnings };
        if(!_dataAvailable)result.Warnings.Add("Nguồn chưa được xác nhận. Các mã chưa tìm thấy cần được tra cứu lại sau khi cập nhật thành công.");
        var eligible=packages.Select(p=>Rules.Contract(p.Contract)).ToHashSet();
        foreach(var code in tokens.Where(x=>!eligible.Contains(Rules.Contract(x))))
        {
            var row=new ContractRow {Contract=code,Warning=_dataAvailable?"Chưa có dữ liệu kiện Hưng Yên trong Tổng hợp.xlsx.":"Chưa đối chiếu được: nguồn chưa sẵn sàng."};
            AttachHistory(row,slips,flags,shipments);result.Missing.Add(row);
        }
        var detailByIdentity=detail.GroupBy(x=>x.Identity).ToDictionary(g=>g.Key,g=>g.ToList());
        var scoped=new List<ContractRow>();var query=Rules.Code(filter.Query);
        foreach(var group in packages.GroupBy(x=>x.Identity))
        {
            var p=group.First();
            if(query.Length>0&&!Rules.Code(p.Contract+" "+p.Project+" "+p.Company).Contains(query))continue;
            var targets=filter.ProjectTargets.Where(x=>Rules.Contract(x.Contract)==Rules.Contract(p.Contract)).ToList();
            if(targets.Count>0&&!targets.Any(x=>x.Identity==p.Identity))continue;
            var selected=group.ToList();
            if(filter.ArrivalDate.Length>0)
            {
                selected=(detailByIdentity.GetValueOrDefault(p.Identity)??selected).Where(x=>x.ArrivalDate==filter.ArrivalDate || (x.ArrivalDate.Length==0&&x.SourceSheet=="chi tiết"&&x.SourceDate==filter.ArrivalDate)).ToList();
                if(selected.Count==0){result.DateMismatch.Add(new ContractRow{Contract=p.Contract,Project=p.Project,Company=p.Company,Warning="Có dữ liệu nhưng không có kiện về trong ngày đã chọn."});continue;}
            }
            var row=new ContractRow{Contract=p.Contract,Project=p.Project,Company=p.Company,Packages=selected};
            row.AllMaterials=selected.SelectMany(pkg=>pkg.Items.Where(i=>i.IsHungYen).Select(i=>Annotate(i,pkg)))
                .DistinctBy(MaterialDuplicateKey).ToList();
            AttachHistory(row,slips,flags,shipments);
            if(filter.ShipDate.Length>0 && !row.Shipments.Any(x=>x.Date==filter.ShipDate)&&!row.Slips.Any(x=>x.Status=="delivered"&&x.HandoverDate==filter.ShipDate))
            {row.Warning="Chưa ghi nhận xuất trong ngày đã chọn.";result.DateMismatch.Add(row);continue;}
            row.Materials=row.AllMaterials.Where(x=>Rules.MaterialMatches(x,filter.Categories,Rules.Tokens(filter.KeywordsText))).ToList();
            // A filtered-out project is visible for reference, but must never be
            // selected for export or a delivery slip unless explicitly checked.
            if(row.Materials.Count==0&&(filter.Categories.Count>0||Rules.Tokens(filter.KeywordsText).Count>0))row.Selected=false;
            scoped.Add(row);
            if(row.Materials.Count>0)result.Rows.Add(row);else result.NoMaterialMatch.Add(row);
        }
        result.Categories=CategoryCounts(scoped);
        result.ContractCount=scoped.Select(x=>Rules.Contract(x.Contract)).Distinct().Count();result.ProjectCount=scoped.Count;
        result.PackageCount=scoped.Sum(x=>x.PackageCount);result.LineCount=scoped.Sum(x=>x.Materials.Count);
        var statusScope=scoped.Concat(result.Missing).ToList();
        foreach(var key in new[]{"withSlips","planned","delayed","delivered","cancelled","cancel_pickup"})
            result.StatusDetails[key]=statusScope.Where(x=>key=="withSlips"?x.Slips.Count>0:key=="delivered"?x.Slips.Any(s=>s.Status==key)||x.Shipments.Count>0:x.Slips.Any(s=>s.Status==key)||x.Flags.Any(f=>f.Status==key)).ToList();
        result.WithSlips=result.StatusDetails["withSlips"].Select(x=>Rules.Contract(x.Contract)).Distinct().Count();
        result.CancelledCount=result.StatusDetails["cancelled"].Select(x=>Rules.Contract(x.Contract)).Distinct().Count();
        return result;
    }
    private static MaterialLine Annotate(MaterialLine item,Package p)
    {
        var line=item.Copy();line.Contract=p.Contract;line.Project=p.Project;line.Company=p.Company;
        line.PackageId=p.Id;line.PackageNo=p.PackageNo;line.ArrivalDate=p.ArrivalDate;line.SourceDate=p.SourceDate;line.SourceFile=p.SourceFile;line.Category=Rules.Classify(line.Material);return line;
    }
    private static string MaterialDuplicateKey(MaterialLine line) => string.Join("|",[
        Rules.Identity(line.Contract,line.Project,line.Company),Rules.Code(line.Material),Rules.Code(line.Unit),
        line.Quantity?.ToString("0.################",CultureInfo.InvariantCulture)??"",line.SourceDate,line.ArrivalDate,Rules.Code(line.Note)
    ]);
    private static bool SlipMatches(Slip slip,ContractRow row)
    {
        var identities=SlipHistoryIdentities(slip);
        if(identities.Count>0)return identities.Any(p=>Rules.SameProject(row.Contract,row.Project,row.Company,p.Contract,p.Project,p.Company));
        if(slip.Status=="delivered"&&slip.Lines.Count>0)return false;
        return slip.Contracts.Any(c=>Rules.Contract(c)==Rules.Contract(row.Contract));
    }
    private static List<(string Contract,string Project,string Company)> SlipHistoryIdentities(Slip slip)
    {
        // Full source snapshots are retained for audit; they do not prove a contract
        // was exported when every one of its material lines was unchecked.
        var detailed=slip.Status=="delivered"&&slip.Lines.Count>0;
        var records=(detailed?slip.Lines.Where(x=>x.Include).Select(p=>(p.Contract,p.Project,p.Company)):
            slip.ContractPackages.Select(p=>(p.Contract,p.Project,p.Company)).Concat(slip.Lines.Select(p=>(p.Contract,p.Project,p.Company))))
            .Concat((slip.ContractOnly??[]).Select(p=>(p.Contract,p.Project,p.Company)));
        return records.Where(p=>!string.IsNullOrWhiteSpace(p.Contract)).DistinctBy(p=>Rules.Identity(p.Contract,p.Project,p.Company)).ToList();
    }
    private static IEnumerable<string> SlipHistoryContracts(Slip slip)
    {
        var records=SlipHistoryIdentities(slip);
        return records.Count>0?records.Select(x=>x.Contract):slip.Status=="delivered"&&slip.Lines.Count>0?[]:slip.Contracts;
    }
    private static void AttachHistory(ContractRow row,List<Slip> slips,List<ContractFlag> flags,List<ShipmentRecord> shipments)
    {
        row.Slips=slips.Where(s=>SlipMatches(s,row)).OrderByDescending(x=>x.HandoverDate).ToList();
        row.Flags=flags.Where(f=>Rules.SameProject(row.Contract,row.Project,row.Company,f.Contract,f.Project,f.Company)
            &&(string.IsNullOrEmpty(f.SlipId)||!slips.Any(s=>s.Id==f.SlipId&&s.Status!="delayed"))).ToList();
        var knownIds=slips.Select(x=>x.Id).ToHashSet();var knownCodes=slips.Select(x=>x.SlipCode).ToHashSet();
        row.Shipments=shipments.Where(s=>Rules.SameProject(row.Contract,row.Project,row.Company,s.Contract,s.Project,s.Company)&&!knownIds.Contains(s.SlipId)&&!(s.SlipCode.Length>0&&knownCodes.Contains(s.SlipCode))).ToList();
        row.Shipments.AddRange(row.Slips.Where(x=>x.Status=="delivered").Select(s=>new ShipmentRecord{Contract=row.Contract,Project=row.Project,Company=row.Company,Date=s.HandoverDate,SlipId=s.Id,SlipCode=s.SlipCode,Source="Phiếu bàn giao"}));
    }
    public static List<CategoryStat> CategoryCounts(IEnumerable<ContractRow> rows)
    {
        var list=rows.ToList();var output=new List<CategoryStat>();
        foreach(var category in Rules.Categories.Take(6))
        {
            var selected=list.Where(x=>x.AllMaterials.Any(i=>Rules.Classify(i.Material)==category)).ToList();
            var lines=selected.SelectMany(x=>x.AllMaterials).Where(x=>Rules.Classify(x.Material)==category).ToList();
            output.Add(new CategoryStat{Category=category,ContractCount=selected.Select(x=>Rules.Contract(x.Contract)).Distinct().Count(),ProjectCount=selected.Count,LineCount=lines.Count,Contracts=selected,Units=lines.GroupBy(x=>x.Unit).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Quantity??0))});
        }
        return output;
    }
    public DailyReport BuildDaily(string day)
    {
        if(!DateTime.TryParse(day,out var selected))throw new ArgumentException("Ngày báo cáo không hợp lệ.");
        day=selected.ToString("yyyy-MM-dd");var slips=Slips.Where(x=>x.HandoverDate==day).ToList();
        var deliveries=Shipments.Where(x=>x.Date==day&&!Slips.Any(s=>s.Id==x.SlipId||x.SlipCode.Length>0&&s.SlipCode==x.SlipCode)).ToList();
        var codes=slips.SelectMany(SlipHistoryContracts).Concat(deliveries.Select(x=>x.Contract)).Where(x=>x.Length>0).DistinctBy(Rules.Contract).ToList();
        var report=new DailyReport{Date=day,Slips=slips,OutstandingDelayed=Slips.Where(x=>x.Status=="delayed"&&string.CompareOrdinal(x.HandoverDate,day)<=0).ToList(),LegacyDeliveries=deliveries};
        var targets=slips.SelectMany(SlipHistoryIdentities).Select(p=>new ContractRow{Contract=p.Contract,Project=p.Project,Company=p.Company})
            .Concat(deliveries.Select(x=>new ContractRow{Contract=x.Contract,Project=x.Project,Company=x.Company})).Where(x=>x.Contract.Length>0).DistinctBy(x=>x.Identity).ToList();
        // Unidentified legacy records scope by code; known project identities must stay exact.
        var unspecific=targets.Where(x=>x.Project.Length==0).Select(x=>Rules.Contract(x.Contract)).ToHashSet();
        targets=targets.Where(x=>!unspecific.Contains(Rules.Contract(x.Contract))).ToList();
        report.Contracts=codes.Count>0?Search(new SearchFilter{ContractsText=string.Join("\n",codes),ProjectTargets=targets}):new ContractReport{Categories=CategoryCounts([]),Warnings=Warnings};
        var rows=slips.SelectMany(s=>s.Lines.Where(x=>x.Include)).GroupBy(x=>Rules.Identity(x.Contract,x.Project,x.Company)).Select(g=>new ContractRow{Contract=g.First().Contract,Project=g.First().Project,Company=g.First().Company,AllMaterials=g.ToList(),Materials=g.ToList()}).ToList();
        report.Categories=CategoryCounts(rows);
        report.StatusCounts=new[]{"planned","delayed","delivered","cancelled"}.ToDictionary(x=>x,x=>slips.Count(s=>s.Status==x));return report;
    }
    public List<Package> SelectContPackages(string contractText)
    {
        var codes=Rules.Tokens(contractText).Select(Rules.Contract).ToHashSet();
        return Snapshot.ContPackages.Where(p=>codes.Contains(Rules.Contract(p.Contract))).ToList();
    }
    public List<Package> GetArrivalPackages(string contractText="",string arrivalDate="")
    {
        var codes=Rules.Tokens(contractText).Select(Rules.Contract).ToHashSet();
        return Snapshot.DetailPackages
            .Where(p=>arrivalDate.Length==0||p.ArrivalDate==arrivalDate)
            .Where(p=>codes.Count==0||codes.Contains(Rules.Contract(p.Contract)))
            .OrderByDescending(p=>p.ArrivalDate).ThenBy(p=>p.Contract,StringComparer.Ordinal).ThenBy(p=>p.Project,StringComparer.Ordinal).ThenBy(p=>p.PackageNo)
            .ToList();
    }
    public async Task<int> DeleteArrivalPackageAsync(Package package)
    {
        if(!File.Exists(Settings.MasterPath))throw new FileNotFoundException("Không tìm thấy Tổng hợp.xlsx.",Settings.MasterPath);
        var removed=await MutateAsync(()=>_excel.DeleteArrivalPackage(Settings.MasterPath,package));
        await RefreshAsync(false);
        return removed;
    }
    public async Task<int> DeleteContPackageAsync(Package package)
    {
        var removed=await MutateAsync(()=>_excel.DeleteContPackage(Settings.MasterPath,package));
        await RefreshAsync(false);return removed;
    }
    public async Task<List<ComparisonRow>> PreviewArrivalAsync(string contractText)
    {
        var packages=SelectContPackages(contractText);
        return await Task.Run(()=>_excel.CompareWithKt(Settings.KtPath,packages));
    }
    public async Task<RecordArrivalResult> RecordArrivalAsync(string contractText,DateTime day,bool useKt=true)
    {
        var selected=SelectContPackages(contractText);
        if(selected.Count==0)throw new InvalidOperationException("Không có mã khớp trong trang Cont đi Hưng Yên. Chưa ghi dữ liệu.");
        var missing=Rules.Tokens(contractText).Where(c=>!selected.Any(p=>Rules.Contract(p.Contract)==Rules.Contract(c))).ToList();
        var result=await MutateAsync(()=>_excel.RecordArrival(Settings.MasterPath,Settings.KtPath,selected,day,useKt));
        if(missing.Count>0)result.Warnings.Add("Chưa có trong cont nguồn: "+string.Join(", ",missing));
        await RefreshCoreAsync(Settings.DataSources.Count>0,false);
        if(LastError.Length>0)result.Warnings.Add("Đã ghi kho nhưng chưa phân loại được file nguồn: "+LastError);
        return result;
    }
    /// <summary>
    /// Import ONLY verified source package IDs explicitly selected from the approved
    /// incoming container schedule; never include another project with the same contract.
    /// </summary>
    public async Task<RecordArrivalResult> RecordVerifiedArrivalAsync(
        IReadOnlyList<Package> verified, DateTime actualArrivalDay, bool useKt = true)
    {
        if (verified.Count==0) throw new InvalidOperationException("Chưa chọn kiện Hưng Yên đã về.");
        if (verified.Any(x=>x.Id.Length==0 || x.ContainerNumber.Length==0 ||
            x.OriginPackageId!=x.Id))
            throw new InvalidOperationException("Kiện chưa có đầy đủ ID nguồn và container.");
        if (verified.DistinctBy(x=>x.Id).Count()!=verified.Count)
            throw new InvalidOperationException("Trùng ID kiện nguồn, chưa ghi kho.");
        // Keep every package separate; selecting one does not select the whole contract.
        await RefreshAsync(false);
        var current=Snapshot;
        foreach (var incoming in verified)
        {
            var source=current.ContPackages.SingleOrDefault(x=>x.Id==incoming.Id);
            if(source==null || source.Identity!=incoming.Identity ||
               source.SourceDate!=incoming.SourceDate ||
               PlannedArrivalWorkflow.MaterialFingerprint(source)!=
               PlannedArrivalWorkflow.MaterialFingerprint(incoming))
                throw new InvalidOperationException("Kiện "+incoming.Contract+
                    " đã thay đổi nguồn hoặc số lượng; cần đối chiếu lại trước khi nhập.");
            if(current.DetailPackages.Any(x=>x.OriginPackageId==incoming.Id))
                throw new InvalidOperationException("Kiện "+incoming.Contract+
                    " đã có trong Tổng hợp.xlsx. Không ghi trùng.");
        }
        // The underlying Excel operation re-reads the workbook and checks source IDs
        // under its lock before writing. Never replace rows by contract identity.
        var result=await MutateAsync(()=>
            _excel.RecordSelectedArrival(Settings.MasterPath,verified,actualArrivalDay));
        // A second source scan archives only the fully received originals. A
        // partial container remains pending; a failed classification never
        // reverses the already committed Excel receipt.
        await RefreshCoreAsync(Settings.DataSources.Count>0,false);
        if(LastError.Length>0)result.Warnings.Add("Đã ghi kho nhưng chưa phân loại được file nguồn: "+LastError);
        return result;
    }

    public async Task<int> SyncKtAsync()
    {
        var count=await MutateAsync(()=>_excel.ApplyKt(Settings.MasterPath,Settings.KtPath));
        await RefreshAsync(false);return count;
    }
    private async Task<T> MutateAsync<T>(Func<T> action)
    {
        await _gate.WaitAsync();IsBusy=true;LastError="";Notify();
        try{return await Task.Run(action);}
        catch(Exception e){LastError=e.Message;Log("write",e);throw;}
        finally{IsBusy=false;_gate.Release();Notify();}
    }
    public Slip CreateDraft(ContractReport report,string templateId,string day)
    {
        if(!report.DataAvailable||!_dataAvailable)throw new InvalidOperationException("Dữ liệu chính chưa sẵn sàng. Cập nhật Tổng hợp.xlsx trước khi lập phiếu.");
        var selected=report.Rows.Concat(report.NoMaterialMatch).Where(x=>x.Selected).ToList();
        var noMaterial=selected.Where(x=>x.Materials.Count==0).Select(x=>new ContractRow{Contract=x.Contract,Project=x.Project,Company=x.Company}).ToList();
        var slip=new Slip{Name="Bàn giao vật tư "+Rules.Day(day),HandoverDate=day,TemplateId=templateId,Contracts=selected.Select(x=>x.Contract).DistinctBy(Rules.Contract).ToList(),ContractOnly=noMaterial,Lines=Clone(selected.SelectMany(x=>x.Materials).ToList()),ContractPackages=Clone(selected.SelectMany(x=>x.Packages).DistinctBy(p=>p.Id).ToList())};
        if(report.Missing.Count>0)slip.Note="Chưa có dữ liệu: "+string.Join(", ",report.Missing.Select(x=>x.Contract));
        return slip;
    }
    private string ReserveCode(string templateId,string day,List<Slip> existing)
    {
        var prefix=_templates.FirstOrDefault(x=>x.Id==templateId)?.CodePrefix??"BBBG-HY";
        if(string.IsNullOrWhiteSpace(prefix))prefix="BBBG-HY";
        var date=DateTime.TryParse(day,out var parsed)?parsed:DateTime.Today;
        var head=prefix+"-"+date.ToString("yyyyMMdd")+"-";var number=1;
        var used=existing.Select(x=>x.SlipCode).ToHashSet();
        while(used.Contains(head+number.ToString("000")))number++;
        return head+number.ToString("000");
    }
    private static void ValidateSlip(Slip slip)
    {
        if(!DateTime.TryParse(slip.HandoverDate,out var day))throw new ArgumentException("Ngày xuất không hợp lệ.");
        slip.HandoverDate=day.ToString("yyyy-MM-dd");
        if(!new[]{"planned","delayed","delivered","cancelled"}.Contains(slip.Status))throw new ArgumentException("Trạng thái phiếu không hợp lệ.");
        slip.Contracts=slip.Contracts.Concat(slip.Lines.Select(x=>x.Contract)).Concat(slip.ContractPackages.Select(x=>x.Contract)).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(Rules.CleanContract).DistinctBy(Rules.Contract).ToList();
        if(slip.Contracts.Count==0)throw new ArgumentException("Phiếu phải có ít nhất một số hợp đồng.");
        var lines=slip.Lines.Where(x=>x.Include).ToList();
        foreach(var line in lines)
        {
            var priorIdentity=Rules.Identity(line.Contract,line.Project,line.Company);
            if(string.IsNullOrWhiteSpace(line.Contract))
            {
                if(slip.Contracts.Count!=1)throw new ArgumentException("Chọn số hợp đồng cho từng dòng vật tư; phiếu có nhiều hợp đồng nên không thể tự đoán mã còn trống.");
                line.Contract=slip.Contracts[0];
            }
            var matches=slip.ContractPackages.Where(p=>Rules.Contract(p.Contract)==Rules.Contract(line.Contract)
                &&(string.IsNullOrWhiteSpace(line.Project)||Rules.Code(p.Project)==Rules.Code(line.Project))
                &&(string.IsNullOrWhiteSpace(line.Company)||Rules.Code(p.Company)==Rules.Code(line.Company))).DistinctBy(p=>p.Identity).ToList();
            if(matches.Count==1){if(string.IsNullOrWhiteSpace(line.Project))line.Project=matches[0].Project;if(string.IsNullOrWhiteSpace(line.Company))line.Company=matches[0].Company;}
            else if(matches.Count>1&&(string.IsNullOrWhiteSpace(line.Project)||string.IsNullOrWhiteSpace(line.Company)))throw new ArgumentException($"HĐ {line.Contract} có nhiều công trình/công ty. Chọn đúng công trình và công ty cho từng dòng vật tư.");
            if(priorIdentity!=Rules.Identity(line.Contract,line.Project,line.Company))
                foreach(var serial in (slip.MotorSerials??[]).OfType<MotorSerialEntry>().Where(x=>x.LineId==line.Id&&Rules.Identity(x.Contract,x.Project,x.Company)==priorIdentity&&Rules.Code(x.Material)==Rules.Code(line.Material)))
                {serial.Contract=line.Contract;serial.Project=line.Project;serial.Company=line.Company;}
        }
        if(slip.Status=="delivered"&&lines.Count==0&&slip.ContractOnly.Count==0)throw new ArgumentException("Phiếu chưa có vật tư; chọn hợp đồng không có vật tư nếu cần ghi nhận xuất theo hợp đồng.");
        if(lines.Any(x=>string.IsNullOrWhiteSpace(x.Material)||string.IsNullOrWhiteSpace(x.Unit)||x.Quantity is null || x.Quantity<0))throw new ArgumentException("Kiểm tra tên vật tư, đơn vị và số lượng. Số lượng phải là số không âm.");
        foreach(var line in lines)line.Category=Rules.Classify(line.Material);
    }
    private void SyncExportsCore()
    {
        _exportsPending=true;
        _excel.SyncExportLedgers(Settings,_slips);
        // Delivery is committed in slips.json first. The companion MÁY KÉO
        // workbook is updated only for exact machine serials in delivered
        // slips, never while planning. A locked file keeps the export pending
        // and is retried safely by the existing background refresh mechanism.
        var motorDates=MotorHandoverExcel.Sync(Settings,_slips,_data,
            Path.Combine(BackupDirectory,"Excel"));
        foreach(var warning in motorDates.Warnings)
        {
            lock(_stateLock)_warnings.Add("Ngày bàn giao máy kéo: "+warning);
            Log("motor-handover",new InvalidOperationException(warning));
        }
        foreach(var s in _slips)s.ProductExportPending=false;
        Persist("slips.json",_slips);_exportsPending=false;
        _shipments=_excel.ReadShipments(Settings);
        _ledgerFingerprint=HistoryFingerprint();_historyCacheValid=true;
        PersistCache();
    }
    private void PrepareSlipTransition(Slip item,Slip? prior,List<Slip> existing)
    {
        item.Lines ??= [];item.Contracts ??= [];item.ContractPackages ??= [];
        if(prior!=null)
        {
            if(!string.IsNullOrWhiteSpace(prior.SlipCode))
            {
                if(!string.IsNullOrWhiteSpace(item.SlipCode)&&item.SlipCode!=prior.SlipCode)throw new ArgumentException("Mã phiếu đã lưu không được thay đổi. Hãy tạo phiếu mới nếu cần mã khác.");
                item.SlipCode=prior.SlipCode;
            }
            if(item.ContractPackages.Count==0&&prior.ContractPackages.Count>0)item.ContractPackages=Clone(prior.ContractPackages);
            item.StatusHistory=Clone(prior.StatusHistory??[]);
            item.CreatedAt=prior.CreatedAt;
            item.DeliveredAt=prior.DeliveredAt;item.DelayedAt=prior.DelayedAt;item.CancelledAt=prior.CancelledAt;
            item.PreviousStatus=prior.PreviousStatus;
        }
        else item.StatusHistory=[];
        var changed=prior==null||prior.Status!=item.Status;
        if(changed&&item.Status=="delayed"&&string.IsNullOrWhiteSpace(item.DelayReason))throw new ArgumentException("Nhập lý do tạm hoãn phiếu.");
        if(changed&&item.Status=="cancelled"&&string.IsNullOrWhiteSpace(item.CancelReason))throw new ArgumentException("Nhập lý do hủy phiếu.");
        if(prior!=null&&item.Status==prior.Status)
        {
            if(string.IsNullOrWhiteSpace(item.DelayReason))item.DelayReason=prior.DelayReason;
            if(string.IsNullOrWhiteSpace(item.CancelReason))item.CancelReason=prior.CancelReason;
        }
        ValidateSlip(item);
        ValidateMotorSerialsForExport(item,existing);
        if(item.Status=="delivered"&&!_dataAvailable)throw new InvalidOperationException("Chưa xác nhận được dữ liệu chính; cập nhật Tổng hợp.xlsx trước khi ghi nhận xuất.");
        if(string.IsNullOrWhiteSpace(item.SlipCode))item.SlipCode=ReserveCode(item.TemplateId,item.HandoverDate,existing);
        if(existing.Any(x=>x.Id!=item.Id&&x.SlipCode==item.SlipCode))throw new ArgumentException("Mã phiếu đã tồn tại. Hãy dùng mã khác.");
        item.UpdatedAt=DateTime.Now.ToString("O");
        if(string.IsNullOrWhiteSpace(item.CreatedAt))item.CreatedAt=item.UpdatedAt;
        if(changed)
        {
            if(item.Status=="cancelled")item.PreviousStatus=prior?.Status??"planned";
            if(item.Status=="delivered")item.DeliveredAt=item.UpdatedAt;
            if(item.Status=="delayed")item.DelayedAt=item.UpdatedAt;
            if(item.Status=="cancelled")item.CancelledAt=item.UpdatedAt;
            item.StatusHistory.Add(new StatusEvent{At=item.UpdatedAt,Status=item.Status,Reason=item.Status=="delayed"?item.DelayReason:item.Status=="cancelled"?item.CancelReason:""});
        }
        item.ProductExportPending=true;
    }
    private List<ContractFlag> SlipDelayFlags(Slip item)
    {
        var previous=Flags.Where(x=>x.SlipId==item.Id).GroupBy(x=>Rules.Identity(x.Contract,x.Project,x.Company)).ToDictionary(x=>x.Key,x=>x.Last());
        var flags=Flags.Where(x=>x.SlipId!=item.Id).ToList();
        if(item.Status!="delayed")return flags;
        var records=item.ContractPackages.Select(x=>new ContractRow{Contract=x.Contract,Project=x.Project,Company=x.Company})
            .Concat(item.Lines.Select(x=>new ContractRow{Contract=x.Contract,Project=x.Project,Company=x.Company}))
            .Where(x=>!string.IsNullOrWhiteSpace(x.Contract)).DistinctBy(x=>x.Identity).ToList();
        var represented=records.Select(x=>Rules.Contract(x.Contract)).ToHashSet();
        records.AddRange(item.Contracts.Where(x=>!represented.Contains(Rules.Contract(x))).Select(x=>new ContractRow{Contract=x}));
        foreach(var record in records)
        {
            var old=previous.GetValueOrDefault(record.Identity);
            flags.Add(new ContractFlag{Id=old?.Id??Guid.NewGuid().ToString(),Contract=record.Contract,Project=record.Project,Company=record.Company,Status="delayed",Note=item.DelayReason,SlipId=item.Id,UpdatedAt=item.UpdatedAt});
        }
        return flags;
    }
    private Slip CommitSlip(Slip item,List<Slip> list)
    {
        var flags=SlipDelayFlags(item);
        list.RemoveAll(x=>x.Id==item.Id);list.Add(item);
        Persist("slips.json",list);lock(_stateLock)_slips=list;
        Persist("contract_flags.json",flags);lock(_stateLock)_flags=flags;
        try{SyncExportsCore();}
        catch(Exception e){_exportsPending=true;lock(_stateLock)_warnings.Add("Phiếu và trạng thái đã lưu; sổ Excel đang chờ cập nhật: "+e.Message);Log("export",e);}
        return item;
    }
    /// <summary>Update manual production status in column N of the existing motor file.</summary>
    public Task<bool> SetMotorProductionStatusAsync(MotorHungYenStore.Item machine,string status)=>
        MutateAsync(()=>MotorProductionExcel.SetStatus(Settings,machine,status,
            Path.Combine(BackupDirectory,"Excel")));

    public async Task<Slip> SaveSlipAsync(Slip slip)
    {
        var saved=await MutateAsync(()=>
        {
            var item=Clone(slip);
            if(string.IsNullOrWhiteSpace(item.Id))item.Id=Guid.NewGuid().ToString();
            var list=Slips;var prior=list.FirstOrDefault(x=>x.Id==item.Id);
            PrepareSlipTransition(item,prior,list);
            return CommitSlip(item,list);
        });
        return saved;
    }
    public async Task<List<Slip>> SavePlanAsync(ContractReport report,string day,string templateId)
    {
        if(!report.DataAvailable)throw new InvalidOperationException("Chưa xác nhận dữ liệu nguồn; cập nhật Tổng hợp.xlsx trước khi lập kế hoạch.");
        var output=await MutateAsync(()=>
        {
            if(!_dataAvailable)throw new InvalidOperationException("Nguồn dữ liệu chính vừa lỗi hoặc thay đổi. Hãy cập nhật lại trước khi lưu kế hoạch.");
            var current=Slips;var created=new List<Slip>();
            foreach(var row in report.Rows.Concat(report.NoMaterialMatch).Where(x=>x.Selected).Concat(report.Missing.Where(x=>x.Selected)))
            {
                var one=new ContractReport{Rows=row.Materials.Count>0?[row]:[],NoMaterialMatch=row.Materials.Count==0?[row]:[],Missing=row.Materials.Count==0?[]:[],DataAvailable=true};
                var item=CreateDraft(one,templateId,day);
                item.Contracts=[row.Contract];item.Name=$"Kế hoạch {row.Contract} · {row.Project}";item.Note=row.Warning;
                if(row.Materials.Count==0)item.Note="Chờ dữ liệu vật tư. "+item.Note;
                item.SlipCode=ReserveCode(templateId,day,current);item.CreatedAt=item.UpdatedAt=DateTime.Now.ToString("O");item.StatusHistory=[new StatusEvent{Status="planned"}];
                ValidateSlip(item);current.Add(item);created.Add(item);
            }
            if(created.Count==0)throw new ArgumentException("Chọn ít nhất một hợp đồng/công trình để lập kế hoạch.");
            Persist("slips.json",current);lock(_stateLock)_slips=current;return created;
        });return output;
    }
    public async Task ChangeSlipStatusAsync(string id,string status,string reason="")
    {
        if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Thiếu mã định danh phiếu cần đổi trạng thái.");
        await MutateAsync(()=>
        {
            var list=Slips;var prior=list.FirstOrDefault(x=>x.Id==id)??throw new ArgumentException("Không tìm thấy phiếu.");
            var item=Clone(prior);
            if(status=="restore")status=new[]{"planned","delayed","delivered"}.Contains(item.PreviousStatus)?item.PreviousStatus:"planned";
            item.Status=status;
            if(status=="delayed"&&!string.IsNullOrWhiteSpace(reason))item.DelayReason=reason;
            if(status=="cancelled"&&!string.IsNullOrWhiteSpace(reason))item.CancelReason=reason;
            PrepareSlipTransition(item,prior,list);CommitSlip(item,list);
            return true;
        });
    }
    public async Task<int> DeleteCancelledSlipsAsync(IEnumerable<string> ids)
    {
        var selected=ids.Where(x=>!string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        if(selected.Count==0)throw new ArgumentException("Chọn ít nhất một phiếu đã hủy.");
        return await MutateAsync(()=>
        {
            var list=Slips;var removable=list.Where(x=>selected.Contains(x.Id)&&x.Status=="cancelled").ToList();
            if(removable.Count==0)throw new InvalidOperationException("Không còn phiếu đã hủy được chọn.");
            list.RemoveAll(x=>removable.Any(r=>r.Id==x.Id));Persist("slips.json",list);lock(_stateLock)_slips=list;
            var flags=Flags.Where(f=>!removable.Any(s=>s.Id==f.SlipId)).ToList();Persist("contract_flags.json",flags);lock(_stateLock)_flags=flags;
            return removable.Count;
        });
    }
    public async Task SaveTemplateAsync(PrintTemplate template)
    {
        var copy=Clone(template);if(string.IsNullOrWhiteSpace(copy.Id))copy.Id=Guid.NewGuid().ToString("N");
        if(string.IsNullOrWhiteSpace(copy.Name)||string.IsNullOrWhiteSpace(copy.Title))throw new ArgumentException("Nhập tên mẫu và tiêu đề phiếu.");
        if(copy.FontSize<6||copy.FontSize>28||copy.TableFontSize<6||copy.TableFontSize>24||copy.TitleFontSize<10||copy.TitleFontSize>40)throw new ArgumentException("Cỡ chữ ngoài giới hạn in.");
        await ValidatePrintableTemplateAsync(copy);
        await MutateAsync(()=>
        {
            var list=Templates;list.RemoveAll(x=>x.Id==copy.Id);list.Add(copy);Persist("templates.json",list);lock(_stateLock)_templates=list;return true;
        });
    }
    private static Task ValidatePrintableTemplateAsync(PrintTemplate template)
    {
        if(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA){PrintService.ValidateTemplate(template);return Task.CompletedTask;}
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new Thread(()=>
        {
            try{PrintService.ValidateTemplate(template);completion.SetResult();}
            catch(Exception error){completion.SetException(error);}
        }){IsBackground=true,Name="HISA print template validation"};
        worker.SetApartmentState(ApartmentState.STA);worker.Start();return completion.Task;
    }
    public async Task SaveFlagsAsync(string contractText,string project,string status,string note)
    {
        if(!new[]{"delayed","cancel_pickup"}.Contains(status))throw new ArgumentException("Chọn hoãn hoặc hủy lấy hàng.");
        var tokens=Rules.Tokens(contractText);if(tokens.Count==0)throw new ArgumentException("Nhập số hợp đồng.");
        await MutateAsync(()=>
        {
            var list=Flags;
            foreach(var code in tokens)
            {
                list.RemoveAll(x=>Rules.Contract(x.Contract)==Rules.Contract(code)&&Rules.Code(x.Project)==Rules.Code(project)&&x.SlipId.Length==0);
                list.Add(new ContractFlag{Contract=code,Project=project,Status=status,Note=note});
            }
            Persist("contract_flags.json",list);lock(_stateLock)_flags=list;return true;
        });
    }
    public async Task RemoveFlagAsync(string id)
    {
        await MutateAsync(()=>{var list=Flags.Where(x=>x.Id!=id).ToList();Persist("contract_flags.json",list);lock(_stateLock)_flags=list;return true;});
    }
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var copy=Clone(settings);copy.DataSources??=[];
        if(copy.DataSources.Count>1)throw new InvalidOperationException("Chỉ cấu hình được một thư mục nguồn Excel.");
        foreach(var source in copy.DataSources)if(!Directory.Exists(source))throw new DirectoryNotFoundException("Nguồn chỉ nhận thư mục đang tồn tại, không nhận tệp Excel: "+source);
        NormalizeSettings(copy);
        if(!File.Exists(copy.MasterPath))throw new FileNotFoundException("Chọn đúng file dữ liệu chính Tổng hợp.xlsx.",copy.MasterPath);
        // Equipment production lists must remain independent of the master and
        // CONT ĐI source. Explicit paths only; no copying/moving user files.
        EquipmentListReader.ValidateIndependentSources(copy);
        copy.DataSources=copy.DataSources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await MutateAsync(()=>{Persist("settings.json",copy);Settings=copy;_sourceFingerprint=SourceFingerprint();return true;});await RefreshAsync(false);
    }
    public Task ExportReportAsync(string path,ContractReport report){EnsureReportDestination(path);return Task.Run(()=>_excel.ExportReport(path,report));}
    public Task ExportDailyAsync(string path,DailyReport report){EnsureReportDestination(path);return Task.Run(()=>_excel.ExportDaily(path,report));}
    public async Task BackupAsync(string path)
    {
        await MutateAsync(()=>
        {
            path=Path.GetFullPath(path);
            if(!Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Tệp sao lưu phải có đuôi .zip.");
            if(path.StartsWith(Path.GetFullPath(_data)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Chọn nơi sao lưu ngoài thư mục dữ liệu ứng dụng.");
            var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                // Serialize backup with app writes and publish only a complete ZIP.
                // An approved overwrite never destroys the prior good archive on error.
                BackupArchive.Create(temp,_data,Settings);
                // A checksum mismatch must NEVER replace an existing good ZIP.
                BackupArchive.Verify(temp);
                File.Move(temp,path,true);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
            return true;
        });
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_stop.Cancel();}
    public class CacheEnvelope
    {
        public string MasterPath { get; set; } = "";
        public WorkbookSnapshot Snapshot { get; set; } = new();
        public List<ShipmentRecord> Shipments { get; set; } = [];
        public string HistoryStamp { get; set; } = "";
    }
}
