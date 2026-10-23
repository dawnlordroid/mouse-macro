namespace MouseMacro;

public readonly record struct MatchResult(int X, int Y, double Score);

public sealed class GrayImage
{
    public int W { get; }
    public int H { get; }
    public float[] D { get; }

    public GrayImage(int w, int h, float[] d) { W = w; H = h; D = d; }

    public static unsafe GrayImage FromBitmap(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var d = new float[w * h];
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
            {
                byte* row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < w; x++)
                {
                    byte* p = row + x * 4;
                    d[y * w + x] = 0.114f * p[0] + 0.587f * p[1] + 0.299f * p[2];
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return new GrayImage(w, h, d);
    }

    public GrayImage Downscale(int s)
    {
        if (s <= 1) return this;
        int nw = W / s, nh = H / s;
        var d = new float[nw * nh];
        float inv = 1f / (s * s);
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                float sum = 0;
                for (int j = 0; j < s; j++)
                    for (int i = 0; i < s; i++)
                        sum += D[(y * s + j) * W + x * s + i];
                d[y * nw + x] = sum * inv;
            }
        return new GrayImage(nw, nh, d);
    }
}

// Normalized cross-correlation (TM_CCOEFF_NORMED) with a coarse-to-fine search.
public static class Matcher
{
    sealed class Prepared
    {
        public GrayImage Img = null!;
        public float[] Zero = null!; // template minus its mean
        public double Norm;          // sqrt(sum(Zero^2))
        public int N;
    }

    static Prepared Prepare(GrayImage t)
    {
        int n = t.W * t.H;
        double mean = 0;
        foreach (var v in t.D) mean += v;
        mean /= n;
        var z = new float[n];
        double ss = 0;
        for (int i = 0; i < n; i++) { z[i] = (float)(t.D[i] - mean); ss += z[i] * (double)z[i]; }
        return new Prepared { Img = t, Zero = z, Norm = Math.Sqrt(ss), N = n };
    }

    sealed class Integral
    {
        public readonly double[] S, S2;
        public readonly int Stride;
        public Integral(GrayImage g)
        {
            Stride = g.W + 1;
            S = new double[Stride * (g.H + 1)];
            S2 = new double[S.Length];
            for (int y = 0; y < g.H; y++)
            {
                double rs = 0, rs2 = 0;
                for (int x = 0; x < g.W; x++)
                {
                    double v = g.D[y * g.W + x];
                    rs += v; rs2 += v * v;
                    S[(y + 1) * Stride + x + 1] = S[y * Stride + x + 1] + rs;
                    S2[(y + 1) * Stride + x + 1] = S2[y * Stride + x + 1] + rs2;
                }
            }
        }
        public double Sum(double[] a, int x, int y, int w, int h) =>
            a[(y + h) * Stride + x + w] - a[y * Stride + x + w] - a[(y + h) * Stride + x] + a[y * Stride + x];
    }

    static double Score(GrayImage s, Integral ii, Prepared t, int x, int y)
    {
        int tw = t.Img.W, th = t.Img.H;
        double sum = ii.Sum(ii.S, x, y, tw, th);
        double sum2 = ii.Sum(ii.S2, x, y, tw, th);
        double var = sum2 - sum * sum / t.N;
        if (var <= 1e-6 || t.Norm <= 1e-6) return 0;
        double dot = 0;
        for (int j = 0; j < th; j++)
        {
            int so = (y + j) * s.W + x;
            int to = j * tw;
            for (int i = 0; i < tw; i++) dot += s.D[so + i] * t.Zero[to + i];
        }
        return dot / (Math.Sqrt(var) * t.Norm);
    }

    // Returns the best match (score is -1..1) or null if the template doesn't fit.
    public static MatchResult? Find(GrayImage screen, GrayImage template)
    {
        if (template.W > screen.W || template.H > screen.H || template.W < 2 || template.H < 2) return null;

        int scale = Math.Clamp(Math.Min(template.W, template.H) / 12, 1, 4);
        var cs = screen.Downscale(scale);
        var ct = template.Downscale(scale);
        if (ct.W < 2 || ct.H < 2 || ct.W > cs.W || ct.H > cs.H) { scale = 1; cs = screen; ct = template; }

        var pt = Prepare(ct);
        var ii = new Integral(cs);
        int rw = cs.W - ct.W + 1, rh = cs.H - ct.H + 1;
        var scores = new double[rw * rh];
        Parallel.For(0, rh, y =>
        {
            for (int x = 0; x < rw; x++) scores[y * rw + x] = Score(cs, ii, pt, x, y);
        });

        if (scale == 1)
        {
            int bi = 0;
            for (int i = 1; i < scores.Length; i++) if (scores[i] > scores[bi]) bi = i;
            return new MatchResult(bi % rw, bi / rw, scores[bi]);
        }

        // Pick the best few distinct coarse candidates, then refine at full resolution.
        var order = Enumerable.Range(0, scores.Length).OrderByDescending(i => scores[i]).Take(400).ToList();
        var picked = new List<int>();
        foreach (var i in order)
        {
            if (picked.Count >= 8 || scores[i] < 0.3) break;
            int cx = i % rw, cy = i / rw;
            if (picked.All(p => Math.Abs(p % rw - cx) > 2 || Math.Abs(p / rw - cy) > 2)) picked.Add(i);
        }
        if (picked.Count == 0) picked.Add(order[0]);

        var pf = Prepare(template);
        var fi = new Integral(screen);
        MatchResult best = new(0, 0, -2);
        foreach (var i in picked)
        {
            int bx = (i % rw) * scale, by = (i / rw) * scale;
            for (int y = Math.Max(0, by - scale * 2); y <= Math.Min(screen.H - template.H, by + scale * 2); y++)
                for (int x = Math.Max(0, bx - scale * 2); x <= Math.Min(screen.W - template.W, bx + scale * 2); x++)
                {
                    double sc = Score(screen, fi, pf, x, y);
                    if (sc > best.Score) best = new MatchResult(x, y, sc);
                }
        }
        return best;
    }
}
