namespace Hisa.App;

/// <summary>
/// Offline regression checks for bulk group toggles in the handover-slip editor.
/// No window, printer, workbook or network activity.
/// </summary>
public static class SlipMaterialGroupSelectionDiagnostics
{
    public static List<string> Run()
    {
        var checks=new List<string>();
        void Check(bool condition,string label)
        {
            if(!condition)throw new InvalidOperationException(
                "Slip group selection: "+label);
            checks.Add("Slip group selection: "+label);
        }

        var ray1=new MaterialLine{Id="a",Contract="34672026",
            Project="Công trình A",Material="Ray dẫn hướng T78",Category="Ray dẫn hướng",
            Quantity=40,Include=true};
        var ray2=new MaterialLine{Id="b",Contract="28872026",
            Project="Công trình B",Material="Ray dẫn hướng T78",Category="Ray dẫn hướng",
            Quantity=20,Include=true};
        var door=new MaterialLine{Id="c",Contract="34672026",
            Material="Đầu cửa tầng",Category="Đầu cửa tầng",Quantity=6,Include=true};
        var bass=new MaterialLine{Id="d",Contract="28872026",
            Material="Bass nối T78",Category="Bass nối ray",Quantity=4,Include=false};
        var rows=new List<MaterialLine>{ray1,ray2,door,bass};
        Check(SlipMaterialGroupSelection.AvailableGroups(rows).SequenceEqual(
                ["Đầu cửa tầng","Ray dẫn hướng","Bass nối ray"]),
            "Group picker shows only material groups present in slip");
        Check(SlipMaterialGroupSelection.SetGroupIncluded(
                rows,"Ray dẫn hướng",false)==2 &&
              !ray1.Include && !ray2.Include && door.Include && !bass.Include,
            "Unchecking Ray affects both contracts but not other groups");
        Check(SlipMaterialGroupSelection.SetGroupIncluded(
                rows,"Ray dẫn hướng",false)==0,
            "Unchecking an already disabled group is idempotent");
        Check(SlipMaterialGroupSelection.SetGroupIncluded(
                rows,"Ray dẫn hướng",true)==2 &&
              ray1.Include && ray2.Include && door.Include && !bass.Include,
            "Rechecking Ray restores only that group");
        Check(SlipMaterialGroupSelection.SetGroupIncluded(
                rows,"Bass nối ray",true)==1 && bass.Include,
            "An originally unchecked group can be restored separately");
        Check(rows.Count==4 && rows[0]==ray1 && rows[1]==ray2 &&
              ray1.Quantity==40 && ray2.Quantity==20 &&
              ray1.Contract=="34672026" && ray2.Contract=="28872026",
            "Group operations do not delete/reorder lines or change quantities/contracts");
        bool refused=false;
        try { SlipMaterialGroupSelection.SetGroupIncluded(
            rows,"Máy kéo",false); }
        catch(InvalidOperationException){refused=true;}
        Check(refused && rows.All(x=>x.Include),
            "Unavailable groups cannot silently change another line");
        return checks;
    }
}
