using System.IO;

namespace Hisa.App;

/// <summary>Isolated preference tests; never touch real credentials or consent.</summary>
public static class MayAssistantPreferencesDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string name)
        {
            if(!ok)throw new InvalidOperationException("Mây setting: "+name);
            checks.Add("Mây setting: "+name);
        }
        var dir=Path.Combine(Path.GetTempPath(),"hisa-may-settings-"+Guid.NewGuid().ToString("N"));
        var file=Path.Combine(dir,"voice.json");
        try
        {
            var initial=MayAssistantPreferences.Read(file);
            Check(!initial.AutoSpeak && initial.MicrophoneNumber==-1 &&
                  initial.Sensitivity==0.0045,"New device uses safe default without listening");
            new MayAssistantPreferences{AutoSpeak=true,MicrophoneNumber=2,
                Sensitivity=0.002}.Save(file);
            var saved=MayAssistantPreferences.Read(file);
            Check(saved.AutoSpeak && saved.MicrophoneNumber==2 &&
                  saved.Sensitivity==0.002,"Speaker, mic and noise sensitivity survive restart");
            new MayAssistantPreferences{AutoSpeak=true,MicrophoneNumber=998,
                Sensitivity=double.NaN}.Save(file);
            var bounded=MayAssistantPreferences.Read(file);
            Check(bounded.MicrophoneNumber==-1&&bounded.Sensitivity==0.0045,
                "Invalid persisted microphone and sensitivity are safely reset");
            File.WriteAllText(file,"not json");
            var recovered=MayAssistantPreferences.Read(file);
            Check(!recovered.AutoSpeak&&recovered.MicrophoneNumber==-1,
                "Damaged preferences cannot enable speaker or microphone implicitly");
            Check(!typeof(MayAssistantPreferences).GetProperties()
                .Any(x=>x.Name.Contains("Consent")||x.Name.Contains("ApiKey")),
                "Voice preferences never store Gemini API credentials or permission grants");
        }
        finally {if(Directory.Exists(dir))Directory.Delete(dir,true);}
        return checks;
    }
}
