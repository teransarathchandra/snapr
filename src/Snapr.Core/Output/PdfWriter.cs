using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Snapr.Core.Output;

/// <summary>Writes a one-page PDF 1.4 holding one lossless (Flate) RGB image, sized as the image at 96 DPI.</summary>
public static class PdfWriter
{
    public static byte[] SingleImagePage(int width, int height, byte[] rgb)
    {
        if (width <= 0 || height <= 0 || rgb.Length != width * height * 3)
            throw new ArgumentException($"Expected {width} × {height} RGB pixels.", nameof(rgb));

        static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        double pageW = width * 72.0 / 96, pageH = height * 72.0 / 96;

        byte[] pixels;
        using (var packed = new MemoryStream())
        {
            using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(rgb);
            pixels = packed.ToArray();
        }
        var content = Encoding.ASCII.GetBytes($"q {N(pageW)} 0 0 {N(pageH)} 0 0 cm /Im0 Do Q\n");

        using var pdf = new MemoryStream();
        var offsets = new List<long>();
        void Write(string s) => pdf.Write(Encoding.ASCII.GetBytes(s));
        void Object(string dictionary, byte[]? stream = null)
        {
            offsets.Add(pdf.Position);
            Write($"{offsets.Count} 0 obj\n{dictionary}\n");
            if (stream is not null)
            {
                Write("stream\n");
                pdf.Write(stream);
                Write("\nendstream\n");
            }
            Write("endobj\n");
        }

        Write("%PDF-1.4\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pageW)} {N(pageH)}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
        Object($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {pixels.Length} >>", pixels);
        Object($"<< /Length {content.Length} >>", content);

        var xref = pdf.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }
}
