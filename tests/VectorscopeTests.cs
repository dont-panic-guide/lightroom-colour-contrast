using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DaftVectorscope.Tests
{
    class Program
    {
        private static int passedTests = 0;
        private static int failedTests = 0;

        static void AssertTrue(string testName, bool condition, string message = "")
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[PASS] ");
                Console.ResetColor();
                Console.WriteLine(testName);
                passedTests++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("[FAIL] ");
                Console.ResetColor();
                Console.WriteLine(testName + ": " + message);
                failedTests++;
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   Daft Lightroom Vectorscope Test Suite");
            Console.WriteLine("==================================================");

            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_output");
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            // Test 1: Mathematical Accuracy of Rec.709 Transforms
            TestRec709Math();

            // Test 2: Skin Tone Line Angle Verification
            TestSkinToneAngle();

            // Test 3: Grayscale Collapse to Neutral Center
            TestGrayscaleCollapse(outDir);

            // Test 4: Color Bars Target Alignment
            TestColorBarsAlignment(outDir);

            // Test 5: Skin Tone Cluster Accuracy
            TestSkinToneClustering(outDir);

            // Test 6: True-Color Mode Trace Preservation
            TestTrueColorPreservation(outDir);

            // Test 7: Gain and Radial Scaling
            TestGainScaling(outDir);

            // Test 8: Performance Benchmark (< 50ms for 512x512)
            TestPerformanceBenchmark();

            // Test 9: Multi-Hue Color Separation & Contrast Shaper Math
            TestColorSeparationMath();

            // Test 10: Target Hue Graticule Lines & CLI Parsing
            TestTargetHuesParsing();
            TestTargetHuesLineRendering(outDir);

            // Test 11: Color Separation Simulation & Real-time Live Preview
            TestColorSeparationSimulationAndPreview(outDir);

            Console.WriteLine("==================================================");
            Console.WriteLine(string.Format("Tests Finished: {0} Passed, {1} Failed", passedTests, failedTests));
            Console.WriteLine("==================================================");

            Environment.Exit(failedTests > 0 ? 1 : 0);
        }

        static void TestRec709Math()
        {
            // Pure Red: (1, 0, 0)
            double cbR, crR;
            VectorscopeCore.RgbToCbCr(1.0, 0.0, 0.0, out cbR, out crR);
            AssertTrue("Rec709 Math: Pure Red Cb is -0.1146", Math.Abs(cbR - (-0.1146)) < 0.0001, "Expected -0.1146, got " + cbR);
            AssertTrue("Rec709 Math: Pure Red Cr is +0.5000", Math.Abs(crR - 0.5000) < 0.0001, "Expected 0.5000, got " + crR);

            // Pure Blue: (0, 0, 1)
            double cbB, crB;
            VectorscopeCore.RgbToCbCr(0.0, 0.0, 1.0, out cbB, out crB);
            AssertTrue("Rec709 Math: Pure Blue Cb is +0.5000", Math.Abs(cbB - 0.5000) < 0.0001, "Expected 0.5000, got " + cbB);
            AssertTrue("Rec709 Math: Pure Blue Cr is -0.0458", Math.Abs(crB - (-0.0458)) < 0.0001, "Expected -0.0458, got " + crB);

            // Pure Green: (0, 1, 0)
            double cbG, crG;
            VectorscopeCore.RgbToCbCr(0.0, 1.0, 0.0, out cbG, out crG);
            AssertTrue("Rec709 Math: Pure Green Cb is -0.3854", Math.Abs(cbG - (-0.3854)) < 0.0001, "Expected -0.3854, got " + cbG);
            AssertTrue("Rec709 Math: Pure Green Cr is -0.4542", Math.Abs(crG - (-0.4542)) < 0.0001, "Expected -0.4542, got " + crG);
        }

        static void TestSkinToneAngle()
        {
            // Standard flesh tone melanin/hemoglobin axis
            // In Rec.709 Cb-Cr polar space, natural skin tone clusters along ~132 degrees
            double angle = VectorscopeCore.SkinToneAngleDegrees;
            AssertTrue("Skin Tone Angle is 132 degrees", Math.Abs(angle - 132.0) < 0.1, "Angle must be 132 deg");

            // Verify a standard light-medium skin tone (e.g. RGB 219, 172, 138)
            double r = 219.0 / 255.0;
            double g = 172.0 / 255.0;
            double b = 138.0 / 255.0;
            double cb, cr;
            VectorscopeCore.RgbToCbCr(r, g, b, out cb, out cr);

            // Polar angle in degrees: atan2(cr, cb) in [0, 360)
            double measuredAngle = Math.Atan2(cr, cb) * (180.0 / Math.PI);
            if (measuredAngle < 0) measuredAngle += 360.0;

            AssertTrue("Natural Skin Tone Angle within 132 +/- 3 deg",
                Math.Abs(measuredAngle - 132.0) < 3.0,
                string.Format("Measured angle: {0:F2} deg", measuredAngle));
        }

        static void TestGrayscaleCollapse(string outDir)
        {
            using (Bitmap ramp = TestPatternGenerator.CreateGrayscaleRamp(512, 512))
            {
                var settings = new ScopeSettings
                {
                    Size = 512,
                    Gain = 1.0f,
                    ShowRings = false,
                    ShowTargets = false,
                    ShowSkinLine = false,
                    ShowAxes = false
                };

                using (Bitmap scope = VectorscopeCore.RenderVectorscope(ramp, settings))
                {
                    string path = Path.Combine(outDir, "test_grayscale_scope.png");
                    scope.Save(path, ImageFormat.Png);

                    // Check that the center pixel (256, 256) has the concentrated trace
                    // and distant pixels (radius > 10px) are pure background
                    int cx = 256, cy = 256;
                    Color center = scope.GetPixel(cx, cy);
                    Color outer = scope.GetPixel(cx + 50, cy + 50);

                    // Center should have trace activity (brightness > background)
                    int centerBright = center.R + center.G + center.B;
                    int outerBright = outer.R + outer.G + outer.B;

                    AssertTrue("Grayscale Collapse: Center contains trace", centerBright > outerBright,
                        string.Format("Center: {0}, Outer: {1}", centerBright, outerBright));
                    AssertTrue("Grayscale Collapse: Outer region remains neutral dark background", outerBright < 80,
                        string.Format("Outer brightness is {0}", outerBright));
                }
            }
        }

        static void TestColorBarsAlignment(string outDir)
        {
            using (Bitmap bars = TestPatternGenerator.CreateColorBars75(512, 512))
            {
                var settings = new ScopeSettings
                {
                    Size = 512,
                    Gain = 1.0f,
                    ShowTargets = true,
                    ShowRings = true,
                    ShowSkinLine = true,
                    ShowAxes = true
                };

                using (Bitmap scope = VectorscopeCore.RenderVectorscope(bars, settings))
                {
                    string path = Path.Combine(outDir, "test_colorbars_scope.png");
                    scope.Save(path, ImageFormat.Png);

                    int size = 512;
                    float cx = size / 2.0f;
                    float cy = size / 2.0f;
                    double scale = VectorscopeCore.GetScale(size);

                    // Check each 75% target to verify that there is non-background pixel data around the target
                    bool allTargetsHit = true;
                    string hitReport = "";

                    foreach (var target in VectorscopeCore.Targets75)
                    {
                        int tx = (int)Math.Round(cx + (target.Cb * scale));
                        int ty = (int)Math.Round(cy - (target.Cr * scale));

                        Color px = scope.GetPixel(tx, ty);
                        int brightness = px.R + px.G + px.B;
                        // Background is (18, 20, 24) sum = 62. Target boxes and traces have brightness > 120.
                        if (brightness < 100)
                        {
                            allTargetsHit = false;
                            hitReport += target.Name + " missed (brightness " + brightness + "); ";
                        }
                    }

                    AssertTrue("Color Bars 75% Target Alignment: All 6 targets hit", allTargetsHit, hitReport);
                }
            }
        }

        static void TestSkinToneClustering(string outDir)
        {
            using (Bitmap skinBmp = TestPatternGenerator.CreateSkinTonePatch(512, 512))
            {
                var settings = new ScopeSettings
                {
                    Size = 512,
                    Gain = 1.0f,
                    ShowSkinLine = false,
                    
                };

                using (Bitmap scope = VectorscopeCore.RenderVectorscope(skinBmp, settings))
                {
                    string path = Path.Combine(outDir, "test_skintone_scope.png");
                    scope.Save(path, ImageFormat.Png);

                    // Probe along the 132 degree axis at r = 48px (where skin patch clusters)
                    int size = 512;
                    float cx = size / 2.0f;
                    float cy = size / 2.0f;
                    double angleRad = 132.0 * (Math.PI / 180.0);
                    int pxOnSkinLine = (int)Math.Round(cx + Math.Cos(angleRad) * 48.0);
                    int pyOnSkinLine = (int)Math.Round(cy - Math.Sin(angleRad) * 48.0);

                    // Perpendicular angle at 42 degrees (empty background)
                    double perpRad = 42.0 * (Math.PI / 180.0);
                    int pxOffLine = (int)Math.Round(cx + Math.Cos(perpRad) * 48.0);
                    int pyOffLine = (int)Math.Round(cy - Math.Sin(perpRad) * 48.0);

                    // Measure peak brightness along the skin line cluster (~132 deg)
                    int maxSkinBrightness = 0;
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            Color c = scope.GetPixel(pxOnSkinLine + dx, pyOnSkinLine + dy);
                            int b = c.R + c.G + c.B;
                            if (b > maxSkinBrightness) maxSkinBrightness = b;
                        }
                    }

                    // Measure peak brightness in off-axis region
                    int maxOffBrightness = 0;
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            Color c = scope.GetPixel(pxOffLine + dx, pyOffLine + dy);
                            int b = c.R + c.G + c.B;
                            if (b > maxOffBrightness) maxOffBrightness = b;
                        }
                    }

                    AssertTrue("Skin Tone Cluster aligns along Skin Tone Line", maxSkinBrightness > maxOffBrightness + 50,
                        string.Format("Skin Line peak: {0}, Off-line peak: {1}", maxSkinBrightness, maxOffBrightness));
                }
            }
        }

        static void TestTrueColorPreservation(string outDir)
        {
            // Test that Red bar yields strongly red trace, and Blue bar yields strongly blue trace in Color Mode
            using (Bitmap bars = TestPatternGenerator.CreateColorBars75(512, 512))
            {
                var settings = new ScopeSettings
                {
                    Size = 512,
                    Gain = 1.0f,
                    ShowRings = false,
                    ShowTargets = false,
                    ShowSkinLine = false,
                    ShowAxes = false
                };

                using (Bitmap scope = VectorscopeCore.RenderVectorscope(bars, settings))
                {
                    string path = Path.Combine(outDir, "test_truecolor_scope.png");
                    scope.Save(path, ImageFormat.Png);

                    int size = 512;
                    float cx = size / 2.0f;
                    float cy = size / 2.0f;
                    double scale = VectorscopeCore.GetScale(size);

                    // Target Red: Cb = -0.08595, Cr = 0.375
                    int rx = (int)Math.Round(cx + (-0.08595 * scale));
                    int ry = (int)Math.Round(cy - (0.375 * scale));
                    Color redTrace = scope.GetPixel(rx, ry);

                    // Target Blue: Cb = 0.375, Cr = -0.03435
                    int bx = (int)Math.Round(cx + (0.375 * scale));
                    int by = (int)Math.Round(cy - (-0.03435 * scale));
                    Color blueTrace = scope.GetPixel(bx, by);

                    AssertTrue("True Color Mode: Red trace has dominant Red channel",
                        redTrace.R > redTrace.G * 1.5 && redTrace.R > redTrace.B * 1.5,
                        string.Format("Red trace RGB: ({0}, {1}, {2})", redTrace.R, redTrace.G, redTrace.B));

                    AssertTrue("True Color Mode: Blue trace has dominant Blue channel",
                        blueTrace.B > blueTrace.R * 1.5 && blueTrace.B > blueTrace.G * 1.5,
                        string.Format("Blue trace RGB: ({0}, {1}, {2})", blueTrace.R, blueTrace.G, blueTrace.B));
                }
            }
        }

        static void TestGainScaling(string outDir)
        {
            using (Bitmap bars = TestPatternGenerator.CreateColorBars75(512, 512))
            {
                // Test with 1.0x gain vs 1.25x gain to remain safely within 512x512 bounds
                var s1 = new ScopeSettings { Size = 512, Gain = 1.0f, ShowTargets = false, ShowRings = false, ShowAxes = false };
                var s2 = new ScopeSettings { Size = 512, Gain = 1.25f, ShowTargets = false, ShowRings = false, ShowAxes = false };

                using (Bitmap scope1 = VectorscopeCore.RenderVectorscope(bars, s1))
                using (Bitmap scope2 = VectorscopeCore.RenderVectorscope(bars, s2))
                {
                    int size = 512;
                    float cx = size / 2.0f;
                    float cy = size / 2.0f;
                    double scale = VectorscopeCore.GetScale(size);

                    // Target Blue: Cb = 0.375, Cr = -0.03435
                    // Blue extends horizontally right, plenty of margin in 512x512
                    int b1x = (int)Math.Round(cx + (0.375 * 1.0 * scale));
                    int b1y = (int)Math.Round(cy - (-0.03435 * 1.0 * scale));

                    int b2x = (int)Math.Round(cx + (0.375 * 1.25 * scale));
                    int b2y = (int)Math.Round(cy - (-0.03435 * 1.25 * scale));

                    Color c1 = scope1.GetPixel(b1x, b1y);
                    Color c2 = scope2.GetPixel(b2x, b2y);

                    AssertTrue("Gain Scaling: 1.0x Blue trace is at 1.0x distance", c1.B > 100,
                        string.Format("Expected blue at 1x distance, got Blue={0}", c1.B));
                    AssertTrue("Gain Scaling: 1.25x Blue trace is at 1.25x distance", c2.B > 100,
                        string.Format("Expected blue at 1.25x distance, got Blue={0}", c2.B));
                }
            }
        }

        static void TestPerformanceBenchmark()
        {
            using (Bitmap testImg = TestPatternGenerator.CreateColorBars75(512, 512))
            {
                var settings = new ScopeSettings { Size = 512, Gain = 1.0f };

                // Warm up
                using (Bitmap w = VectorscopeCore.RenderVectorscope(testImg, settings)) { }

                Stopwatch sw = Stopwatch.StartNew();
                int iterations = 20;
                for (int i = 0; i < iterations; i++)
                {
                    using (Bitmap scope = VectorscopeCore.RenderVectorscope(testImg, settings))
                    {
                    }
                }
                sw.Stop();

                double avgMs = sw.ElapsedMilliseconds / (double)iterations;
                Console.WriteLine(string.Format("Performance Benchmark: Average render latency = {0:F2} ms across {1} runs", avgMs, iterations));

                AssertTrue("Performance Benchmark: Latency < 50ms", avgMs < 50.0,
                    string.Format("Average latency was {0:F2} ms (threshold: 50 ms)", avgMs));
            }
        }

        public static double AngleDistance(double a, double b)
        {
            double diff = Math.Abs(a - b) % 360.0;
            if (diff > 180.0) diff = 360.0 - diff;
            return diff;
        }

        public static int CalculateChannelSat(double channelAngle, double[] targetHues, double boost, double suppress, double bandwidth)
        {
            double twoSigmaSq = 2.0 * bandwidth * bandwidth;
            double bNorm = boost / 100.0;
            double sNorm = suppress / 100.0;

            double maxAffinity = 0.0;
            foreach (double h in targetHues)
            {
                double dist = AngleDistance(channelAngle, h);
                double aff = Math.Exp(-(dist * dist) / twoSigmaSq);
                if (aff > maxAffinity) maxAffinity = aff;
            }

            double penalty = 1.0 - maxAffinity;
            double deltaSat = (bNorm * maxAffinity) - (sNorm * penalty);
            int sat = (int)Math.Round(deltaSat * 100.0);
            return Math.Max(-100, Math.Min(100, sat));
        }

        public static int CalculateChannelLum(int sat, double rho, double density = 100.0)
        {
            double dNorm = density / 100.0;
            double softKnee = 1.0 - (Math.Abs(sat) / 250.0);
            double deltaLum = -dNorm * rho * sat * softKnee;
            int lum = (int)Math.Round(deltaLum);
            return Math.Max(-100, Math.Min(100, lum));
        }

        static void TestColorSeparationMath()
        {
            // Orange (138Â°) and Aqua (283Â°) should be boosted with Skin (132Â°) & Teal (312Â°)
            double[] dualHues = new double[] { 132.0, 312.0 };
            int orangeSat = CalculateChannelSat(138.0, dualHues, 50.0, 35.0, 40.0);
            int aquaSat = CalculateChannelSat(283.0, dualHues, 50.0, 35.0, 40.0);
            int magentaSat = CalculateChannelSat(50.0, dualHues, 50.0, 35.0, 40.0);
            int greenSat = CalculateChannelSat(220.0, dualHues, 50.0, 35.0, 40.0);

            AssertTrue("Color Separation: Orange is boosted on Skin/Teal axis", orangeSat > 30, string.Format("Orange sat: {0}", orangeSat));
            AssertTrue("Color Separation: Aqua is boosted on Skin/Teal axis", aquaSat > 20, string.Format("Aqua sat: {0}", aquaSat));
            AssertTrue("Color Separation: Magenta is suppressed (orthogonal)", magentaSat < -20, string.Format("Magenta sat: {0}", magentaSat));
            AssertTrue("Color Separation: Green is suppressed (orthogonal)", greenSat < -20, string.Format("Green sat: {0}", greenSat));

            // Filmic Subtractive Density Tests (Coupled Saturation & Luminance)
            int orangeLum = CalculateChannelLum(orangeSat, 0.38, 100.0);
            int aquaLum = CalculateChannelLum(aquaSat, 0.52, 100.0);
            int magentaLum = CalculateChannelLum(magentaSat, 0.40, 100.0);
            int greenLum = CalculateChannelLum(greenSat, 0.55, 100.0);

            AssertTrue("Filmic Density: Boosted Orange (Skin) has darkened luminance", orangeLum < -10, string.Format("Orange lum: {0}", orangeLum));
            AssertTrue("Filmic Density: Boosted Aqua (Teal) has darkened luminance", aquaLum < -10, string.Format("Aqua lum: {0}", aquaLum));
            AssertTrue("Filmic Density: Suppressed Magenta has lifted luminance (dye bleaching)", magentaLum > 5, string.Format("Magenta lum: {0}", magentaLum));
            AssertTrue("Filmic Density: Suppressed Green has lifted luminance (dye bleaching)", greenLum > 5, string.Format("Green lum: {0}", greenLum));

            // Density scaling at 0%
            int orangeLumZero = CalculateChannelLum(orangeSat, 0.38, 0.0);
            AssertTrue("Filmic Density: 0% density yields 0 luminance change", orangeLumZero == 0);

            // Triadic (3 hues): 132Â°, 220Â°, 355Â° with bandwidth 30.0
            double[] triHues = new double[] { 132.0, 220.0, 355.0 };
            int triOrange = CalculateChannelSat(138.0, triHues, 50.0, 35.0, 30.0);
            int triGreen = CalculateChannelSat(220.0, triHues, 50.0, 35.0, 30.0);
            int triBlue = CalculateChannelSat(355.0, triHues, 50.0, 35.0, 30.0);
            int triMagenta = CalculateChannelSat(50.0, triHues, 50.0, 35.0, 30.0);

            AssertTrue("Color Separation (Triadic): All 3 target poles are boosted", triOrange > 30 && triGreen > 40 && triBlue > 40);
            AssertTrue("Color Separation (Triadic): Non-target Magenta is suppressed", triMagenta < -15, string.Format("Magenta sat: {0}", triMagenta));

            // Clamp bounds test
            int maxSat = CalculateChannelSat(132.0, new double[] { 132.0 }, 150.0, 150.0, 40.0);
            int minSat = CalculateChannelSat(0.0, new double[] { 180.0 }, 150.0, 150.0, 40.0);
            int maxLum = CalculateChannelLum(maxSat, 0.55, 150.0);
            int minLum = CalculateChannelLum(minSat, 0.55, 150.0);
            AssertTrue("Color Separation: Values clamp strictly to [-100, +100]", maxSat <= 100 && minSat >= -100 && maxLum <= 100 && minLum >= -100);
        }

        static void TestTargetHuesParsing()
        {
            var s = VectorscopeCore.ParseArgs(new string[] { "--targets", "132:75,312:40,220:60" });
            AssertTrue("Targets CLI Parsing: Correct count", s.Targets.Count == 3);
            AssertTrue("Targets CLI Parsing: Target 1 is 132:75", Math.Abs(s.Targets[0].Hue - 132.0) < 0.1 && Math.Abs(s.Targets[0].Sat - 75.0) < 0.1);
            AssertTrue("Targets CLI Parsing: Target 2 is 312:40", Math.Abs(s.Targets[1].Hue - 312.0) < 0.1 && Math.Abs(s.Targets[1].Sat - 40.0) < 0.1);
            AssertTrue("Targets CLI Parsing: Target 3 is 220:60", Math.Abs(s.Targets[2].Hue - 220.0) < 0.1 && Math.Abs(s.Targets[2].Sat - 60.0) < 0.1);
        }

        static void TestTargetHuesLineRendering(string outDir)
        {
            using (Bitmap grayBmp = TestPatternGenerator.CreateGrayscaleRamp(512, 512))
            {
                var settingsNoTargets = new ScopeSettings
                {
                    Size = 512,
                    ShowRings = false,
                    ShowTargets = false,
                    ShowSkinLine = false,
                    ShowAxes = false
                };

                var settingsWithTargets = new ScopeSettings
                {
                    Size = 512,
                    ShowRings = false,
                    ShowTargets = false,
                    ShowSkinLine = false,
                    ShowAxes = false,
                    Targets = new List<TargetColorPoint> { new TargetColorPoint(312.0, 50.0) }
                };

                using (Bitmap scopeNoHues = VectorscopeCore.RenderVectorscope(grayBmp, settingsNoTargets))
                using (Bitmap scopeWithHues = VectorscopeCore.RenderVectorscope(grayBmp, settingsWithTargets))
                {
                    int size = 512;
                    float cx = size / 2.0f;
                    float cy = size / 2.0f;
                    double scale = VectorscopeCore.GetScale(size);
                    double rad = 312.0 * (Math.PI / 180.0);
                    float testDist = (float)(0.5 * 0.596 * scale);

                    int px = (int)Math.Round(cx + Math.Cos(rad) * testDist);
                    int py = (int)Math.Round(cy - Math.Sin(rad) * testDist);

                    Color cNo = scopeNoHues.GetPixel(px, py);
                    Color cWith = scopeWithHues.GetPixel(px, py);

                    bool dotDrawn = (cWith.R != cNo.R || cWith.G != cNo.G || cWith.B != cNo.B);
                    AssertTrue("Target Point Rendering: Dot drawn at (312 deg, 50% sat)", dotDrawn,
                        string.Format("Without: {0}, With: {1}", cNo, cWith));
                }
            }
        }

        static void TestColorSeparationSimulationAndPreview(string outDir)
        {
            // 1. Unified Math Adjustments in C#
            var targets = new List<TargetColorPoint>
            {
                new TargetColorPoint(132.0, 80.0),
                new TargetColorPoint(312.0, 80.0),
            };
            float[] lowBandSat = new float[] { 20, 20, 20, 20, 20, 20, 20, 20 };
            float[] highBandSat = new float[] { 65, 65, 65, 65, 65, 65, 65, 65 };

            // Strength = 0 -> all 16 HSL outputs are 0
            float[] satZero, lumZero;
            ColorSeparationSimulator.CalculateAdjustments(targets, lowBandSat, 0.0f, 100.0f, 40.0f, out satZero, out lumZero);
            AssertTrue("Unified Separation: Strength 0 yields 0 sat/lum",
                Math.Abs(satZero[1]) < 0.001f && Math.Abs(lumZero[1]) < 0.001f);

            // Density = 0 -> 0 luminance change, saturation still active
            float[] satNoLum, lumNoLum;
            ColorSeparationSimulator.CalculateAdjustments(targets, lowBandSat, 80.0f, 0.0f, 40.0f, out satNoLum, out lumNoLum);
            AssertTrue("Unified Separation: Density 0 yields 0 luminance delta",
                Math.Abs(lumNoLum[1]) < 0.001f && Math.Abs(lumNoLum[7]) < 0.001f);
            AssertTrue("Unified Separation: Saturation boosted when Density is 0", satNoLum[1] > 20.0f);

            // Gap scaling: larger (TargetSat - BandSat) gap yields stronger boost
            float[] satLowGap, lumLowGap;
            ColorSeparationSimulator.CalculateAdjustments(targets, highBandSat, 80.0f, 100.0f, 40.0f, out satLowGap, out lumLowGap);
            AssertTrue("Unified Separation: Larger saturation gap boosts more", satNoLum[1] > satLowGap[1]);

            float[] satFull, lumFull;
            ColorSeparationSimulator.CalculateAdjustments(targets, lowBandSat, 80.0f, 100.0f, 40.0f, out satFull, out lumFull);
            AssertTrue("Unified Separation: Orange darkened with high density", lumFull[1] < -5.0f);
            AssertTrue("Unified Separation: Magenta suppressed & bleached", satFull[7] < -10.0f && lumFull[7] > 3.0f);

            // Hue Shaping via RGB Point Tone Curves + Black/White Pinning
            int[] rcZ, gcZ, bcZ;
            ColorSeparationSimulator.CalculateToneCurves(targets, 0.0f, 40.0f, 100.0f, out rcZ, out gcZ, out bcZ);
            AssertTrue("Tone Curve Hue: Strength 0 yields linear RGB curves",
                rcZ[1] == 0 && rcZ[3] == 64 && rcZ[5] == 128 && rcZ[7] == 192 && rcZ[9] == 255 &&
                gcZ[1] == 0 && gcZ[5] == 128 && gcZ[9] == 255 &&
                bcZ[1] == 0 && bcZ[5] == 128 && bcZ[9] == 255);

            int[] rcPin, gcPin, bcPin;
            ColorSeparationSimulator.CalculateToneCurves(targets, 80.0f, 40.0f, 100.0f, out rcPin, out gcPin, out bcPin);
            AssertTrue("Tone Curve Hue: Interior points shift when Strength > 0",
                rcPin[3] != 64 || rcPin[7] != 192 || gcPin[7] != 192 || bcPin[7] != 192);
            AssertTrue("Tone Curve Hue: Pin 100% keeps black (0,0) and white (255,255) endpoints pinned",
                rcPin[1] == 0 && gcPin[1] == 0 && bcPin[1] == 0 &&
                rcPin[9] == 255 && gcPin[9] == 255 && bcPin[9] == 255);

            int[] rcUnpin, gcUnpin, bcUnpin;
            ColorSeparationSimulator.CalculateToneCurves(targets, 80.0f, 40.0f, 0.0f, out rcUnpin, out gcUnpin, out bcUnpin);
            AssertTrue("Tone Curve Hue: Pin 0% shifts black/white endpoints",
                (rcUnpin[1] > 0 || gcUnpin[1] > 0 || bcUnpin[1] > 0) &&
                (rcUnpin[9] < 255 || gcUnpin[9] < 255 || bcUnpin[9] < 255));

            int[] rcHalf, gcHalf, bcHalf;
            ColorSeparationSimulator.CalculateToneCurves(targets, 80.0f, 40.0f, 50.0f, out rcHalf, out gcHalf, out bcHalf);
            int dispUnpin = rcUnpin[1] + gcUnpin[1] + bcUnpin[1] + (255 - rcUnpin[9]) + (255 - gcUnpin[9]) + (255 - bcUnpin[9]);
            int dispHalf = rcHalf[1] + gcHalf[1] + bcHalf[1] + (255 - rcHalf[9]) + (255 - gcHalf[9]) + (255 - bcHalf[9]);
            AssertTrue("Tone Curve Hue: Pin 50% scales endpoint offsets proportionally between 0% and 100%",
                dispHalf > 0 && dispHalf < dispUnpin);

            // Hue Shaping: Smooth Sine-Attractor anchors poles and pulls intermediate hues toward nearest pole
            var targetHues = new List<double> { 132.0, 312.0 };
            double poleShift = ColorSeparationSimulator.ComputeHueAttractorShift(132.0, targetHues, 1.0f);
            AssertTrue("Hue Attractor: Target pole has 0 shift", Math.Abs(poleShift) < 0.001);
            double pullDownShift = ColorSeparationSimulator.ComputeHueAttractorShift(160.0, targetHues, 1.0f);
            AssertTrue("Hue Attractor: Hue between 132 and midpoint 222 pulls toward 132", pullDownShift < -0.5);
            double pullUpShift = ColorSeparationSimulator.ComputeHueAttractorShift(280.0, targetHues, 1.0f);
            AssertTrue("Hue Attractor: Hue between midpoint 222 and 312 pulls toward 312", pullUpShift > 0.5);

            // 2. Pixel Level Simulation & MeasureBandSaturations
            using (Bitmap testImg = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(testImg))
                {
                    using (SolidBrush grayBrush = new SolidBrush(Color.FromArgb(128, 128, 128)))
                        g.FillRectangle(grayBrush, 0, 0, 256, 128);
                    using (SolidBrush skinBrush = new SolidBrush(Color.FromArgb(210, 150, 110)))
                        g.FillRectangle(skinBrush, 0, 128, 256, 128);
                }

                float[] measured = VectorscopeCore.MeasureBandSaturations(testImg);
                AssertTrue("MeasureBandSaturations: Returns 8 bands in [0, 100]",
                    measured.Length == 8 && measured[1] > 5.0f && measured[1] <= 100.0f);

                var settings = new ScopeSettings
                {
                    Targets = targets,
                    BandSat = lowBandSat,
                    Strength = 80.0f,
                    Density = 100.0f,
                    Bandwidth = 40.0f,
                };

                using (Bitmap simulated = ColorSeparationSimulator.Apply(testImg, settings))
                {
                    Color origGray = testImg.GetPixel(64, 64);
                    Color simGray = simulated.GetPixel(64, 64);
                    AssertTrue("Pixel Simulation: Neutral gray preserved",
                        Math.Abs(origGray.R - simGray.R) <= 1 &&
                        Math.Abs(origGray.G - simGray.G) <= 1 &&
                        Math.Abs(origGray.B - simGray.B) <= 1);

                    Color origSkin = testImg.GetPixel(64, 192);
                    Color simSkin = simulated.GetPixel(64, 192);
                    AssertTrue("Pixel Simulation: Skin tone color separation applied",
                        simSkin.R >= origSkin.R - 20 && (simSkin.R - simSkin.B) > (origSkin.R - origSkin.B));
                }

                // 3. CLI Integration (Scope + Stats)
                string testInputJpg = Path.Combine(outDir, "cli_test_input.jpg");
                testImg.Save(testInputJpg, ImageFormat.Jpeg);

                string cliScopeOut = Path.Combine(outDir, "cli_scope.png");
                string cliStatsOut = Path.Combine(outDir, "cli_stats.txt");

                string[] cliArgs = new string[]
                {
                    "--input", testInputJpg,
                    "--output", cliScopeOut,
                    "--stats-output", cliStatsOut,
                    "--targets", "132:80,312:80",
                    "--strength", "80",
                    "--density", "100",
                    "--bandwidth", "40"
                };

                int cliResult = VectorscopeCore.Main(cliArgs);
                AssertTrue("CLI Engine: Executed with code 0", cliResult == 0);
                AssertTrue("CLI Engine: Generated Scope PNG", File.Exists(cliScopeOut));
                AssertTrue("CLI Engine: Generated Stats File", File.Exists(cliStatsOut));
            }
        }
    }
}
