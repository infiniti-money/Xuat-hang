using System.IO;

namespace Hisa.App;

public static class GeminiCredentialDiagnostics
{
    // A disposable target name prevents tests from reading or modifying real user credentials.
    public static List<string> Run()
    {
        var checks = new List<string>();
        void Check(bool result, string label)
        {
            if (!result) throw new InvalidOperationException("Credential regression failed: " + label);
            checks.Add("Gemini key: " + label);
        }
        Check(GeminiCredentialStore.CredentialTarget=="HISA.KhoVatTu.Gemini.API",
            "Gemini credential target remains stable across installer versions");
        var userLocal=Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var configPaths=new[]{
            MayAssistantPreferences.ConfigPath,
            GeminiVoiceConsentStore.ConfigPath,
            GeminiWarehouseSharingConsent.ConfigPath
        };
        Check(configPaths.All(path=>Path.GetFullPath(path).StartsWith(
                  Path.GetFullPath(userLocal)+Path.DirectorySeparatorChar,
                  StringComparison.OrdinalIgnoreCase)) &&
              configPaths.All(path=>path.Contains("HISA-Kho-Vat-Tu")),
            "Gemini speech, microphone and warehouse-sharing preferences persist under user's LocalAppData instead of Program Files");
        var target = "HISA.TEST.Gemini." + Guid.NewGuid().ToString("N");
        var first = new GeminiCredentialStore(target);
        try
        {
            Check(first.ReadKey() == null && !first.HasKey(), "Missing credential reports not configured");
            const string value = "AIzFakeTestKeyForHisa_123456789";
            first.SaveKey(value);
            Check(first.HasKey(), "Credential is saved to Windows");
            Check(new GeminiCredentialStore(target).ReadKey() == value,
                "Key survives re-opening an independent store instance (update simulation)");
            first.SaveKey("ReplacementGeminiTestKey");
            Check(new GeminiCredentialStore(target).ReadKey() == "ReplacementGeminiTestKey",
                "Replacing key updates existing Windows credential");
            first.DeleteKey();
            Check(first.ReadKey() == null && !first.HasKey(), "Delete removes key");
            first.DeleteKey();
            Check(first.ReadKey() == null, "Repeated delete is safe");
            return checks;
        }
        finally { first.DeleteKey(); }
    }
}
