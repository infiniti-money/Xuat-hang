namespace Hisa.App;

/// <summary>
/// Changes only the "Xuất / in" flags of material lines in one slip.
/// Never deletes lines, changes quantities, or writes to Excel.
/// </summary>
public static class SlipMaterialGroupSelection
{
    public static List<string> AvailableGroups(IEnumerable<MaterialLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var groups=lines.Select(line=>line.Group)
            .Where(group=>!string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.Ordinal).ToList();
        var order=HandoverMaterialGroups.All;
        return groups.OrderBy(group=>
        {
            var index=order.IndexOf(group);
            return index<0?int.MaxValue:index;
        }).ThenBy(group=>group,StringComparer.CurrentCulture).ToList();
    }

    /// <returns>Number of checkboxes whose Include flag actually changed.</returns>
    public static int SetGroupIncluded(IEnumerable<MaterialLine> lines,
        string group,bool include)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if(string.IsNullOrWhiteSpace(group))
            throw new InvalidOperationException("Chọn nhóm vật tư cần thay đổi trước.");
        var target=lines.Where(line=>string.Equals(line.Group,group,
            StringComparison.Ordinal)).ToList();
        if(target.Count==0)
            throw new InvalidOperationException(
                "Phiếu không còn dòng vật tư thuộc nhóm '"+group+"'.");
        int changed=0;
        foreach(var line in target)
        {
            if(line.Include==include)continue;
            line.Include=include;
            changed++;
        }
        return changed;
    }
}
