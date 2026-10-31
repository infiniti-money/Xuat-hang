using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Hisa.App;

/// <summary>Offline updater tests. No real GitHub calls or real warehouse files.</summary>
public static class UpdateDiagnostics
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(reply(request));
    }

    public static async Task<List<string>> RunAsync()
    {
        var evidence=new List<string>();
        void Check(bool ok,string description)
        {
            if(!ok)throw new InvalidOperationException("Updater: "+description);
            evidence.Add("Updater: "+description);
        }
        var name="HISA-Kho-Vat-Tu-1.9.1-Setup.exe";
        var hash=new string('a',64);
        Check(AutoUpdater.ParseChecksum(hash+"  "+name+"\r\n",name)==hash,
            "Valid SHA-256 file is parsed");
        foreach(var bad in new[]{hash+"  wrong.exe",hash+"  "+name+"\n"+hash+"  "+name,"xyz  "+name})
        {
            bool rejected=false;
            try{AutoUpdater.ParseChecksum(bad,name);}
            catch(InvalidDataException){rejected=true;}
            Check(rejected,"Wrong or ambiguous checksum is rejected");
        }

        var root=Path.Combine(Path.GetTempPath(),"hisa-update-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var partial=Path.Combine(root,"setup.exe.partial");
        var payload=Encoding.ASCII.GetBytes(new string('A',1000));
        var uri=new Uri("https://github.com/infiniti-money/Xuat-hang/releases/download/test/setup.exe");
        try
        {
            // The first download had been interrupted after 60%: resume exactly at byte 600.
            File.WriteAllBytes(partial,payload[..600]);
            long? observedRange=null;
            using(var client=new HttpClient(new StubHandler(request=>
            {
                observedRange=request.Headers.Range?.Ranges.Single().From;
                var response=new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {Content=new ByteArrayContent(payload[600..])};
                response.Content.Headers.ContentRange=new ContentRangeHeaderValue(600,999,1000);
                return response;
            })))
                await AutoUpdater.DownloadInstallerAsync(client,uri,partial,1000,CancellationToken.None);
            Check(observedRange==600 && File.ReadAllBytes(partial).SequenceEqual(payload),
                "60 percent interruption resumes without duplicating bytes");

            // Some CDNs ignore Range. 200 means start from zero, not append duplicate bytes.
            File.WriteAllBytes(partial,payload[..600]);
            using(var client=new HttpClient(new StubHandler(request=>
                new HttpResponseMessage(HttpStatusCode.OK)
                    {Content=new ByteArrayContent(payload)})))
                await AutoUpdater.DownloadInstallerAsync(client,uri,partial,1000,CancellationToken.None);
            Check(File.ReadAllBytes(partial).SequenceEqual(payload),
                "Server ignoring Range resets instead of corrupting installer");

            File.WriteAllBytes(partial,payload[..600]);
            bool rejectedRange=false;
            using(var client=new HttpClient(new StubHandler(request=>
            {
                var response=new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {Content=new ByteArrayContent(payload[550..])};
                response.Content.Headers.ContentRange=new ContentRangeHeaderValue(550,999,1000);
                return response;
            })))
            {
                try{await AutoUpdater.DownloadInstallerAsync(client,uri,partial,1000,CancellationToken.None);}
                catch(InvalidDataException){rejectedRange=true;}
            }
            Check(rejectedRange && new FileInfo(partial).Length==600,
                "Incorrect resume offset is blocked and partial file is kept");

            // A connection error must not delete the already downloaded bytes.
            bool networkFailure=false;
            using(var client=new HttpClient(new StubHandler(request=>
                throw new HttpRequestException("Simulated disconnect"))))
            {
                try{await AutoUpdater.DownloadInstallerAsync(client,uri,partial,1000,CancellationToken.None);}
                catch(HttpRequestException){networkFailure=true;}
            }
            Check(networkFailure && new FileInfo(partial).Length==600,
                "Connection failure retains partial installer for retry");
            Check(!AutoUpdater.RequestInstall(),
                "No installer can be launched without a verified staged file");
        }
        finally { Directory.Delete(root,true); }
        return evidence;
    }
}
