using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Hisa.App;

public static class LogoDiagnostics
{
    public static List<string> Run()
    {
        var result=new List<string>();
        void Check(bool valid,string text){if(!valid)throw new InvalidOperationException("FAIL: "+text);result.Add(text);}
        var resource=Application.GetResourceStream(new Uri(BrandingAssets.LogoUri));
        Check(resource!=null,"Original Hisa logo embedded in the application");
        using(resource!.Stream)Check(resource.Stream.Length==21600,"Logo keeps the original user-supplied JPEG bytes");
        Check(BrandingAssets.Logo.PixelWidth==414&&BrandingAssets.Logo.PixelHeight==414,"User logo loads at original dimensions");
        using var icon=Application.GetResourceStream(new Uri(BrandingAssets.IconUri))!.Stream;
        var decoder=new IconBitmapDecoder(icon,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        Check(new[]{16,24,32,48,64,128,256}.All(size=>decoder.Frames.Any(f=>f.PixelWidth==size&&f.PixelHeight==size)),"Windows icon contains seven correct resolutions");
        return result;
    }
}
