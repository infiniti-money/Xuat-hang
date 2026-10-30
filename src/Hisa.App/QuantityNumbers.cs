using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Hisa.App;

/// <summary>Accept decimal comma/dot without treating a fractional quantity as thousands.</summary>
public static class QuantityNumbers
{
    public static bool TryParse(string? text,out decimal value)
    {
        value=0;if(string.IsNullOrWhiteSpace(text))return false;
        var input=text.Trim().Replace('\u00a0',' ').Replace('\u202f',' ');
        if(input.Contains(' '))
        {
            if(!Regex.IsMatch(input,@"^[+-]?\d{1,3}(?: \d{3})+(?:[.,]\d+)?$"))return false;
            input=input.Replace(" ","");
        }
        if(!Regex.IsMatch(input,@"^[+-]?(?:\d+(?:[.,]\d+)*|[.,]\d+)$"))return false;
        var comma=input.Count(c=>c==',');var dot=input.Count(c=>c=='.');
        if(comma>0&&dot>0)
        {
            var separator=input.LastIndexOf(',')>input.LastIndexOf('.')?',':'.';
            if(input.Count(c=>c==separator)!=1)return false;
            var parts=input.Split(separator);var grouping=separator==','?'.':',';
            if(!ValidGroupedInteger(parts[0],grouping))return false;
            input=parts[0].Replace(grouping.ToString(),"")+"."+parts[1];
        }
        else if(comma+dot>1)
        {
            var separator=comma>0?',':'.';
            if(!ValidGroupedInteger(input,separator))return false;
            input=input.Replace(separator.ToString(),"");
        }
        else input=input.Replace(',','.');
        return decimal.TryParse(input,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value);
    }
    private static bool ValidGroupedInteger(string text,char grouping)
    {
        text=text.TrimStart('+','-');var groups=text.Split(grouping);
        return groups.Length>1&&groups[0].Length is >=1 and <=3&&groups.Skip(1).All(x=>x.Length==3&&x.All(char.IsDigit));
    }
}
public sealed class QuantityValueConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is decimal number?number.ToString("0.############################",culture):value??"";
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>QuantityNumbers.TryParse(value?.ToString(),out var result)?result:DependencyProperty.UnsetValue;
}
public sealed class QuantityEntryRule:ValidationRule
{
    public override ValidationResult Validate(object value,CultureInfo cultureInfo)=>QuantityNumbers.TryParse(value?.ToString(),out var number)&&number>=0?ValidationResult.ValidResult:new ValidationResult(false,"Nhập số lượng không âm, ví dụ 1,5 hoặc 1.5.");
}
