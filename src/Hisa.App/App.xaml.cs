using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Hisa.App;

public partial class App : Application
{
    private Mutex? _instance;
    private WarehouseService? _service;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.CurrentCulture=CultureInfo.DefaultThreadCurrentCulture=new CultureInfo("vi-VN");
        CultureInfo.CurrentUICulture=CultureInfo.DefaultThreadCurrentUICulture=new CultureInfo("vi-VN");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("vi-VN")));
        DispatcherUnhandledException += (_,args)=>
        {
            _service?.Log("ui",args.Exception);
            MessageBox.Show(args.Exception.Message,SoftwareInfo.DisplayName+" · Không thể thực hiện",MessageBoxButton.OK,MessageBoxImage.Warning);
            args.Handled=true;
        };
        try
        {
            if(e.Args.Contains("--self-test"))
            {
                ShutdownMode=ShutdownMode.OnExplicitShutdown;
                var path=Argument(e.Args,"--output")??Path.Combine(Path.GetTempPath(),"hisa-csharp-selftest.json");
                var result=await SelfTests.RunAsync();
                File.WriteAllText(path,JsonSerializer.Serialize(result,new JsonSerializerOptions(WarehouseService.Json){WriteIndented=true}));
                Shutdown(result.Success?0:1);return;
            }
            if(e.Args.Contains("--benchmark"))
            {
                var master=Argument(e.Args,"--master")??throw new ArgumentException("Thiếu --master");
                var watch=System.Diagnostics.Stopwatch.StartNew();var store=new ExcelStore();var data=await Task.Run(()=>store.ReadMaster(master));watch.Stop();
                var diagnosticRoot=Path.Combine(Path.GetTempPath(),"hisa-native-benchmark-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(diagnosticRoot);
                using var diagnosticService=new WarehouseService(diagnosticRoot);diagnosticService.UseSnapshotForDiagnostics(data);
                var lookupWatch=System.Diagnostics.Stopwatch.StartNew();
                var ten=diagnosticService.Search(new SearchFilter{ContractsText=string.Join("\n",data.Packages.Select(p=>p.Contract).DistinctBy(Rules.Contract).Take(10)),Categories=[..Rules.Categories.Take(6)]});
                var tenMs=lookupWatch.Elapsed.TotalMilliseconds;
                lookupWatch.Restart();var all=diagnosticService.Search(new SearchFilter());var allMs=lookupWatch.Elapsed.TotalMilliseconds;
                var result=new { source=master,milliseconds=watch.Elapsed.TotalMilliseconds,profile=store.LastReadProfile,tenCodeSearchMilliseconds=tenMs,allContractsSearchMilliseconds=allMs,packages=data.Packages.Count,contracts=data.Packages.Select(x=>Rules.Contract(x.Contract)).Distinct().Count(),projects=data.Packages.Select(x=>x.Identity).Distinct().Count(),lines=data.Packages.Sum(x=>x.ItemCount),contPackages=data.ContPackages.Count,arrivalPackages=data.DetailPackages.Count,warnings=data.Warnings };
                File.WriteAllText(Argument(e.Args,"--output")??Path.Combine(Path.GetTempPath(),"hisa-csharp-benchmark.json"),JsonSerializer.Serialize(result,WarehouseService.Json));
                Shutdown();return;
            }
            var workspace=e.Args.Contains("--demo")?SelfTests.CreateDemoWorkspace():Argument(e.Args,"--workspace");
            var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace??AppContext.BaseDirectory)))[..16];
            _instance=new Mutex(true,"Local\\Hisa.KhoVatTu.Native."+identity,out var fresh);
            if(!fresh)
            {
                MessageBox.Show(SoftwareInfo.DisplayName+" đang mở. Bạn có thể chuyển sang cửa sổ ứng dụng trên thanh tác vụ.",SoftwareInfo.DisplayName+" · Ứng dụng đang chạy",MessageBoxButton.OK,MessageBoxImage.Information);
                Shutdown();return;
            }
            _service=new WarehouseService(workspace);
            if(!e.Args.Contains("--demo"))
            {
                var shortcut=Path.Combine(_service.Workspace,"HISA Kho Vat Tu CSharp.lnk");
                try{if(!File.Exists(shortcut)&&Environment.ProcessPath!=null)WindowsShortcut.Create(shortcut,Environment.ProcessPath);}
                catch(Exception ex){_service.Log("shortcut",ex);}
            }
            MainWindow=new MainWindow(_service);
            if(e.Args.Contains("--demo"))MainWindow.Title+=" · DỮ LIỆU THỬ";
            MainWindow.Show();
        }
        catch(Exception ex)
        {
            try{File.AppendAllText(Path.Combine(Path.GetTempPath(),"hisa-native-startup.log"),$"{DateTime.Now:O} {ex}\n");}catch{}
            var output=Argument(e.Args,"--output");
            if(output!=null)File.WriteAllText(output,JsonSerializer.Serialize(new{success=false,error=ex.ToString()},WarehouseService.Json));
            else MessageBox.Show(ex.Message,SoftwareInfo.DisplayName+" · Không mở được ứng dụng",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private static string? Argument(string[] args,string key)
    {
        var index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:null;
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _service?.Dispose();_instance?.Dispose();base.OnExit(e);
    }
}
