namespace Hisa.App;

/// <summary>Physical paper dimensions for slip templates, measured in millimeters.</summary>
public static class PrintPaperSizes
{
    private static readonly (string Key, string Label, double WidthMm, double HeightMm)[] Formats =
    [
        ("A3", "A3 · 297 × 420 mm", 297, 420),
        ("A4", "A4 · 210 × 297 mm", 210, 297),
        ("A5", "A5 · 148 × 210 mm", 148, 210),
        ("B5", "B5 (ISO) · 176 × 250 mm", 176, 250),
        ("Letter", "Letter · 216 × 279 mm", 215.9, 279.4),
        ("Legal", "Legal · 216 × 356 mm", 215.9, 355.6)
    ];

    public static (string Key, string Label)[] Options =>
        Formats.Select(x => (x.Key, x.Label)).ToArray();

    public static (double WidthMm, double HeightMm) GetMillimeters(string paperSize)
    {
        var format = Formats.FirstOrDefault(x =>
            string.Equals(x.Key, paperSize, StringComparison.OrdinalIgnoreCase));
        if (format.Key is null)
            throw new ArgumentException("Khổ giấy không hỗ trợ: " + paperSize +
                ". Hãy chọn khổ giấy trong danh sách mẫu in.");
        return (format.WidthMm, format.HeightMm);
    }
}
