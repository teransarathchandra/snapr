using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using Snapr.App.Dialogs;
using Snapr.App.Services;
using Snapr.Core;

namespace Snapr.App;

public sealed partial class AppController
{
    public async Task GrabTextAsync(BitmapSource image)
    {
        string text;
        try
        {
            text = await OcrService.RecognizeAsync(image);
        }
        catch (OcrUnavailableException)
        {
            ShowOcrHelp();
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            Notify("No text found", "Snapr couldn't find any text in that area.");
            return;
        }
        if (!ClipboardImage.SetText(text)) Log.Write("Grab Text: clipboard busy, showing result only");
        TextResultWindow.Open(text);
    }

    /// <summary>Shown when Windows has no OCR language (Grab Text and Redact).</summary>
    public void ShowOcrHelp()
    {
        var answer = MessageBox.Show("Reading text needs a Windows OCR language. Add one under Settings → Time & language → Language (any language with \"Optical character recognition\").\n\nOpen Settings now?",
            "Snapr", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });
    }
}
