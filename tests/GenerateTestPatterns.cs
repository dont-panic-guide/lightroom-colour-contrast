using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace DaftVectorscope.Tests
{
    public static class TestPatternGenerator
    {
        public static Bitmap CreateColorBars75(int width = 512, int height = 512)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            // 75% SMPTE color bars: White, Yellow, Cyan, Green, Magenta, Red, Blue
            Color[] bars = new Color[]
            {
                Color.FromArgb(191, 191, 191), // 75% White
                Color.FromArgb(191, 191, 0),   // 75% Yellow
                Color.FromArgb(0, 191, 191),   // 75% Cyan
                Color.FromArgb(0, 191, 0),     // 75% Green
                Color.FromArgb(191, 0, 191),   // 75% Magenta
                Color.FromArgb(191, 0, 0),     // 75% Red
                Color.FromArgb(0, 0, 191),     // 75% Blue
            };

            int barWidth = width / bars.Length;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                for (int i = 0; i < bars.Length; i++)
                {
                    int x = i * barWidth;
                    int w = (i == bars.Length - 1) ? (width - x) : barWidth;
                    using (SolidBrush b = new SolidBrush(bars[i]))
                    {
                        g.FillRectangle(b, x, 0, w, height);
                    }
                }
            }
            return bmp;
        }

        public static Bitmap CreateGrayscaleRamp(int width = 512, int height = 512)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                for (int x = 0; x < width; x++)
                {
                    int val = (int)((x / (float)width) * 255);
                    Color c = Color.FromArgb(val, val, val);
                    using (Pen p = new Pen(c))
                    {
                        g.DrawLine(p, x, 0, x, height);
                    }
                }
            }
            return bmp;
        }

        public static Bitmap CreateSkinTonePatch(int width = 512, int height = 512)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            // Representative skin tones across Fitzpatrick scale
            // All share the standard melanin/hemoglobin chromaticity axis
            Color[] skinTones = new Color[]
            {
                Color.FromArgb(238, 206, 180), // Type I/II Light
                Color.FromArgb(219, 172, 138), // Type III Medium
                Color.FromArgb(180, 126, 92),  // Type IV Tan / Olive
                Color.FromArgb(141, 85, 48),   // Type V Brown
                Color.FromArgb(90, 52, 33),    // Type VI Deep
            };

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(128, 128, 128)); // Neutral gray background
                int patchSize = width / 3;
                int startX = (width - patchSize) / 2;
                int startY = (height - patchSize) / 2;

                // Center patch contains skin tone gradient
                for (int y = 0; y < patchSize; y++)
                {
                    int toneIndex = (y * skinTones.Length) / patchSize;
                    using (Pen p = new Pen(skinTones[toneIndex]))
                    {
                        g.DrawLine(p, startX, startY + y, startX + patchSize, startY + y);
                    }
                }
            }
            return bmp;
        }
    }
}
