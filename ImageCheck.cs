using System.Drawing.Drawing2D;

namespace MouseMacro;

static class ImageCheck
{
    public static Bitmap Capture(Rectangle r)
    {
        var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.X, r.Y, 0, 0, bmp.Size);
        return bmp;
    }

    static double[] Gray(Bitmap b)
    {
        var v = new double[b.Width * b.Height];
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                var c = b.GetPixel(x, y);
                v[y * b.Width + x] = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            }
        return v;
    }

    // Normalized cross-correlation on grayscale (robust to dimming overlays). Range -1..1.
    public static double Similarity(Bitmap captured, Bitmap reference)
    {
        Bitmap? scaled = null;
        try
        {
            if (reference.Size != captured.Size)
            {
                scaled = new Bitmap(captured.Width, captured.Height);
                using var g = Graphics.FromImage(scaled);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(reference, 0, 0, captured.Width, captured.Height);
                reference = scaled;
            }
            var a = Gray(captured);
            var b = Gray(reference);
            double ma = a.Average(), mb = b.Average();
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double da = a[i] - ma, db = b[i] - mb;
                sab += da * db; saa += da * da; sbb += db * db;
            }
            if (saa < 1e-6 || sbb < 1e-6)
                return saa < 1e-6 && sbb < 1e-6 && Math.Abs(ma - mb) < 10 ? 1.0 : 0.0;
            return sab / Math.Sqrt(saa * sbb);
        }
        finally { scaled?.Dispose(); }
    }
}
