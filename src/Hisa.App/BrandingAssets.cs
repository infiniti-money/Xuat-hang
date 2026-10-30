using System.Windows.Media.Imaging;

namespace Hisa.App;

public static class BrandingAssets
{
    public const string LogoUri="pack://application:,,,/Assets/hisa-logo.jpg";
    public const string IconUri="pack://application:,,,/Assets/hisa.ico";
    private static BitmapImage? _logo;
    public static BitmapImage Logo
    {
        get
        {
            if(_logo!=null)return _logo;
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(LogoUri);image.EndInit();image.Freeze();return _logo=image;
        }
    }
}
