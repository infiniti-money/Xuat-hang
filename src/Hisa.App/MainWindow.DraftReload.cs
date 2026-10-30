namespace Hisa.App;

public partial class MainWindow
{
    private Action? _captureDraftEditor;
    private Action? _captureDraftHeader;
    internal static Slip ReloadDraftMaterials(Slip? existing,Slip loaded)
    {
        if(existing==null)return loaded;
        var result=Copy(existing);
        result.Lines=loaded.Lines;result.ContractPackages=loaded.ContractPackages;result.Contracts=loaded.Contracts;
        try{result.MotorSerials=MotorSerials.BuildRows(result);}
        catch(ArgumentException){ /* Invalid draft motor quantities remain visible for correction before export. */ }
        return result;
    }
}
