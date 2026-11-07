using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace VideoTransitionBatchApp
{
    public enum TransitionStyle
    {
        BrushStroke,
        Watercolor,
        SmoothFade,
        Wipe,
        Split,
        Dissolve,
        Checkerboard,
        Blinds,
        Clock,
        None
    }

    public enum KenBurnsMode
    {
        Off,
        ZoomIn,
        ZoomOut
    }

    public static class MaskGenerator
    {
        private const string BrushInMatte = "brush_in.mp4";
        private const string BrushOutMatte = "brush_out.mp4";
        private const string WcInMatte = "wc_in.mp4";
        private const string WcOutMatte = "wc_out.mp4";

        public static string EnsureMask(string ffmpegPath, string masksCacheDir, TransitionStyle style, int width, int height, double fps, double durationInSec, bool isReverse)
        {
            string direction = isReverse ? "out" : "in";
            int totalFrames = (int)Math.Max(10, Math.Round(durationInSec * fps));

            Directory.CreateDirectory(masksCacheDir);

            // Prefer external matte videos (brush_in/out, wc_in/out) for brush & watercolor.
            string externalSource;
            if (TryGetExternalMatteSource(style, isReverse, masksCacheDir, out externalSource))
            {
                string matteCacheName = string.Format("mask_{0}_{1}x{2}_{3}fps_{4}f_{5}_matte2.mp4",
                    style.ToString().ToLower(), width, height, (int)fps, totalFrames, direction);
                string matteCachePath = Path.Combine(masksCacheDir, matteCacheName);

                bool cacheFresh = false;
                if (File.Exists(matteCachePath))
                {
                    try
                    {
                        cacheFresh = File.GetLastWriteTimeUtc(externalSource) <= File.GetLastWriteTimeUtc(matteCachePath);
                    }
                    catch
                    {
                        cacheFresh = true;
                    }
                }

                if (cacheFresh) return matteCachePath;

                if (BuildMatteCache(ffmpegPath, externalSource, matteCachePath, width, height, fps, durationInSec, totalFrames, isReverse))
                {
                    return matteCachePath;
                }

                try { if (File.Exists(matteCachePath)) File.Delete(matteCachePath); } catch { }
                // Fall through to procedural fallback if matte conversion failed.
            }

            string maskFileName = string.Format("mask_{0}_{1}x{2}_{3}fps_{4}f_{5}.mp4",
                style.ToString().ToLower(), width, height, (int)fps, totalFrames, direction);

            string maskPath = Path.Combine(masksCacheDir, maskFileName);
            if (File.Exists(maskPath)) return maskPath;

            string baseMaskPath = Path.Combine(masksCacheDir, string.Format("mask_{0}_{1}x{2}_{3}fps_{4}f_in.mp4", 
                style.ToString().ToLower(), width, height, (int)fps, totalFrames));

            if (isReverse && File.Exists(baseMaskPath))
            {
                ProcessStartInfo psiRev = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = string.Format("-y -i \"{0}\" -vf reverse -c:v libx264 -pix_fmt yuv420p -color_range pc \"{1}\"", baseMaskPath, maskPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psiRev)) { p.WaitForExit(); }
                if (File.Exists(maskPath)) return maskPath;
            }

            string tempFramesDir = Path.Combine(masksCacheDir, "temp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFramesDir);

            try
            {
                // No legacy procedural brush/watercolor: matte files are the only
                // source of truth. Without a matte video, fall back to a plain
                // smooth fade (solid black -> solid white).
                switch (style)
                {
                    case TransitionStyle.Wipe:
                        GenerateWipeFrames(tempFramesDir, width, height, totalFrames);
                        break;
                    case TransitionStyle.Split:
                        GenerateSplitFrames(tempFramesDir, width, height, totalFrames);
                        break;
                    case TransitionStyle.Dissolve:
                        GenerateDissolveFrames(tempFramesDir, width, height, totalFrames);
                        break;
                    case TransitionStyle.Checkerboard:
                        GenerateCheckerboardFrames(tempFramesDir, width, height, totalFrames);
                        break;
                    case TransitionStyle.Blinds:
                        GenerateBlindsFrames(tempFramesDir, width, height, totalFrames);
                        break;
                    case TransitionStyle.Clock:
                        GenerateClockFrames(tempFramesDir, width, height, totalFrames);
                        break;
                    case TransitionStyle.SmoothFade:
                    default:
                        GenerateSmoothFadeFrames(tempFramesDir, width, height, totalFrames);
                        break;
                }

                string framesPattern = Path.Combine(tempFramesDir, "frame_%04d.png");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = string.Format("-y -framerate {0} -i \"{1}\" -vf \"curves=m='0/0 0.06/0 0.94/1 1/1'\" -c:v libx264 -preset fast -crf 10 -pix_fmt yuv420p -color_range pc \"{2}\"",
                        fps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), framesPattern, baseMaskPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi)) { p.WaitForExit(); }

                if (isReverse)
                {
                    ProcessStartInfo psiRev = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = string.Format("-y -i \"{0}\" -vf reverse -c:v libx264 -pix_fmt yuv420p -color_range pc \"{1}\"", baseMaskPath, maskPath),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (Process p = Process.Start(psiRev)) { p.WaitForExit(); }
                    return maskPath;
                }

                return baseMaskPath;
            }
            finally
            {
                if (Directory.Exists(tempFramesDir))
                {
                    try { Directory.Delete(tempFramesDir, true); } catch { }
                }
            }
        }

        private static bool TryGetExternalMatteSource(TransitionStyle style, bool isReverse, string masksCacheDir, out string sourcePath)
        {
            sourcePath = null;
            string fileName = null;

            if (style == TransitionStyle.BrushStroke)
            {
                fileName = isReverse ? BrushOutMatte : BrushInMatte;
            }
            else if (style == TransitionStyle.Watercolor)
            {
                fileName = isReverse ? WcOutMatte : WcInMatte;
            }
            else
            {
                return false;
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(appDir, "transition", fileName),
                Path.Combine(appDir, fileName),
                Path.Combine(masksCacheDir, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "transition", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    sourcePath = candidates[i];
                    return true;
                }
            }

            return false;
        }

        private static double ProbeSourceDuration(string ffmpegPath, string filePath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = string.Format("-i \"{0}\"", filePath),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                string output = "";
                using (Process p = Process.Start(psi))
                {
                    output = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                }

                Match mDur = Regex.Match(output, @"Duration:\s*(\d+):(\d+):(\d+(\.\d+)?)");
                if (mDur.Success)
                {
                    int hours = int.Parse(mDur.Groups[1].Value);
                    int mins = int.Parse(mDur.Groups[2].Value);
                    double secs = double.Parse(mDur.Groups[3].Value, CultureInfo.InvariantCulture);
                    double dur = hours * 3600 + mins * 60 + secs;
                    if (dur > 0.1) return dur;
                }
            }
            catch
            {
            }
            return 1.0;
        }

        private static bool BuildMatteCache(string ffmpegPath, string sourcePath, string cachePath, int width, int height, double fps, double targetDuration, int totalFrames, bool isReverse)
        {
            try
            {
                double srcDur = ProbeSourceDuration(ffmpegPath, sourcePath);
                if (srcDur <= 0.1) srcDur = 1.0;
                if (targetDuration <= 0.1) targetDuration = 1.0;

                double factor = targetDuration / srcDur;
                string sFactor = factor.ToString("0.#####", CultureInfo.InvariantCulture);
                string sFps = fps.ToString("0.##", CultureInfo.InvariantCulture);
                string sTarget = targetDuration.ToString("0.###", CultureInfo.InvariantCulture);

                // Lock the matte edges to solid black/white and crush haze so the
                // start/end of the transition sit on true solid black.
                double hold = 2.0 / Math.Max(1.0, fps);
                string sHold = hold.ToString("0.###", CultureInfo.InvariantCulture);
                string fadeEdge = "";
                if (!isReverse)
                {
                    fadeEdge = string.Format(CultureInfo.InvariantCulture,
                        ",fade=t=in:st=0:d={0}:color=black", sHold);
                }
                else
                {
                    double outSt = Math.Max(0.0, targetDuration - hold);
                    string sOutSt = outSt.ToString("0.###", CultureInfo.InvariantCulture);
                    fadeEdge = string.Format(CultureInfo.InvariantCulture,
                        ",fade=t=out:st={0}:d={1}:color=black", sOutSt, sHold);
                }

                string vf = string.Format(CultureInfo.InvariantCulture,
                    "scale={0}:{1}:flags=lanczos:out_range=full,setpts=PTS*({2}),fps={3},curves=m='0/0 0.08/0 0.92/1 1/1'{4},format=yuv420p",
                    width, height, sFactor, sFps, fadeEdge);

                string arguments = string.Format(CultureInfo.InvariantCulture,
                    "-y -i \"{0}\" -vf \"{1}\" -c:v libx264 -preset fast -crf 16 -pix_fmt yuv420p -color_range pc -r {2} -t {3} -frames:v {4} \"{5}\"",
                    sourcePath, vf, sFps, sTarget, totalFrames, cachePath);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0) return false;
                }

                if (File.Exists(cachePath))
                {
                    FileInfo fi = new FileInfo(cachePath);
                    if (fi.Length > 0) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void GenerateWipeFrames(string outDir, int width, int height, int totalFrames)
        {
            for (int f = 0; f < totalFrames; f++)
            {
                float p = totalFrames > 1 ? (float)f / (totalFrames - 1) : 1f;
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.None;
                    if (p > 0f)
                    {
                        int edge = (int)Math.Round(p * width);
                        if (edge >= width) g.Clear(Color.White);
                        else using (Brush brush = new SolidBrush(Color.White)) g.FillRectangle(brush, 0, 0, Math.Max(1, edge), height);
                    }
                    bmp.Save(Path.Combine(outDir, string.Format("frame_{0:D4}.png", f)), ImageFormat.Png);
                }
            }
        }

        private static void GenerateSplitFrames(string outDir, int width, int height, int totalFrames)
        {
            for (int f = 0; f < totalFrames; f++)
            {
                float p = totalFrames > 1 ? (float)f / (totalFrames - 1) : 1f;
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.None;
                    if (p > 0f)
                    {
                        float half = height / 2f;
                        int topH = (int)Math.Round(p * half);
                        int botH = (int)Math.Round(p * half);
                        if (topH >= half && botH >= half) g.Clear(Color.White);
                        else
                        {
                            using (Brush brush = new SolidBrush(Color.White))
                            {
                                if (topH > 0) g.FillRectangle(brush, 0, (int)(half - topH), width, Math.Max(1, topH));
                                if (botH > 0) g.FillRectangle(brush, 0, (int)half, width, Math.Max(1, botH));
                            }
                        }
                    }
                    bmp.Save(Path.Combine(outDir, string.Format("frame_{0:D4}.png", f)), ImageFormat.Png);
                }
            }
        }

        private static void GenerateDissolveFrames(string outDir, int width, int height, int totalFrames)
        {
            int cols = 64;
            int rows = 36;
            float cellW = (float)width / cols;
            float cellH = (float)height / rows;
            Random rnd = new Random(7);
            double[] rank = new double[cols * rows];
            for (int i = 0; i < rank.Length; i++) rank[i] = rnd.NextDouble();
            for (int f = 0; f < totalFrames; f++)
            {
                float p = totalFrames > 1 ? (float)f / (totalFrames - 1) : 1f;
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.None;
                    if (p > 0f)
                    {
                        if (p >= 1f) g.Clear(Color.White);
                        else
                        {
                            using (Brush brush = new SolidBrush(Color.White))
                            {
                                for (int y = 0; y < rows; y++)
                                {
                                    for (int x = 0; x < cols; x++)
                                    {
                                        if (rank[y * cols + x] <= p)
                                            g.FillRectangle(brush, x * cellW, y * cellH, cellW, cellH);
                                    }
                                }
                            }
                        }
                    }
                    bmp.Save(Path.Combine(outDir, string.Format("frame_{0:D4}.png", f)), ImageFormat.Png);
                }
            }
        }

        private static void GenerateCheckerboardFrames(string outDir, int width, int height, int totalFrames)
        {
            int cols = 14;
            int rows = 8;
            float cellW = (float)width / cols;
            float cellH = (float)height / rows;
            double[] tileT = new double[cols * rows];
            double maxDist = Math.Sqrt(cols * cols + rows * rows);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double dist = Math.Sqrt(x * x + y * y) / maxDist;
                    tileT[y * cols + x] = ((x + y) % 2) * 0.18 + dist * 0.82;
                }
            }
            for (int f = 0; f < totalFrames; f++)
            {
                float p = totalFrames > 1 ? (float)f / (totalFrames - 1) : 1f;
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.None;
                    if (p > 0f)
                    {
                        if (p >= 1f) g.Clear(Color.White);
                        else
                        {
                            using (Brush brush = new SolidBrush(Color.White))
                            {
                                for (int y = 0; y < rows; y++)
                                {
                                    for (int x = 0; x < cols; x++)
                                    {
                                        double start = tileT[y * cols + x];
                                        if (p >= start)
                                        {
                                            double local = Math.Min(1.0, (p - start) / Math.Max(0.01, 1.0 - start));
                                            float w = (float)(cellW * local);
                                            float h = (float)(cellH * local);
                                            g.FillRectangle(brush, x * cellW, y * cellH, w, h);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    bmp.Save(Path.Combine(outDir, string.Format("frame_{0:D4}.png", f)), ImageFormat.Png);
                }
            }
        }

        private static void GenerateBlindsFrames(string outDir, int width, int height, int totalFrames)
        {
            int bars = 12;
            float barH = (float)height / bars;
            double[] delay = new double[bars];
            Random rnd = new Random(21);
            for (int i = 0; i < bars; i++) delay[i] = rnd.NextDouble() * 0.45;
            for (int f = 0; f < totalFrames; f++)
            {
                float p = totalFrames > 1 ? (float)f / (totalFrames - 1) : 1f;
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.None;
                    if (p > 0f)
                    {
                        if (p >= 1f) g.Clear(Color.White);
                        else
                        {
                            using (Brush brush = new SolidBrush(Color.White))
                            {
                                for (int b = 0; b < bars; b++)
                                {
                                    double local = Math.Max(0.0, Math.Min(1.0, (p - delay[b]) / (1.0 - delay[b])));
                                    if (local > 0)
                                        g.FillRectangle(brush, 0, b * barH, width, barH * (float)local);
                                }
                            }
                        }
                    }
                    bmp.Save(Path.Combine(outDir, string.Format("frame_{0:D4}.png", f)), ImageFormat.Png);
                }
            }
        }

        private static void GenerateClockFrames(string outDir, int width, int height, int totalFrames)
        {
            float radius = (float)Math.Sqrt(width * width + height * height) / 2f;
            for (int f = 0; f < totalFrames; f++)
            {
                float p = totalFrames > 1 ? (float)f / (totalFrames - 1) : 1f;
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.None;
                    if (p > 0f)
                    {
                        if (p >= 1f) g.Clear(Color.White);
                        else
                        {
                            using (Brush brush = new SolidBrush(Color.White))
                            {
                                g.FillPie(brush, width / 2f - radius, height / 2f - radius, radius * 2f, radius * 2f, -90f, 360f * p);
                            }
                        }
                    }
                    bmp.Save(Path.Combine(outDir, string.Format("frame_{0:D4}.png", f)), ImageFormat.Png);
                }
            }
        }

        private static void GenerateSmoothFadeFrames(string outDir, int width, int height, int totalFrames)
        {
            for (int f = 0; f < totalFrames; f++)
            {
                float t = totalFrames > 1 ? (float)f / (totalFrames - 1) : 1f;
                int val = (int)Math.Max(0, Math.Min(255, Math.Round(t * 255)));
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Color c = Color.FromArgb(val, val, val);
                    g.Clear(c);
                    bmp.Save(Path.Combine(outDir, string.Format("frame_{0:D4}.png", f)), ImageFormat.Png);
                }
            }
        }
    }
}
