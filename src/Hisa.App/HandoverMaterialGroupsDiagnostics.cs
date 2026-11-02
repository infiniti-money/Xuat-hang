namespace Hisa.App;

/// <summary>Six-group handover filtering tests, with no external Excel writes.</summary>
public static class HandoverMaterialGroupsDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException("Handover groups: "+message);
            checks.Add(message);
        }
        var keys=HandoverMaterialGroups.All;
        Check(keys.SequenceEqual(new[]{"Tủ điện","Máy kéo","Đầu cửa tầng","Đầu cửa car",
            "Ray dẫn hướng","Bass nối ray"}) &&
            HandoverMaterialGroups.Options.Last().Label=="Bass nối" &&
            !keys.Contains("Khác"),
            "Exactly six requested groups, with visible Bass nối label and no default Khác");
        var two=HandoverMaterialGroups.BuildFilter("10392026",["Tủ điện","Máy kéo"]);
        Check(two.ContractsText=="10392026" &&
              two.Categories.SequenceEqual(["Tủ điện","Máy kéo"]) &&
              Rules.MaterialMatches(new MaterialLine{Material="Máy kéo"},
                  two.Categories,[]) &&
              Rules.MaterialMatches(new MaterialLine{Material="Tủ điện"},
                  two.Categories,[]) &&
              !Rules.MaterialMatches(new MaterialLine{Material="Đầu cửa tầng"},
                  two.Categories,[]) &&
              !Rules.MaterialMatches(new MaterialLine{Material="Vật tư khác"},
                  two.Categories,[]),
            "Search filter includes only checked groups, never other categories or miscellaneous");
        var all=HandoverMaterialGroups.RequireSelected(
            HandoverMaterialGroups.All.Concat(["Máy kéo"]));
        Check(all.Count==6,"Select all groups is idempotent");
        bool blockedNone=false;
        try{HandoverMaterialGroups.BuildFilter("10392026",[]);}
        catch(InvalidOperationException){blockedNone=true;}
        Check(blockedNone,"Empty selection cannot implicitly become all material groups");
        bool blockedUnknown=false;
        try{HandoverMaterialGroups.RequireSelected(["Máy kéo","Khác"]);}
        catch(InvalidOperationException){blockedUnknown=true;}
        Check(blockedUnknown,"Unsupported group Khác cannot be silently included");
        var emptyReport=new ContractReport
        {
            NoMaterialMatch=[new ContractRow {Contract="10392026",
                Project="T1",Selected=true}]
        };
        bool missingMaterials=false;
        try{HandoverMaterialGroups.EnsureSelectedContractsHaveMaterials(emptyReport);}
        catch(InvalidOperationException ex)
        {
            missingMaterials=ex.Message.Contains("10392026") &&
                ex.Message.Contains("chọn thêm nhóm vật tư");
        }
        Check(missingMaterials,"Selected contract without selected-group material is blocked with specific warning");
        emptyReport.NoMaterialMatch[0].Selected=false;
        HandoverMaterialGroups.EnsureSelectedContractsHaveMaterials(emptyReport);
        Check(true,"Unselected contract without material never blocks other selected contracts");
        return checks;
    }
}
