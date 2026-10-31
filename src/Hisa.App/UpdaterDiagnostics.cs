using System.IO;

namespace Hisa.App;

/// <summary>Offline checks for trusted download locations, SHA-256 and explicit install consent.</summary>
public static class UpdaterDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool okay,string label)
        {
            if(!okay)throw new InvalidOperationException("Updater regression: "+label);
            checks.Add("Updater: "+label);
        }
        var name="HISA-Kho-Vat-Tu-"+SoftwareInfo.Version+"-Setup.exe";
        var official="https://github.com/infiniti-money/Xuat-hang/releases/download/v"+
            SoftwareInfo.Version+"/"+name;
        Check(AutoUpdater.TrustedAssetUri(official).Host=="github.com",
            "HTTPS official GitHub release installer is accepted");
        var untrusted=new[]
        {
            "http://github.com/infiniti-money/Xuat-hang/releases/download/v1.9.1/"+name,
            "https://github.com.evil.example/infiniti-money/Xuat-hang/releases/download/v1.9.1/"+name,
            "https://github.com/another-user/Xuat-hang/releases/download/v1.9.1/"+name,
            "https://github.com/infiniti-money/Xuat-hang/archive/main.zip"
        };
        foreach(var url in untrusted)
        {
            var rejected=false;
            try{AutoUpdater.TrustedAssetUri(url);}
            catch(InvalidDataException){rejected=true;}
            Check(rejected,"Untrusted update URL is rejected");
        }
        var hash=new string('a',64);
        var checksum=hash+"  "+name;
        Check(AutoUpdater.ParseChecksum(checksum,name,"sha256:"+hash)==hash,
            "Installer checksum and GitHub digest match");
        var mismatch=false;
        try{AutoUpdater.ParseChecksum(hash+"  other-Setup.exe",name,"sha256:"+hash);}
        catch(InvalidDataException){mismatch=true;}
        Check(mismatch,"Checksum naming another installer is rejected");
        mismatch=false;
        try{AutoUpdater.ParseChecksum(checksum,name,"sha256:"+new string('b',64));}
        catch(InvalidDataException){mismatch=true;}
        Check(mismatch,"Conflicting GitHub digest is rejected");
        mismatch=false;
        try{AutoUpdater.ParseChecksum(checksum+"\n"+checksum,name,null);}
        catch(InvalidDataException){mismatch=true;}
        Check(mismatch,"Multiple checksum lines are rejected");
        Check(AutoUpdater.CanAppend(System.Net.HttpStatusCode.PartialContent,200,200),
            "Exact HTTP byte-range resumes partial download");
        Check(!AutoUpdater.CanAppend(System.Net.HttpStatusCode.OK,200,null) &&
              !AutoUpdater.CanAppend(System.Net.HttpStatusCode.PartialContent,200,100) &&
              !AutoUpdater.CanAppend(System.Net.HttpStatusCode.PartialContent,0,0),
            "Ignored or mismatched HTTP Range never appends incorrect data");
        var cache=Path.Combine(Path.GetTempPath(),"hisa-cached-setup-test-"+Guid.NewGuid().ToString("N")+".exe");
        try
        {
            File.WriteAllText(cache,"verified-test-file");
            var original=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(cache)));
            Check(AutoUpdater.ReuseVerifiedCachedInstaller(cache,original) && File.Exists(cache),
                "Valid cached installer is reused without deleting it");
            Check(!AutoUpdater.ReuseVerifiedCachedInstaller(cache,new string('0',64)) &&
                  !File.Exists(cache),
                "Corrupt cached installer is safely closed then deleted on Windows");
        }
        finally {if(File.Exists(cache))File.Delete(cache);}
        Check(!AutoUpdater.RequestInstallOnExit(),
            "Installer cannot launch without a verified download and approval");
        return checks;
    }
}
