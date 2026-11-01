using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// One-time, revocable user consent for uploading microphone audio to Google Gemini.
/// Disabled by default; never interpreted as permission to auto-start recording.
/// </summary>
public static class GeminiVoiceConsentStore
{
    private sealed class Consent { public bool AllowMicrophoneAndCloudAudio {get;set;} }
    private static readonly object Sync=new();
    internal static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HISA-Kho-Vat-Tu","voice-consent.json");

    public static bool Read()
    {
        lock(Sync)
        {
            try
            {
                if(!File.Exists(ConfigPath))return false;
                var raw=File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<Consent>(raw)?.AllowMicrophoneAndCloudAudio==true;
            }
            catch { return false; } // Permission fails closed on corruption or IO failure.
        }
    }

    public static void Save(bool allowed)
    {
        lock(Sync)
        {
            var folder=Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(folder);
            var temp=ConfigPath+".tmp";
            try
            {
                File.WriteAllText(temp,JsonSerializer.Serialize(
                    new Consent {AllowMicrophoneAndCloudAudio=allowed}));
                File.Move(temp,ConfigPath,true);
            }
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
