using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// Non-secret local preferences for the Mây assistant. Microphone permission
/// and warehouse-sharing consent are persisted separately and NEVER inferred
/// from these convenience settings. Reading corrupt preferences fails closed.
/// </summary>
public sealed class MayAssistantPreferences
{
    public bool AutoSpeak { get; set; }
    public int MicrophoneNumber { get; set; }=-1;
    public double Sensitivity { get; set; }=0.0045;

    public static string ConfigPath=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HISA-Kho-Vat-Tu","may-assistant-preferences.json");

    public static MayAssistantPreferences Read(string? path=null)
    {
        try
        {
            var value=JsonSerializer.Deserialize<MayAssistantPreferences>(
                File.ReadAllText(path??ConfigPath))??new();
            return Normalize(value);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException)
        {
            return new MayAssistantPreferences();
        }
    }

    public static MayAssistantPreferences Normalize(MayAssistantPreferences value)=>new()
    {
        AutoSpeak=value.AutoSpeak,
        MicrophoneNumber=value.MicrophoneNumber>=-1 &&
            value.MicrophoneNumber<=128?value.MicrophoneNumber:-1,
        Sensitivity=value.Sensitivity is 0.002 or 0.0045 or 0.010
            ?value.Sensitivity:0.0045
    };

    public void Save(string? path=null)
    {
        var name=path??ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(name))!);
        var temp=name+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,JsonSerializer.Serialize(Normalize(this)));
            File.Move(temp,name,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
