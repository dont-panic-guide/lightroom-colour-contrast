using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DaftVectorscope
{
    public class ColorTarget
    {
        public string Name { get; set; }
        public double R { get; set; }
        public double G { get; set; }
        public double B { get; set; }
        public double Cb { get; set; }
        public double Cr { get; set; }

        public ColorTarget(string name, double r, double g, double b)
        {
            Name = name;
            R = r;
            G = g;
            B = b;
            Cb = -0.1146 * r - 0.3854 * g + 0.5000 * b;
            Cr =  0.5000 * r - 0.4542 * g - 0.0458 * b;
        }
    }

    public class TargetColorPoint
    {
        public double Hue { get; set; }
        public double Sat { get; set; }

        public TargetColorPoint(double hue, double sat)
        {
            Hue = ((hue % 360.0) + 360.0) % 360.0;
            Sat = Math.Max(0.0, Math.Min(100.0, sat));
        }
    }

    public class ScopeSettings
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string StatsOutputPath { get; set; }
        public int Size { get; set; } = 512;
        public float Gain { get; set; } = 1.0f;
        public float Intensity { get; set; } = 2.5f;
        public bool ShowSkinLine { get; set; } = true;
        public bool ShowTargets { get; set; } = true;
        public bool ShowRings { get; set; } = true;
        public bool ShowAxes { get; set; } = true;
        public List<TargetColorPoint> Targets { get; set; } = new List<TargetColorPoint>();
        public float Strength { get; set; } = 0.0f;
        public float Density { get; set; } = 0.0f;
        public float Bandwidth { get; set; } = 40.0f;
        public float PinBlackWhite { get; set; } = 100.0f;
        public float[] BandSat { get; set; } = null;
    }

    public class VectorscopeCore
    {
        // 75% color targets for Rec.709
        public static readonly ColorTarget[] Targets75 = new ColorTarget[]
        {
            new ColorTarget("R",  0.75, 0.00, 0.00),
            new ColorTarget("Mg", 0.75, 0.00, 0.75),
            new ColorTarget("B",  0.00, 0.00, 0.75),
            new ColorTarget("Cy", 0.00, 0.75, 0.75),
            new ColorTarget("G",  0.00, 0.75, 0.00),
            new ColorTarget("Yl", 0.75, 0.75, 0.00),
        };

        // 100% color targets for Rec.709
        public static readonly ColorTarget[] Targets100 = new ColorTarget[]
        {
            new ColorTarget("R",  1.00, 0.00, 0.00),
            new ColorTarget("Mg", 1.00, 0.00, 1.00),
            new ColorTarget("B",  0.00, 0.00, 1.00),
            new ColorTarget("Cy", 0.00, 1.00, 1.00),
            new ColorTarget("G",  0.00, 1.00, 0.00),
            new ColorTarget("Yl", 1.00, 1.00, 0.00),
        };

        // Skin Tone Angle in Rec.709 Cb-Cr coordinates (~132 degrees / ~11 o'clock)
        public const double SkinToneAngleDegrees = 132.0;

        // Precalculated lookup tables for fast RGB to Cb/Cr conversion (zero divisions at runtime)
        private static readonly double[] Cb_R = new double[256];
        private static readonly double[] Cb_G = new double[256];
        private static readonly double[] Cb_B = new double[256];
        private static readonly double[] Cr_R = new double[256];
        private static readonly double[] Cr_G = new double[256];
        private static readonly double[] Cr_B = new double[256];
        private static readonly float[] ByteToNorm = new float[256];

        static VectorscopeCore()
        {
            for (int i = 0; i < 256; i++)
            {
                double norm = i / 255.0;
                ByteToNorm[i] = (float)norm;
                Cb_R[i] = -0.1146 * norm;
                Cb_G[i] = -0.3854 * norm;
                Cb_B[i] =  0.5000 * norm;
                Cr_R[i] =  0.5000 * norm;
                Cr_G[i] = -0.4542 * norm;
                Cr_B[i] = -0.0458 * norm;
            }
        }

        public static double GetScale(int size)
        {
            double cx = size / 2.0;
            // 0.596 is max chroma radius for 100% saturation (Magenta)
            return (cx * 0.88) / 0.596;
        }

        public static void RgbToCbCr(double r, double g, double b, out double cb, out double cr)
        {
            cb = -0.1146 * r - 0.3854 * g + 0.5000 * b;
            cr =  0.5000 * r - 0.4542 * g - 0.0458 * b;
        }

        public static Bitmap RenderVectorscope(Bitmap sourceBitmap, ScopeSettings settings)
        {
            int size = settings.Size;
            float cx = size / 2.0f;
            float cy = size / 2.0f;
            double scale = GetScale(size);
            double gainScale = scale * settings.Gain;

            // Fast flat accumulation buffer: [R, G, B, Weight] per pixel
            int totalCells = size * size;
            float[] accumBuffer = new float[totalCells * 4];

            int srcW = sourceBitmap.Width;
            int srcH = sourceBitmap.Height;

            // Dynamic sampling stride: aim for ~120,000 - 180,000 pixels for real-time responsiveness
            int totalPixelsInRegion = srcW * srcH;
            int step = 1;
            if (totalPixelsInRegion > 250000) step = 2;
            if (totalPixelsInRegion > 1000000) step = 3;

            bool hasAlpha = (sourceBitmap.PixelFormat == PixelFormat.Format32bppArgb || sourceBitmap.PixelFormat == PixelFormat.Format32bppPArgb);

            // Lock source bitmap for direct pointer access
            BitmapData srcData = sourceBitmap.LockBits(
                new Rectangle(0, 0, srcW, srcH),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            int pixelCount = 0;
            try
            {
                unsafe
                {
                    byte* pBase = (byte*)srcData.Scan0.ToPointer();
                    int stride = srcData.Stride;

                    for (int y = 0; y < srcH; y += step)
                    {
                        byte* row = pBase + (y * stride);
                        for (int x = 0; x < srcW; x += step)
                        {
                            int pxOffset = x * 4;
                            byte bByte = row[pxOffset];
                            byte gByte = row[pxOffset + 1];
                            byte rByte = row[pxOffset + 2];
                            byte aByte = row[pxOffset + 3];

                            if (hasAlpha && aByte < 15) continue;

                            // Fast LUT lookup for Cb and Cr
                            double cb = Cb_R[rByte] + Cb_G[gByte] + Cb_B[bByte];
                            double cr = Cr_R[rByte] + Cr_G[gByte] + Cr_B[bByte];

                            double sx = cx + (cb * gainScale);
                            double sy = cy - (cr * gainScale); // Invert Cr so Red is up-left

                            int x0 = (int)sx;
                            int y0 = (int)sy;

                            if (x0 >= 0 && x0 < size - 1 && y0 >= 0 && y0 < size - 1)
                            {
                                float fx = (float)(sx - x0);
                                float fy = (float)(sy - y0);

                                float w00 = (1.0f - fx) * (1.0f - fy);
                                float w10 = fx * (1.0f - fy);
                                float w01 = (1.0f - fx) * fy;
                                float w11 = fx * fy;

                                float rNorm = ByteToNorm[rByte];
                                float gNorm = ByteToNorm[gByte];
                                float bNorm = ByteToNorm[bByte];

                                int idx00 = (y0 * size + x0) * 4;
                                accumBuffer[idx00]     += rNorm * w00;
                                accumBuffer[idx00 + 1] += gNorm * w00;
                                accumBuffer[idx00 + 2] += bNorm * w00;
                                accumBuffer[idx00 + 3] += w00;

                                int idx10 = (y0 * size + (x0 + 1)) * 4;
                                accumBuffer[idx10]     += rNorm * w10;
                                accumBuffer[idx10 + 1] += gNorm * w10;
                                accumBuffer[idx10 + 2] += bNorm * w10;
                                accumBuffer[idx10 + 3] += w10;

                                int idx01 = ((y0 + 1) * size + x0) * 4;
                                accumBuffer[idx01]     += rNorm * w01;
                                accumBuffer[idx01 + 1] += gNorm * w01;
                                accumBuffer[idx01 + 2] += bNorm * w01;
                                accumBuffer[idx01 + 3] += w01;

                                int idx11 = ((y0 + 1) * size + (x0 + 1)) * 4;
                                accumBuffer[idx11]     += rNorm * w11;
                                accumBuffer[idx11 + 1] += gNorm * w11;
                                accumBuffer[idx11 + 2] += bNorm * w11;
                                accumBuffer[idx11 + 3] += w11;

                                pixelCount++;
                            }
                        }
                    }
                }
            }
            finally
            {
                sourceBitmap.UnlockBits(srcData);
            }

            // Create target bitmap
            Bitmap scopeBitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            BitmapData dstData = scopeBitmap.LockBits(
                new Rectangle(0, 0, size, size),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            // Studio dark background: #121418
            byte bgB = 24, bgG = 20, bgR = 18;

            float avgDensity = pixelCount > 0 ? (float)pixelCount / (size * size * 0.1f) : 1.0f;
            float densityFactor = (0.35f / Math.Max(0.1f, avgDensity)) * settings.Intensity;

            try
            {
                unsafe
                {
                    byte* pDstBase = (byte*)dstData.Scan0.ToPointer();
                    int dstStride = dstData.Stride;

                    for (int y = 0; y < size; y++)
                    {
                        byte* dstRow = pDstBase + (y * dstStride);
                        int rowOffset = y * size * 4;

                        for (int x = 0; x < size; x++)
                        {
                            int pxOffset = x * 4;
                            int bufIdx = rowOffset + pxOffset;
                            float w = accumBuffer[bufIdx + 3];

                            if (w > 0.0001f)
                            {
                                float rNorm = accumBuffer[bufIdx] / w;
                                float gNorm = accumBuffer[bufIdx + 1] / w;
                                float bNorm = accumBuffer[bufIdx + 2] / w;

                                float alpha = 1.0f - (float)Math.Exp(-w * densityFactor);
                                alpha = Math.Min(1.0f, Math.Max(0.0f, alpha));

                                float boost = 1.15f;
                                float rTrace = Math.Min(1.0f, rNorm * boost) * 255.0f;
                                float gTrace = Math.Min(1.0f, gNorm * boost) * 255.0f;
                                float bTrace = Math.Min(1.0f, bNorm * boost) * 255.0f;

                                byte finalR = (byte)Math.Min(255, (int)(bgR * (1.0f - alpha) + rTrace * alpha));
                                byte finalG = (byte)Math.Min(255, (int)(bgG * (1.0f - alpha) + gTrace * alpha));
                                byte finalB = (byte)Math.Min(255, (int)(bgB * (1.0f - alpha) + bTrace * alpha));

                                dstRow[pxOffset]     = finalB;
                                dstRow[pxOffset + 1] = finalG;
                                dstRow[pxOffset + 2] = finalR;
                                dstRow[pxOffset + 3] = 255;
                            }
                            else
                            {
                                dstRow[pxOffset]     = bgB;
                                dstRow[pxOffset + 1] = bgG;
                                dstRow[pxOffset + 2] = bgR;
                                dstRow[pxOffset + 3] = 255;
                            }
                        }
                    }
                }
            }
            finally
            {
                scopeBitmap.UnlockBits(dstData);
            }

            // Draw Graticule over the color trace
            using (Graphics g = Graphics.FromImage(scopeBitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawGraticule(g, size, scale, settings);
            }

            return scopeBitmap;
        }

        private static void DrawGraticule(Graphics g, int size, double scale, ScopeSettings settings)
        {
            float cx = size / 2.0f;
            float cy = size / 2.0f;

            // Graticule styling
            Color gridColor = Color.FromArgb(70, 85, 100);
            Color gridSubtle = Color.FromArgb(40, 50, 60);
            Color axisColor = Color.FromArgb(80, 100, 120);
            Color skinColor = Color.FromArgb(235, 150, 75); // Flesh tone warm amber
            Color targetColor = Color.FromArgb(180, 205, 230);

            using (Pen ringPen = new Pen(gridSubtle, 1.0f))
            using (Pen outerRingPen = new Pen(gridColor, 1.5f))
            using (Pen axisPen = new Pen(axisColor, 1.0f))
            using (Pen skinPen = new Pen(skinColor, 1.25f))
            using (Pen targetPen = new Pen(targetColor, 1.0f))
            using (Font labelFont = new Font("Segoe UI", 9.0f, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(targetColor))
            using (SolidBrush skinTextBrush = new SolidBrush(skinColor))
            {
                skinPen.DashStyle = DashStyle.Dash;

                // 1. Concentric Saturation Rings
                if (settings.ShowRings)
                {
                    double maxR = 0.596; // 100% saturation radius
                    float[] percentages = new float[] { 0.20f, 0.40f, 0.60f, 0.75f, 1.00f };

                    foreach (float pct in percentages)
                    {
                        float rPx = (float)(pct * maxR * scale);
                        Pen p = (pct == 1.00f || pct == 0.75f) ? outerRingPen : ringPen;
                        g.DrawEllipse(p, cx - rPx, cy - rPx, rPx * 2, rPx * 2);
                    }
                }

                // 2. Axes Crosshairs
                if (settings.ShowAxes)
                {
                    float axisRadius = (float)(0.596 * scale);
                    // Cb axis (Horizontal)
                    g.DrawLine(axisPen, cx - axisRadius, cy, cx + axisRadius, cy);
                    // Cr axis (Vertical)
                    g.DrawLine(axisPen, cx, cy - axisRadius, cx, cy + axisRadius);

                    // Minor tick marks along axes
                    int ticks = 5;
                    for (int i = -ticks; i <= ticks; i++)
                    {
                        if (i == 0) continue;
                        float tickPos = (float)(i * (0.596 / ticks) * scale);
                        // Cb ticks
                        g.DrawLine(ringPen, cx + tickPos, cy - 3, cx + tickPos, cy + 3);
                        // Cr ticks
                        g.DrawLine(ringPen, cx - 3, cy + tickPos, cx + 3, cy + tickPos);
                    }
                }

                // 3. Skin Tone Line (I-Line at 132 degrees)
                if (settings.ShowSkinLine)
                {
                    double angleRad = SkinToneAngleDegrees * (Math.PI / 180.0);
                    // In polar Cb-Cr coordinates: Cb = cos(angle), Cr = sin(angle)
                    // Screen: sx = cx + Cb * scale, sy = cy - Cr * scale
                    float lineLength = (float)(0.596 * scale);
                    float sx = cx + (float)(Math.Cos(angleRad) * lineLength);
                    float sy = cy - (float)(Math.Sin(angleRad) * lineLength);

                    g.DrawLine(skinPen, cx, cy, sx, sy);

                    // Skin tone target label
                    float labelX = cx + (float)(Math.Cos(angleRad) * (lineLength * 1.05f));
                    float labelY = cy - (float)(Math.Sin(angleRad) * (lineLength * 1.05f));
                    g.DrawString("SKIN", labelFont, skinTextBrush, labelX - 18, labelY - 14);
                }

                // 4. Color Targets (75% and 100%)
                if (settings.ShowTargets)
                {
                    // Draw 75% target boxes
                    foreach (var target in Targets75)
                    {
                        float tx = cx + (float)(target.Cb * scale);
                        float ty = cy - (float)(target.Cr * scale);

                        // Standard tolerance box (outer box and inner cross)
                        float boxSize = 10.0f;
                        g.DrawRectangle(targetPen, tx - boxSize / 2, ty - boxSize / 2, boxSize, boxSize);
                        g.DrawLine(targetPen, tx - 2, ty, tx + 2, ty);
                        g.DrawLine(targetPen, tx, ty - 2, tx, ty + 2);

                        // Label
                        SizeF textSize = g.MeasureString(target.Name, labelFont);
                        float textX = tx + (target.Cb >= 0 ? 8 : -textSize.Width - 8);
                        float textY = ty + (target.Cr >= 0 ? -textSize.Height : 2);
                        g.DrawString(target.Name, labelFont, textBrush, textX, textY);
                    }

                    // Draw 100% target markers (smaller square)
                    using (Pen marker100Pen = new Pen(gridSubtle, 1.0f))
                    {
                        foreach (var target in Targets100)
                        {
                            float tx = cx + (float)(target.Cb * scale);
                            float ty = cy - (float)(target.Cr * scale);
                            float boxSize = 6.0f;
                            g.DrawRectangle(marker100Pen, tx - boxSize / 2, ty - boxSize / 2, boxSize, boxSize);
                        }
                    }
                }

                // 5. Target Color Points (Hue & Saturation)
                if (settings.Targets != null && settings.Targets.Count > 0)
                {
                    for (int idx = 0; idx < settings.Targets.Count; idx++)
                    {
                        TargetColorPoint pt = settings.Targets[idx];
                        double hue = pt.Hue;
                        double sat = pt.Sat;
                        double rad = hue * (Math.PI / 180.0);
                        double cbNorm = Math.Cos(rad);
                        double crNorm = Math.Sin(rad);

                        // Compute a representative vibrant color for this hue in Rec.709
                        double satFrac = Math.Max(0.15, sat / 100.0);
                        double testCb = cbNorm * 0.38 * satFrac;
                        double testCr = crNorm * 0.38 * satFrac;
                        double y = 0.65;
                        double rD = y + 1.5748 * testCr;
                        double bD = y + 1.8556 * testCb;
                        double gD = (y - 0.2126 * rD - 0.0722 * bD) / 0.7152;
                        byte rByte = (byte)Math.Max(50, Math.Min(255, (int)(rD * 255.0)));
                        byte gByte = (byte)Math.Max(50, Math.Min(255, (int)(gD * 255.0)));
                        byte bByte = (byte)Math.Max(50, Math.Min(255, (int)(bD * 255.0)));
                        Color hueColor = Color.FromArgb(245, rByte, gByte, bByte);

                        float maxRingR = (float)(0.596 * scale);
                        float ptDist = (float)((sat / 100.0) * maxRingR);
                        float px = cx + (float)(cbNorm * ptDist);
                        float py = cy - (float)(crNorm * ptDist);

                        float lineStart = (float)(0.04 * maxRingR);
                        float x1 = cx + (float)(cbNorm * lineStart);
                        float y1 = cy - (float)(crNorm * lineStart);

                        using (Pen huePen = new Pen(Color.FromArgb(140, hueColor), 1.25f))
                        using (Pen ptRingPen = new Pen(Color.FromArgb(235, 255, 255, 255), 1.75f))
                        using (Pen darkHalo = new Pen(Color.FromArgb(180, 0, 0, 0), 3.2f))
                        using (SolidBrush hueBrush = new SolidBrush(hueColor))
                        using (Font tagFont = new Font("Segoe UI", 8.0f, FontStyle.Bold))
                        {
                            huePen.DashStyle = DashStyle.Dash;
                            if (ptDist > lineStart)
                            {
                                g.DrawLine(huePen, x1, y1, px, py);
                            }

                            // Target point marker at (Hue, Sat)
                            float dotR = 5.5f;
                            g.DrawEllipse(darkHalo, px - dotR, py - dotR, dotR * 2, dotR * 2);
                            g.FillEllipse(hueBrush, px - dotR, py - dotR, dotR * 2, dotR * 2);
                            g.DrawEllipse(ptRingPen, px - dotR, py - dotR, dotR * 2, dotR * 2);

                            // Target label tag: "T1"
                            string tag = string.Format("T{0}", idx + 1);
                            SizeF tagSize = g.MeasureString(tag, tagFont);
                            float labelOffset = dotR + 9.0f;
                            float tagX = px + (float)(cbNorm * labelOffset);
                            float tagY = py - (float)(crNorm * labelOffset);
                            g.DrawString(tag, tagFont, hueBrush, tagX - tagSize.Width / 2, tagY - tagSize.Height / 2);
                        }
                    }
                }

                // Center Origin Crosshair
                g.DrawLine(targetPen, cx - 4, cy, cx + 4, cy);
                g.DrawLine(targetPen, cx, cy - 4, cx, cy + 4);
            }
        }

        public static ScopeSettings ParseArgs(string[] args)
        {
            var settings = new ScopeSettings();

            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i].ToLowerInvariant();
                string val = (i + 1 < args.Length) ? args[i + 1] : "";

                switch (key)
                {
                    case "--input":
                    case "-i":
                        settings.InputPath = val;
                        i++;
                        break;
                    case "--output":
                    case "-o":
                        settings.OutputPath = val;
                        i++;
                        break;
                    case "--size":
                    case "-s":
                        int s;
                        if (int.TryParse(val, out s)) settings.Size = Math.Max(128, Math.Min(2048, s));
                        i++;
                        break;
                    case "--gain":
                    case "-g":
                        float g;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out g))
                            settings.Gain = Math.Max(0.1f, Math.Min(10.0f, g));
                        i++;
                        break;
                    case "--intensity":
                        float intens;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out intens))
                            settings.Intensity = Math.Max(0.1f, Math.Min(10.0f, intens));
                        i++;
                        break;
                    case "--skinline":
                        bool skin;
                        if (bool.TryParse(val, out skin)) settings.ShowSkinLine = skin;
                        i++;
                        break;
                    case "--targets":
                        if (val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase))
                        {
                            bool tg;
                            if (bool.TryParse(val, out tg)) settings.ShowTargets = tg;
                        }
                        else if (!string.IsNullOrEmpty(val))
                        {
                            settings.Targets.Clear();
                            string[] items = val.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string item in items)
                            {
                                string[] hs = item.Split(':');
                                double hue;
                                if (double.TryParse(hs[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hue))
                                {
                                    double sat = 50.0;
                                    if (hs.Length > 1)
                                    {
                                        double parsedSat;
                                        if (double.TryParse(hs[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsedSat))
                                            sat = parsedSat;
                                    }
                                    settings.Targets.Add(new TargetColorPoint(hue, sat));
                                }
                            }
                        }
                        i++;
                        break;
                    case "--rings":
                        bool r;
                        if (bool.TryParse(val, out r)) settings.ShowRings = r;
                        i++;
                        break;
                    case "--axes":
                        bool a;
                        if (bool.TryParse(val, out a)) settings.ShowAxes = a;
                        i++;
                        break;
                    case "--stats-output":
                        settings.StatsOutputPath = val;
                        i++;
                        break;
                    case "--strength":
                        float str;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out str))
                            settings.Strength = Math.Max(0.0f, Math.Min(100.0f, str));
                        i++;
                        break;
                    case "--density":
                        float dens;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dens))
                            settings.Density = Math.Max(0.0f, Math.Min(100.0f, dens));
                        i++;
                        break;
                    case "--bandwidth":
                        float bw;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bw))
                            settings.Bandwidth = Math.Max(10.0f, Math.Min(90.0f, bw));
                        i++;
                        break;
                    case "--pin-bw":
                        float pbw;
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pbw))
                            settings.PinBlackWhite = Math.Max(0.0f, Math.Min(100.0f, pbw));
                        i++;
                        break;
                    case "--band-sat":
                        if (!string.IsNullOrEmpty(val))
                        {
                            string[] bsParts = val.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (bsParts.Length == 8)
                            {
                                float[] parsed = new float[8];
                                bool okAll = true;
                                for (int k = 0; k < 8; k++)
                                {
                                    if (!float.TryParse(bsParts[k].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed[k]))
                                    {
                                        okAll = false;
                                        break;
                                    }
                                }
                                if (okAll) settings.BandSat = parsed;
                            }
                        }
                        i++;
                        break;
                }
            }

            return settings;
        }

        /// <summary>
        /// Measures Gaussian-weighted average chroma saturation (0-100%) per Lightroom HSL band,
        /// skipping near-neutral pixels (chromaSq &lt; 0.000004).
        /// </summary>
        public static float[] MeasureBandSaturations(Bitmap src)
        {
            double[] weightSum = new double[8];
            double[] satSum = new double[8];
            int w = src.Width;
            int h = src.Height;

            BitmapData srcData = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* pBase = (byte*)srcData.Scan0.ToPointer();
                    int stride = srcData.Stride;
                    int step = Math.Max(1, Math.Min(w, h) / 256);

                    for (int y = 0; y < h; y += step)
                    {
                        byte* row = pBase + y * stride;
                        for (int x = 0; x < w; x += step)
                        {
                            int px = x * 4;
                            byte b = row[px];
                            byte g = row[px + 1];
                            byte r = row[px + 2];

                            double cb = Cb_R[r] + Cb_G[g] + Cb_B[b];
                            double cr = Cr_R[r] + Cr_G[g] + Cr_B[b];
                            double chromaSq = cb * cb + cr * cr;
                            if (chromaSq < 0.000004) continue;

                            double satPct = Math.Min(100.0, (Math.Sqrt(chromaSq) / 0.596) * 100.0);
                            double angle = Math.Atan2(cr, cb) * (180.0 / Math.PI);
                            if (angle < 0) angle += 360.0;

                            for (int i = 0; i < 8; i++)
                            {
                                double diff = Math.Abs(angle - ColorSeparationSimulator.Channels[i].Angle) % 360.0;
                                if (diff > 180.0) diff = 360.0 - diff;
                                double wt = Math.Exp(-(diff * diff) / (2.0 * 25.0 * 25.0));
                                weightSum[i] += wt;
                                satSum[i] += wt * satPct;
                            }
                        }
                    }
                }
            }
            finally
            {
                src.UnlockBits(srcData);
            }

            float[] bandSat = new float[8];
            for (int i = 0; i < 8; i++)
            {
                bandSat[i] = (weightSum[i] > 1e-4) ? (float)(satSum[i] / weightSum[i]) : 25.0f;
            }
            return bandSat;
        }

        public static void WriteStats(float[] bandSat, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using (StreamWriter sw = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            {
                for (int i = 0; i < 8; i++)
                {
                    sw.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "band_sat_{0}={1:F2}", i + 1, bandSat[i]));
                }
            }
        }

        public static int Main(string[] args)
        {
            try
            {
                var settings = ParseArgs(args);

                if (string.IsNullOrEmpty(settings.InputPath) || string.IsNullOrEmpty(settings.OutputPath))
                {
                    Console.WriteLine("Usage: vectorscope_engine.exe --input <photo.jpg> --output <scope.png> [options]");
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --size <int>             Output square dimension (default 512)");
                    Console.WriteLine("  --targets <csv>          Target Hue:Sat pairs (e.g. 132:50,312:50)");
                    Console.WriteLine("  --strength <float>       Overall color separation strength (0 to 100)");
                    Console.WriteLine("  --density <float>        Luminance color density impact (0 to 100)");
                    Console.WriteLine("  --bandwidth <float>      Hue tolerance in degrees (15 to 60)");
                    Console.WriteLine("  --stats-output <path>    Output measured 8-band saturations");
                    return 1;
                }

                if (!File.Exists(settings.InputPath))
                {
                    Console.Error.WriteLine("Error: Input file does not exist: " + settings.InputPath);
                    return 2;
                }

                using (Bitmap src = new Bitmap(settings.InputPath))
                {
                    if (settings.BandSat == null)
                    {
                        settings.BandSat = MeasureBandSaturations(src);
                    }
                    if (!string.IsNullOrEmpty(settings.StatsOutputPath))
                    {
                        WriteStats(settings.BandSat, settings.StatsOutputPath);
                    }

                    Bitmap workingBitmap = src;
                    bool ownsWorkingBitmap = false;

                    bool shouldSimulate = settings.Strength > 0.001f &&
                                          settings.Targets != null && settings.Targets.Count > 0;

                    if (shouldSimulate)
                    {
                        workingBitmap = ColorSeparationSimulator.Apply(src, settings);
                        ownsWorkingBitmap = true;
                    }

                    try
                    {
                        using (Bitmap scope = RenderVectorscope(workingBitmap, settings))
                        {
                            string dir = Path.GetDirectoryName(settings.OutputPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            scope.Save(settings.OutputPath, ImageFormat.Png);
                        }
                    }
                    finally
                    {
                        if (ownsWorkingBitmap)
                        {
                            workingBitmap.Dispose();
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Vectorscope Engine Exception: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 3;
            }
        }
    }

    public class ColorSeparationSimulator
    {
        public struct HslChannelDef
        {
            public string Name;
            public double Angle;
            public double Rho;
            public HslChannelDef(string name, double angle, double rho)
            {
                Name = name;
                Angle = angle;
                Rho = rho;
            }
        }

        public static readonly HslChannelDef[] Channels = new HslChannelDef[]
        {
            new HslChannelDef("Red",     103, 0.48),
            new HslChannelDef("Orange",  138, 0.38),
            new HslChannelDef("Yellow",  175, 0.30),
            new HslChannelDef("Green",   220, 0.55),
            new HslChannelDef("Aqua",    283, 0.52),
            new HslChannelDef("Blue",    355, 0.45),
            new HslChannelDef("Purple",  25,  0.42),
            new HslChannelDef("Magenta", 50,  0.40),
        };

        public static readonly HslChannelDef[] PrimaryPoles = new HslChannelDef[]
        {
            new HslChannelDef("Red",   103.0, 0.0),
            new HslChannelDef("Green", 220.0, 0.0),
            new HslChannelDef("Blue",  355.0, 0.0),
        };

        public static double AngleDistance(double a, double b)
        {
            double diff = Math.Abs(a - b) % 360.0;
            if (diff > 180.0) diff = 360.0 - diff;
            return diff;
        }

        private static int ClampByte(double val)
        {
            int rounded = (int)Math.Floor(val + 0.5);
            if (rounded > 255) return 255;
            if (rounded < 0) return 0;
            return rounded;
        }

        public static double ComputeHueAttractorShift(double angleDeg, List<double> targetHues, float strengthNorm)
        {
            if (targetHues == null || targetHues.Count == 0 || Math.Abs(strengthNorm) < 1e-6)
                return 0.0;

            List<double> sorted = new List<double>();
            foreach (double th in targetHues)
            {
                double norm = ((th % 360.0) + 360.0) % 360.0;
                bool dup = false;
                foreach (double s in sorted)
                {
                    if (Math.Abs(s - norm) < 0.5) { dup = true; break; }
                }
                if (!dup) sorted.Add(norm);
            }
            sorted.Sort();

            double a = ((angleDeg % 360.0) + 360.0) % 360.0;
            int nearestIdx = 0;
            double bestAbs = 999.0;
            double signedDelta = 0.0;

            for (int i = 0; i < sorted.Count; i++)
            {
                double d = sorted[i] - a;
                while (d > 180.0) d -= 360.0;
                while (d <= -180.0) d += 360.0;
                if (Math.Abs(d) < bestAbs)
                {
                    bestAbs = Math.Abs(d);
                    signedDelta = d;
                    nearestIdx = i;
                }
            }

            if (bestAbs < 1e-4) return 0.0;

            double halfGap = 180.0;
            if (sorted.Count >= 2)
            {
                int adjIdx = (signedDelta > 0)
                    ? (nearestIdx - 1 + sorted.Count) % sorted.Count
                    : (nearestIdx + 1) % sorted.Count;
                double span = Math.Abs(sorted[nearestIdx] - sorted[adjIdx]);
                if (span > 180.0) span = 360.0 - span;
                // Handle wrap-around order when count == 2
                if (sorted.Count == 2) span = Math.Min(span, 360.0 - span);
                halfGap = Math.Max(15.0, span * 0.5);
            }

            double u = Math.Max(-1.0, Math.Min(1.0, signedDelta / halfGap));
            return strengthNorm * Math.Sin(Math.PI * u);
        }

        /// <summary>
        /// Translates sine-attractor hue shifts and target chroma pull into 5-knot (0, 64, 128, 192, 255)
        /// RGB point curves, scaling black/white endpoint offsets by (1 - pinBlackWhite / 100).
        /// </summary>
        public static void CalculateToneCurves(
            List<TargetColorPoint> targets,
            float strength,
            float bandwidth,
            float pinBlackWhite,
            out int[] redCurve,
            out int[] greenCurve,
            out int[] blueCurve)
        {
            if (targets == null || targets.Count == 0 || strength <= 0.0001f)
            {
                redCurve   = new int[] { 0, 0, 64, 64, 128, 128, 192, 192, 255, 255 };
                greenCurve = new int[] { 0, 0, 64, 64, 128, 128, 192, 192, 255, 255 };
                blueCurve  = new int[] { 0, 0, 64, 64, 128, 128, 192, 192, 255, 255 };
                return;
            }

            double sNorm = Math.Max(0.0, Math.Min(1.0, strength / 100.0));
            double sigma = bandwidth > 0 ? bandwidth : 40.0;
            double twoSigmaSq = 2.0 * sigma * sigma;
            double unpin = 1.0 - Math.Max(0.0, Math.Min(1.0, pinBlackWhite / 100.0));

            List<double> targetHues = new List<double>(targets.Count);
            foreach (var t in targets) targetHues.Add(t.Hue);

            double[,] bandDelta = new double[8, 3];
            for (int i = 0; i < 8; i++)
            {
                double chAngle = Channels[i].Angle;
                double rad0 = chAngle * (Math.PI / 180.0);
                double shift = ComputeHueAttractorShift(chAngle, targetHues, (float)sNorm);
                double rad1 = (chAngle + shift * 30.0) * (Math.PI / 180.0);

                double maxAff = 0.0;
                double sumAff = 0.0;
                double sumCb = 0.0;
                double sumCr = 0.0;
                foreach (var pt in targets)
                {
                    double dist = AngleDistance(chAngle, pt.Hue);
                    double aff = Math.Exp(-(dist * dist) / twoSigmaSq);
                    if (aff > maxAff) maxAff = aff;
                    double ptRad = pt.Hue * (Math.PI / 180.0);
                    double ptSat = pt.Sat / 100.0;
                    sumCb += aff * (ptSat * Math.Cos(ptRad));
                    sumCr += aff * (ptSat * Math.Sin(ptRad));
                    sumAff += aff;
                }

                double tCb = (sumAff > 1e-6) ? (sumCb / sumAff) : Math.Cos(rad0);
                double tCr = (sumAff > 1e-6) ? (sumCr / sumAff) : Math.Sin(rad0);

                double dCb = (Math.Cos(rad1) - Math.Cos(rad0)) + 0.35 * sNorm * maxAff * (tCb - Math.Cos(rad0));
                double dCr = (Math.Sin(rad1) - Math.Sin(rad0)) + 0.35 * sNorm * maxAff * (tCr - Math.Sin(rad0));

                bandDelta[i, 0] = 1.5748 * dCr;
                bandDelta[i, 1] = -0.4681 * dCr - 0.1873 * dCb;
                bandDelta[i, 2] = 1.8556 * dCb;
            }

            const double K_CURVE = 28.0;
            double[] delta64 = new double[3];
            double[] delta128 = new double[3];
            double[] delta192 = new double[3];

            for (int p = 0; p < 3; p++)
            {
                double pAngle = PrimaryPoles[p].Angle;
                double compAngle = (pAngle + 180.0) % 360.0;
                double sumWHi = 0.0, sumHi = 0.0;
                double sumWLo = 0.0, sumLo = 0.0;

                for (int i = 0; i < 8; i++)
                {
                    double chAngle = Channels[i].Angle;
                    double diffHi = AngleDistance(chAngle, pAngle);
                    if (diffHi < 90.0)
                    {
                        double c = Math.Cos(diffHi * Math.PI / 180.0);
                        double w = c * c;
                        sumWHi += w;
                        sumHi += w * bandDelta[i, p];
                    }

                    double diffLo = AngleDistance(chAngle, compAngle);
                    if (diffLo < 90.0)
                    {
                        double c = Math.Cos(diffLo * Math.PI / 180.0);
                        double w = c * c;
                        sumWLo += w;
                        sumLo += w * bandDelta[i, p];
                    }
                }

                delta192[p] = (sumWHi > 1e-6) ? (K_CURVE * (sumHi / sumWHi)) : 0.0;
                delta64[p]  = (sumWLo > 1e-6) ? (K_CURVE * (sumLo / sumWLo)) : 0.0;
                delta128[p] = 0.5 * (delta64[p] + delta192[p]);
            }

            double minDelta64 = Math.Min(0.0, Math.Min(delta64[0], Math.Min(delta64[1], delta64[2])));
            double maxDelta192 = Math.Max(0.0, Math.Max(delta192[0], Math.Max(delta192[1], delta192[2])));

            int[][] curves = new int[3][];
            for (int p = 0; p < 3; p++)
            {
                int y0   = ClampByte(0.0 + unpin * (delta64[p] - minDelta64));
                int y64  = ClampByte(64.0 + delta64[p]);
                int y128 = ClampByte(128.0 + delta128[p]);
                int y192 = ClampByte(192.0 + delta192[p]);
                int y255 = ClampByte(255.0 + unpin * (delta192[p] - maxDelta192));
                curves[p] = new int[] { 0, y0, 64, y64, 128, y128, 192, y192, 255, y255 };
            }

            redCurve   = curves[0];
            greenCurve = curves[1];
            blueCurve  = curves[2];
        }

        /// <summary>
        /// Piecewise-linearly interpolates a 5-knot Lightroom tone curve into a 256-entry byte LUT.
        /// </summary>
        public static byte[] BuildCurveByteLut(int[] curvePoints)
        {
            byte[] lut = new byte[256];
            if (curvePoints == null || curvePoints.Length < 10)
            {
                for (int v = 0; v < 256; v++) lut[v] = (byte)v;
                return lut;
            }

            for (int v = 0; v < 256; v++)
            {
                double y = v;
                for (int k = 0; k < curvePoints.Length - 2; k += 2)
                {
                    int x0 = curvePoints[k];
                    int y0 = curvePoints[k + 1];
                    int x1 = curvePoints[k + 2];
                    int y1 = curvePoints[k + 3];
                    if (v >= x0 && v <= x1)
                    {
                        double t = (x1 > x0) ? ((double)(v - x0) / (x1 - x0)) : 0.0;
                        y = y0 + t * (y1 - y0);
                        break;
                    }
                }
                lut[v] = (byte)ClampByte(y);
            }
            return lut;
        }

        public static void CalculateAdjustments(
            List<TargetColorPoint> targets,
            float[] bandSat,
            float strength,
            float density,
            float bandwidth,
            out float[] satAdjustments,
            out float[] lumAdjustments)
        {
            satAdjustments = new float[8];
            lumAdjustments = new float[8];

            if (targets == null || targets.Count == 0 || strength <= 0.0001f)
                return;

            double sigma = bandwidth > 0 ? bandwidth : 40.0;
            double twoSigmaSq = 2.0 * sigma * sigma;
            double sNorm = Math.Max(0.0, Math.Min(1.0, strength / 100.0));
            double dNorm = Math.Max(0.0, Math.Min(1.0, density / 100.0));

            for (int i = 0; i < 8; i++)
            {
                double chAngle = Channels[i].Angle;
                double maxAffinity = 0.0;
                double weightedTargetSat = 0.0;
                double affinitySum = 0.0;

                foreach (var pt in targets)
                {
                    double diff = AngleDistance(chAngle, pt.Hue);
                    double affinity = Math.Exp(-(diff * diff) / twoSigmaSq);
                    if (affinity > maxAffinity) maxAffinity = affinity;
                    weightedTargetSat += affinity * pt.Sat;
                    affinitySum += affinity;
                }

                double targetSat = (affinitySum > 1e-6) ? (weightedTargetSat / affinitySum) : 50.0;
                double measuredSat = (bandSat != null && bandSat.Length == 8) ? bandSat[i] : 25.0;
                double gap = targetSat - measuredSat;
                double penalty = 1.0 - maxAffinity;

                double deltaSat = sNorm * ((gap * maxAffinity) - (65.0 * penalty));
                float satVal = (float)Math.Round(deltaSat);
                satVal = Math.Max(-100.0f, Math.Min(100.0f, satVal));

                float lumVal = 0.0f;
                if (dNorm > 0.0001)
                {
                    float softKnee = 1.0f - (Math.Abs(satVal) / 250.0f);
                    float deltaLum = (float)(-dNorm * Channels[i].Rho * satVal * softKnee);
                    lumVal = (float)Math.Round(deltaLum);
                    lumVal = Math.Max(-100.0f, Math.Min(100.0f, lumVal));
                }

                satAdjustments[i] = satVal;
                lumAdjustments[i] = lumVal;
            }
        }

        public static void BuildLut(
            List<TargetColorPoint> targets,
            float[] bandSat,
            float strength,
            float density,
            float bandwidth,
            out float[] deltaSatTable,
            out float[] deltaLumTable)
        {
            deltaSatTable = new float[360];
            deltaLumTable = new float[360];

            if (targets == null || targets.Count == 0 || strength <= 0.0001f)
                return;

            float[] satAdj, lumAdj;
            CalculateAdjustments(targets, bandSat, strength, density, bandwidth,
                                 out satAdj, out lumAdj);

            for (int deg = 0; deg < 360; deg++)
            {
                double totalWeight = 0.0;
                double sumSat = 0.0;
                double sumLum = 0.0;

                for (int i = 0; i < 8; i++)
                {
                    double diff = AngleDistance(deg, Channels[i].Angle);
                    double w = Math.Exp(-(diff * diff) / (2.0 * 25.0 * 25.0));
                    totalWeight += w;
                    sumSat += w * satAdj[i];
                    sumLum += w * lumAdj[i];
                }

                if (totalWeight > 0.0001)
                {
                    deltaSatTable[deg] = (float)((sumSat / totalWeight) / 100.0);
                    deltaLumTable[deg] = (density > 0.0001f) ? (float)((sumLum / totalWeight) / 100.0) : 0.0f;
                }
            }
        }

        public static Bitmap Apply(Bitmap src, ScopeSettings settings)
        {
            int w = src.Width;
            int h = src.Height;

            float[] deltaSatTable, deltaLumTable;
            BuildLut(settings.Targets, settings.BandSat, settings.Strength, settings.Density, settings.Bandwidth,
                     out deltaSatTable, out deltaLumTable);

            int[] redCurve, greenCurve, blueCurve;
            CalculateToneCurves(settings.Targets, settings.Strength, settings.Bandwidth, settings.PinBlackWhite,
                                out redCurve, out greenCurve, out blueCurve);
            byte[] rLut = BuildCurveByteLut(redCurve);
            byte[] gLut = BuildCurveByteLut(greenCurve);
            byte[] bLut = BuildCurveByteLut(blueCurve);

            bool applyLum = settings.Density > 0.0001f;

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            BitmapData srcData = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dstData = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* pSrcBase = (byte*)srcData.Scan0.ToPointer();
                    byte* pDstBase = (byte*)dstData.Scan0.ToPointer();
                    int srcStride = srcData.Stride;
                    int dstStride = dstData.Stride;

                    System.Threading.Tasks.Parallel.For(0, h, y =>
                    {
                        byte* srcRow = pSrcBase + (y * srcStride);
                        byte* dstRow = pDstBase + (y * dstStride);

                        for (int x = 0; x < w; x++)
                        {
                            int px = x * 4;
                            byte bByte = srcRow[px];
                            byte gByte = srcRow[px + 1];
                            byte rByte = srcRow[px + 2];
                            byte aByte = srcRow[px + 3];

                            double rNorm = rByte / 255.0;
                            double gNorm = gByte / 255.0;
                            double bNorm = bByte / 255.0;

                            double cb = -0.1146 * rNorm - 0.3854 * gNorm + 0.5000 * bNorm;
                            double cr =  0.5000 * rNorm - 0.4542 * gNorm - 0.0458 * bNorm;
                            double yLum = 0.2126 * rNorm + 0.7152 * gNorm + 0.0722 * bNorm;

                            double chromaSq = cb * cb + cr * cr;
                            if (chromaSq < 0.000004) // neutral gray/white/black
                            {
                                dstRow[px]     = bByte;
                                dstRow[px + 1] = gByte;
                                dstRow[px + 2] = rByte;
                                dstRow[px + 3] = aByte;
                                continue;
                            }

                            double angle = Math.Atan2(cr, cb) * (180.0 / Math.PI);
                            if (angle < 0) angle += 360.0;
                            int degIdx = (int)Math.Round(angle) % 360;

                            float dS = deltaSatTable[degIdx];
                            float dL = applyLum ? deltaLumTable[degIdx] : 0.0f;

                            float satMult = Math.Max(0.0f, 1.0f + dS);
                            double newCb = cb * satMult;
                            double newCr = cr * satMult;

                            // Filmic density modulation: scale luminance shift with chroma saturation
                            double chromaRadius = Math.Sqrt(chromaSq);
                            double chromaFactor = Math.Min(1.0, chromaRadius / 0.18);
                            double newY = applyLum
                                ? Math.Max(0.0, Math.Min(1.0, yLum + dL * 0.35 * chromaFactor))
                                : yLum;

                            double rNew = newY + 1.5748 * newCr;
                            double bNew = newY + 1.8556 * newCb;
                            double gNew = (newY - 0.2126 * rNew - 0.0722 * bNew) / 0.7152;

                            byte outB = (byte)Math.Max(0, Math.Min(255, (int)(bNew * 255.0 + 0.5)));
                            byte outG = (byte)Math.Max(0, Math.Min(255, (int)(gNew * 255.0 + 0.5)));
                            byte outR = (byte)Math.Max(0, Math.Min(255, (int)(rNew * 255.0 + 0.5)));

                            dstRow[px]     = bLut[outB];
                            dstRow[px + 1] = gLut[outG];
                            dstRow[px + 2] = rLut[outR];
                            dstRow[px + 3] = aByte;
                        }
                    });
                }
            }
            finally
            {
                src.UnlockBits(srcData);
                dst.UnlockBits(dstData);
            }

            return dst;
        }
    }

}
