using System.Text.Json.Serialization;

namespace Hisa.App;

/// <summary>Read-only evidence shown while entering contracts and before saving a slip.</summary>
public sealed class SlipWarning
{
    public string Code { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Project { get; set; } = "";
    public string Company { get; set; } = "";
    public string Message { get; set; } = "";
    public string SlipId { get; set; } = "";
    public string SlipCode { get; set; } = "";
    public string Date { get; set; } = "";
    public string Source { get; set; } = "";
    public string Reason { get; set; } = "";
    [JsonIgnore] public string DateLabel => Rules.Day(Date);
    [JsonIgnore] public string Label => Code switch
    {
        "missing" => "Không có dữ liệu",
        "unverified" => "Chưa đối chiếu được",
        "shipped" => "Đã xuất hàng",
        "handed_over" => "Đã bàn giao",
        "existing_slip" => "Đã có phiếu bàn giao",
        "delayed" => "Tạm hoãn",
        "cancelled" => "Đã hủy",
        _ => "Cần kiểm tra"
    };
}

public sealed class SlipWarningReport
{
    public int RequestedCount { get; set; }
    public bool DataAvailable { get; set; }
    public List<SlipWarning> Items { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    [JsonIgnore] public bool HasWarnings => Items.Count > 0 || Warnings.Count > 0;
    [JsonIgnore] public int MissingCount => Count("missing");
    [JsonIgnore] public int UnverifiedCount => Count("unverified");
    [JsonIgnore] public int ShippedCount => Count("shipped");
    [JsonIgnore] public int HandedOverCount => Count("handed_over");
    [JsonIgnore] public int ExistingSlipCount => Count("existing_slip");
    [JsonIgnore] public int DelayedCount => Count("delayed");
    [JsonIgnore] public int CancelledCount => Count("cancelled");
    [JsonIgnore] public string Summary
    {
        get
        {
            if (RequestedCount == 0) return "Nhập số hợp đồng để kiểm tra dữ liệu và lịch sử bàn giao.";
            var counts = new List<string>();
            if (MissingCount > 0) counts.Add($"{MissingCount} mã không có dữ liệu");
            if (UnverifiedCount > 0) counts.Add($"{UnverifiedCount} mã chưa đối chiếu được");
            if (ShippedCount > 0) counts.Add($"{ShippedCount} HĐ đã xuất hàng");
            if (HandedOverCount > 0) counts.Add($"{HandedOverCount} HĐ đã bàn giao");
            if (ExistingSlipCount > 0) counts.Add($"{ExistingSlipCount} HĐ đã có phiếu bàn giao");
            if (DelayedCount > 0) counts.Add($"{DelayedCount} HĐ tạm hoãn");
            if (CancelledCount > 0) counts.Add($"{CancelledCount} HĐ đã hủy");
            if (Warnings.Count > 0) counts.Add($"{Warnings.Count} lưu ý nguồn dữ liệu");
            return $"Đã kiểm tra {RequestedCount} số hợp đồng · " + (counts.Count == 0 ? "không có cảnh báo." : string.Join(" · ", counts));
        }
    }
    private int Count(string code) => Items.Where(x => x.Code == code).Select(x => Rules.Contract(x.Contract)).Distinct().Count();
}
