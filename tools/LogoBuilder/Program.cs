using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Length!=2)throw new ArgumentException("Usage: LogoBuilder source.jpg destination.ico");
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.GetFullPath(args[0]));bitmap.EndInit();bitmap.Freeze();
        int[] sizes=[16,24,32,48,64,128,256];var frames=new List<byte[]>();
        foreach(var size in sizes)
        {
            var drawing=new DrawingVisual();RenderOptions.SetBitmapScalingMode(drawing,BitmapScalingMode.HighQuality);
            using(var context=drawing.RenderOpen())context.DrawImage(bitmap,new Rect(0,0,size,size));
            var target=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);target.Render(drawing);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));using var output=new MemoryStream();encoder.Save(output);frames.Add(output.ToArray());
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        using var file=File.Create(args[1]);using var writer=new BinaryWriter(file);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
        var offset=6+16*sizes.Length;
        for(int index=0;index<sizes.Length;index++)
        {
            writer.Write((byte)(sizes[index]==256?0:sizes[index]));writer.Write((byte)(sizes[index]==256?0:sizes[index]));writer.Write((byte)0);writer.Write((byte)0);
            writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[index].Length);writer.Write(offset);offset+=frames[index].Length;
        }
        foreach(var frame in frames)writer.Write(frame);
        Console.WriteLine("Created icon with sizes: "+string.Join(", ",sizes));
    }
}
