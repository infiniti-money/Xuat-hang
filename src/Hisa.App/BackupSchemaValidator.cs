using System.IO;
using System.Text.Json;

namespace Hisa.App;

/// <summary>
/// JSON syntax alone is not sufficient for a safe restore: a valid JSON object
/// stored in slips.json will crash WarehouseService on the next startup.
/// Validate the file's expected model before replacing any current data.
/// </summary>
public static class BackupSchemaValidator
{
    public static void Validate(string name,string path)
    {
        try
        {
            var content=File.ReadAllText(path);
            using var document=JsonDocument.Parse(content);
            var root=document.RootElement;
            if(root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new InvalidDataException("JSON phải là đối tượng hoặc danh sách: "+name);

            if(name.Equals("slips.json",StringComparison.OrdinalIgnoreCase))
            {
                Ensure(root.ValueKind==JsonValueKind.Array,name);
                var items=JsonSerializer.Deserialize<List<Slip>>(content,WarehouseService.Json);
                Ensure(items!=null && items.All(x=>x!=null && x.Lines!=null &&
                    x.Contracts!=null && x.ContractOnly!=null && x.ContractPackages!=null &&
                    x.MotorSerials!=null && x.StatusHistory!=null &&
                    x.Lines.All(y=>y!=null) && x.ContractPackages.All(y=>y!=null && y.Items!=null)),
                    name);
            }
            else if(name.Equals("templates.json",StringComparison.OrdinalIgnoreCase))
            {
                Ensure(root.ValueKind==JsonValueKind.Array,name);
                var items=JsonSerializer.Deserialize<List<PrintTemplate>>(content,WarehouseService.Json);
                Ensure(items!=null && items.All(x=>x!=null && x.Id!=null && x.Signatures!=null),
                    name);
            }
            else if(name.Equals("contract_flags.json",StringComparison.OrdinalIgnoreCase))
            {
                Ensure(root.ValueKind==JsonValueKind.Array,name);
                var flags=JsonSerializer.Deserialize<List<ContractFlag>>(content,WarehouseService.Json);
                Ensure(flags!=null && flags.All(x=>x!=null && x.Contract!=null),name);
            }
            else if(name.Equals("settings.json",StringComparison.OrdinalIgnoreCase))
            {
                Ensure(root.ValueKind==JsonValueKind.Object,name);
                var settings=JsonSerializer.Deserialize<AppSettings>(content,WarehouseService.Json);
                Ensure(settings!=null && !string.IsNullOrWhiteSpace(settings.MasterPath) &&
                    settings.DataSources!=null && settings.DataSources.All(x=>x!=null) &&
                    settings.KtPath!=null && settings.ProductExportPath!=null &&
                    settings.PollSeconds>=5 && settings.PollSeconds<=3600,name);
            }
            else if(name.Equals("cache.json",StringComparison.OrdinalIgnoreCase))
            {
                Ensure(root.ValueKind==JsonValueKind.Object,name);
                var cached=JsonSerializer.Deserialize<WarehouseService.CacheEnvelope>(content,
                    WarehouseService.Json);
                Ensure(cached!=null && cached.Snapshot!=null &&
                    cached.Snapshot.Packages!=null && cached.Snapshot.ContPackages!=null &&
                    cached.Snapshot.DetailPackages!=null && cached.Snapshot.Warnings!=null &&
                    cached.Shipments!=null,name);
            }
            else if(name.StartsWith("ai_schedules/lich-kho-",StringComparison.OrdinalIgnoreCase))
            {
                Ensure(root.ValueKind==JsonValueKind.Object,name);
                var schedule=JsonSerializer.Deserialize<AiScheduleDocument>(content,WarehouseService.Json);
                Ensure(schedule!=null && schedule.Arrivals!=null && schedule.Dispatches!=null &&
                    schedule.Warnings!=null && schedule.ApprovedAt!=null,name);
            }
        }
        catch(JsonException error)
        {
            throw new InvalidDataException("Dữ liệu JSON trong ZIP không đúng cấu trúc: "+
                name+". "+error.Message,error);
        }
    }

    private static void Ensure(bool okay,string name)
    {
        if(!okay)throw new InvalidDataException(
            "Tệp sao lưu không đúng kiểu dữ liệu của HISA: "+name);
    }
}
