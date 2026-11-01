using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// Independent, explicitly revocable permission to include a bounded, read-only
/// warehouse summary in Gemini prompts. Never sends raw workbooks or directories.
/// </summary>
public static class GeminiWarehouseSharingConsent
{
    private sealed class Preference {public bool ShareReadOnlySummary {get;set;}}
    private static readonly object Sync=new();
    public static string ConfigPath=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HISA-Kho-Vat-Tu","gemini-warehouse-sharing.json");

    public static bool Read()
    {
        lock(Sync)
        {
            try
            {
                if(!File.Exists(ConfigPath))return false;
                return JsonSerializer.Deserialize<Preference>(
                    File.ReadAllText(ConfigPath))?.ShareReadOnlySummary==true;
            }
            catch{return false;}
        }
    }

    public static void Save(bool enabled)
    {
        lock(Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var temporary=ConfigPath+".tmp";
            try
            {
                File.WriteAllText(temporary,JsonSerializer.Serialize(
                    new Preference{ShareReadOnlySummary=enabled}));
                File.Move(temporary,ConfigPath,true);
            }
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
