using System.Globalization;
using System.Text.RegularExpressions;

namespace Hisa.App;

/// <summary>
/// Local, deterministic extraction of an EXPLICIT request to PREPARE a slip.
/// No model-generated command, data field or voice transcript can invoke Excel
/// writes through this parser. Only the existing HISA review editor can save.
/// </summary>
public sealed record MaySlipProposal(IReadOnlyList<string> Contracts,string Day)
{
    public const int MaximumContracts=10;
    public string Summary=>Contracts.Count==0
        ? "Mây cần số hợp đồng trước khi lên phiếu nháp."
        : "Phiếu nháp CHỜ XUẤT · HĐ "+string.Join(", ",Contracts)+
          " · ngày "+DateTime.ParseExact(Day,"yyyy-MM-dd",CultureInfo.InvariantCulture)
              .ToString("dd/MM/yyyy",CultureInfo.InvariantCulture);
}

public static class MaySlipIntent
{
    private static readonly Regex RequestVerb=new(
        @"\b(LAP|TAO|SOAN|CHUAN BI|LEN|LAM|DE XUAT)\b",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly Regex SlipNoun=new(
        @"\b(PHIEU|PHIEU XUAT|PHIEU BAN GIAO)\b",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly Regex Negation=new(
        @"\b(KHONG|DUNG|CHUA|HUY)\s+(?:CO\s+)?(?:LAP|TAO|SOAN|LEN|LAM)\s+PHIEU\b",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);

    public static MaySlipProposal? Parse(string? utterance,DateTime today)
    {
        if(string.IsNullOrWhiteSpace(utterance))return null;
        var norm=Rules.Norm(utterance);
        if(!RequestVerb.IsMatch(norm)||!SlipNoun.IsMatch(norm)||Negation.IsMatch(norm))
            return null;
        var codes=GeminiChatService.ContractNumbers(utterance).ToList();
        if(codes.Count>MaySlipProposal.MaximumContracts)
            throw new InvalidOperationException(
                "Mây chỉ chuẩn bị tối đa 10 hợp đồng mỗi lượt; vui lòng chia nhỏ yêu cầu.");
        // Date is read ONLY from the user's direct utterance; no hallucinated dates.
        var date=today.Date;
        if(Regex.IsMatch(norm,@"\bNGAY KIA\b"))
            date=date.AddDays(2);
        else if(Regex.IsMatch(norm,@"\bNGAY MAI\b"))
            date=date.AddDays(1);
        else
        {
            var iso=Regex.Match(utterance,@"(?<!\d)(\d{4})[-](\d{2})[-](\d{2})(?!\d)");
            var vi=Regex.Match(utterance,@"(?<!\d)(\d{1,2})[\/](\d{1,2})[\/](\d{4})(?!\d)");
            if(iso.Success &&
                DateTime.TryParseExact(iso.Value,"yyyy-MM-dd",CultureInfo.InvariantCulture,
                    DateTimeStyles.None,out var isoDate))date=isoDate;
            else if(vi.Success &&
                DateTime.TryParseExact(vi.Value,["d/M/yyyy","dd/MM/yyyy"],
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var viDate))date=viDate;
        }
        return new MaySlipProposal(codes.OrderBy(x=>x,StringComparer.Ordinal).ToList(),
            date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
    }
}
