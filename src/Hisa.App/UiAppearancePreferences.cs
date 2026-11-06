using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// Stores the non-secret visual preference separately from warehouse data.
/// Corrupt/locked settings fall back to the accessible light theme.
/// </summary>
public sealed class UiAppearancePreferences
{
    public bool DarkMode { get; set; }

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HISA-Kho-Vat-Tu", "ui-appearance.json");

    public static UiAppearancePreferences Read(string? path = null)
    {
        try
        {
            var target=path??ConfigPath;
            if(!File.Exists(target))return new();
            return JsonSerializer.Deserialize<UiAppearancePreferences>(
                File.ReadAllText(target)) ?? new();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException)
        {
            return new();
        }
    }

    public void Save(string? path = null)
    {
        var target=Path.GetFullPath(path??ConfigPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,JsonSerializer.Serialize(this));
            File.Move(temp,target,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}

/// <summary>Isolated tests: never alter the current user's appearance preferences.</summary>
public static class UiAppearanceDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        var path=Path.Combine(Path.GetTempPath(),
            "hisa-ui-theme-test-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            if(UiAppearancePreferences.Read(path).DarkMode)
                throw new InvalidOperationException("Appearance defaults should be light.");
            checks.Add("Appearance: missing settings default to light");

            new UiAppearancePreferences{DarkMode=true}.Save(path);
            if(!UiAppearancePreferences.Read(path).DarkMode)
                throw new InvalidOperationException("Dark-mode preference was not retained.");
            checks.Add("Appearance: dark preference survives app restart");

            File.WriteAllText(path,"invalid json");
            if(UiAppearancePreferences.Read(path).DarkMode)
                throw new InvalidOperationException("Corrupt preferences should fail closed.");
            checks.Add("Appearance: corrupt preferences safely fall back to light");
        }
        finally {if(File.Exists(path))File.Delete(path);}
        return checks;
    }
}
