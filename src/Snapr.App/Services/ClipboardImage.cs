using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Snapr.App.Rendering;

namespace Snapr.App.Services;

public static class ClipboardImage
{
    /// <summary>Puts the image on the clipboard as a bitmap and as PNG (keeps transparency in apps that read PNG).</summary>
    public static bool Set(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(ImageIo.FlattenOnWhite(image));   // the bitmap format has no alpha: transparent areas would paste black
        data.SetData("PNG", new MemoryStream(ImageIo.EncodePng(image)), false);
        return Try(() => Clipboard.SetDataObject(data, true));
    }

    public static bool SetText(string text) => Try(() => Clipboard.SetText(text));

    /// <summary>Another app may hold the clipboard open for a moment; retry briefly instead of failing.</summary>
    static bool Try(Action set)
    {
        for (var i = 0; i < 5; i++)
        {
            try { set(); return true; }
            catch (COMException) { Thread.Sleep(60); }
        }
        return false;
    }
}
