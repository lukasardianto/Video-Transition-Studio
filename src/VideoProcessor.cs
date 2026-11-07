using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace VideoTransitionBatchApp
{
    public class MediaInfo
    {
        public string FilePath { get; set; }
        public string FileName { get { return Path.GetFileName(FilePath); } }
        public bool IsImage { get; set; }
        public double DurationSeconds { get; set; }
        public double InDuration { get; set; }
        public double OutDuration { get; set; }
        public TransitionStyle Style { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int OutputWidth { get; set; }
        public int OutputHeight { get; set; }
        public double Fps { get; set; }
        public bool HasAudio { get; set; }
        public long FileSizeBytes { get; set; }
        public double ZoomPercent { get; set; }
        public KenBurnsMode Animation { get; set; }

        public MediaInfo()
        {
            InDuration = 1.2;
            OutDuration = 1.2;
            Style = TransitionStyle.BrushStroke;
            OutputWidth = 1920;
            OutputHeight = 1080;
            ZoomPercent = 12.0;
            Animation = KenBurnsMode.Off;
        }
    }

    public class ProcessProgressEventArgs : EventArgs
    {
        public double Percent { get; set; }
        public string CurrentTime { get; set; }
        public string Speed { get; set; }
        public string RawLine { get; set; }
    }

    public static class VideoProcessor
    {
        public static readonly string[] ImageExtensions = new string[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff" };
        public static readonly string[] VideoExtensions = new string[] { ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v" };

        public static string GetWritableMasksCacheDir()
        {
            string preferred = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "masks_cache");
            try
            {
                Directory.CreateDirectory(preferred);
                string probe = Path.Combine(preferred, "_write_test.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return preferred;
            }
            catch
            {
            }
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Video Transition Studio", "masks_cache");
            try { Directory.CreateDirectory(fallback); } catch { }
            return fallback;
        }

        public static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            foreach (string imgExt in ImageExtensions)
            {
                if (ext == imgExt) return true;
            }
            return false;
        }

        public static string FindFFmpeg()
        {
            // Embedded in app directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localFfmpeg = Path.Combine(appDir, "ffmpeg.exe");
            if (File.Exists(localFfmpeg)) return localFfmpeg;

            string localBinFfmpeg = Path.Combine(appDir, "bin", "ffmpeg.exe");
            if (File.Exists(localBinFfmpeg)) return localBinFfmpeg;

            // System paths
            string[] candidatePaths = new string[]
            {
                @"C:\Program Files\MediaHuman\YouTube Downloader\ffmpeg\ffmpeg.exe",
                @"C:\Program Files (x86)\Digiarty\VideoProc Converter\ffmpeg.exe"
            };

            foreach (string p in candidatePaths)
            {
                if (File.Exists(p)) return p;
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string[] parts = pathEnv.Split(';');
                foreach (string part in parts)
                {
                    string candidate = Path.Combine(part.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        public static MediaInfo ProbeMedia(string ffmpegPath, string filePath, double defaultImageDuration = 7.0, TransitionStyle defaultStyle = TransitionStyle.BrushStroke, double defaultIn = 1.2, double defaultOut = 1.2)
        {
            MediaInfo info = new MediaInfo();
            info.FilePath = filePath;
            info.InDuration = defaultIn;
            info.OutDuration = defaultOut;
            info.Style = defaultStyle;
            FileInfo fi = new FileInfo(filePath);
            info.FileSizeBytes = fi.Exists ? fi.Length : 0;
            info.Fps = 30.0;
            info.HasAudio = false;

            if (IsImageFile(filePath))
            {
                info.IsImage = true;
                info.DurationSeconds = defaultImageDuration;
                try
                {
                    using (Image img = Image.FromFile(filePath))
                    {
                        info.Width = img.Width;
                        info.Height = img.Height;
                    }
                }
                catch
                {
                    info.Width = 1920;
                    info.Height = 1080;
                }
                return info;
            }

            info.IsImage = false;
            info.Width = 1920;
            info.Height = 1080;
            info.DurationSeconds = 7.0;

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
                info.DurationSeconds = hours * 3600 + mins * 60 + secs;
            }

            Match mVid = Regex.Match(output, @"Stream.*Video:.*?(\d{3,5})x(\d{3,5})");
            if (mVid.Success)
            {
                info.Width = int.Parse(mVid.Groups[1].Value);
                info.Height = int.Parse(mVid.Groups[2].Value);
            }

            Match mFps = Regex.Match(output, @"(\d+(\.\d+)?)\s*fps");
            if (mFps.Success)
            {
                double fpsVal;
                if (double.TryParse(mFps.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out fpsVal))
                {
                    if (fpsVal > 5) info.Fps = fpsVal;
                }
            }

            if (output.Contains("Stream") && output.Contains("Audio:"))
            {
                info.HasAudio = true;
            }

            return info;
        }

        private static string BuildSourceFilter(int targetWidth, int targetHeight, KenBurnsMode animation, double zoomPercent, string sTotDur, string sFps)
        {
            string scalePad = string.Format(CultureInfo.InvariantCulture,
                "scale={0}:{1}:force_original_aspect_ratio=decrease:out_color_matrix=bt709,pad={0}:{1}:(ow-iw)/2:(oh-ih)/2,setsar=1",
                targetWidth, targetHeight);

            if (animation == KenBurnsMode.Off)
            {
                return scalePad + ",format=yuv420p";
            }

            int hiWidth = targetWidth * 2;
            int hiHeight = targetHeight * 2;
            double zoomRatio = Math.Max(0.0, Math.Min(0.40, zoomPercent / 100.0));
            string sRatio = zoomRatio.ToString("0.####", CultureInfo.InvariantCulture);
            string sBase = (1.0 + zoomRatio).ToString("0.####", CultureInfo.InvariantCulture);
            string zoomZExpr;
            if (animation == KenBurnsMode.ZoomIn)
            {
                zoomZExpr = string.Format(CultureInfo.InvariantCulture,
                    "1.0+{0}*(3*min(max(it/{1},0),1)*min(max(it/{1},0),1)-2*min(max(it/{1},0),1)*min(max(it/{1},0),1)*min(max(it/{1},0),1))",
                    sRatio, sTotDur);
            }
            else
            {
                zoomZExpr = string.Format(CultureInfo.InvariantCulture,
                    "{0}-{1}*(3*min(max(it/{2},0),1)*min(max(it/{2},0),1)-2*min(max(it/{2},0),1)*min(max(it/{2},0),1)*min(max(it/{2},0),1))",
                    sBase, sRatio, sTotDur);
            }

            string zoomFilter = string.Format(CultureInfo.InvariantCulture,
                "zoompan=z='{0}':d=1:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s={1}x{2}:fps={3}",
                zoomZExpr, hiWidth, hiHeight, sFps);

            string kenBurnsVF = string.Format(CultureInfo.InvariantCulture,
                "scale={0}:{1}:flags=bicubic:out_color_matrix=bt709,{2},scale={3}:{4}:flags=bicubic,format=yuv420p",
                hiWidth, hiHeight, zoomFilter, targetWidth, targetHeight);

            return scalePad + "," + kenBurnsVF;
        }

        public static bool ProcessMedia(
            string ffmpegPath,
            string masksCacheDir,
            MediaInfo mediaInfo,
            string outputPath,
            string bgColor,
            string bgImagePath,
            Action<ProcessProgressEventArgs> onProgress,
            CancellationToken cancelToken,
            Action<string> onLog)
        {
            if (mediaInfo.DurationSeconds <= 0.5)
            {
                onLog("Error: Durasi media terlalu pendek: " + mediaInfo.DurationSeconds);
                return false;
            }

            double inDuration = mediaInfo.InDuration;
            double outDuration = mediaInfo.OutDuration;
            TransitionStyle style = mediaInfo.Style;

            if (inDuration + outDuration >= mediaInfo.DurationSeconds)
            {
                double safeHalf = (mediaInfo.DurationSeconds * 0.45);
                inDuration = Math.Min(inDuration, safeHalf);
                outDuration = Math.Min(outDuration, safeHalf);
            }

            double midDuration = mediaInfo.DurationSeconds - inDuration - outDuration;
            if (midDuration < 0.1) midDuration = 0.1;

            int targetWidth = mediaInfo.OutputWidth > 0 ? mediaInfo.OutputWidth : (mediaInfo.IsImage ? 1920 : mediaInfo.Width);
            int targetHeight = mediaInfo.OutputHeight > 0 ? mediaInfo.OutputHeight : (mediaInfo.IsImage ? 1080 : mediaInfo.Height);
            if (targetWidth <= 0) targetWidth = 1920;
            if (targetHeight <= 0) targetHeight = 1080;

            string sInDur = inDuration.ToString("0.###", CultureInfo.InvariantCulture);
            string sOutDur = outDuration.ToString("0.###", CultureInfo.InvariantCulture);
            string sMidDur = midDuration.ToString("0.###", CultureInfo.InvariantCulture);
            string sTotDur = mediaInfo.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            string sFps = mediaInfo.Fps.ToString("0.##", CultureInfo.InvariantCulture);
            double outStart = mediaInfo.DurationSeconds - outDuration;
            string sOutStart = outStart.ToString("0.###", CultureInfo.InvariantCulture);

            string arguments = "";

            // Ken Burns is an independent animation layer applied to the source
            // over the full duration; the matte transition runs on top of it.
            string anim = BuildSourceFilter(targetWidth, targetHeight, mediaInfo.Animation, mediaInfo.ZoomPercent, sTotDur, sFps);

            if (style == TransitionStyle.None)
            {
                if (mediaInfo.IsImage)
                {
                    string vf = anim;
                    arguments = string.Format(CultureInfo.InvariantCulture,
                        "-y -loop 1 -framerate {0} -t {1} -i \"{2}\" -vf \"{3}\" -c:v libx264 -preset fast -crf 16 -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 -pix_fmt yuv420p \"{4}\"",
                        sFps, sTotDur, mediaInfo.FilePath, vf, outputPath);
                }
                else
                {
                    string vf = anim;
                    string audioArgs = "";
                    if (mediaInfo.HasAudio)
                    {
                        audioArgs = "-c:a aac -b:a 192k";
                    }
                    arguments = string.Format(CultureInfo.InvariantCulture,
                        "-y -i \"{0}\" -vf \"{1}\" {2} -c:v libx264 -preset fast -crf 16 -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 -pix_fmt yuv420p \"{3}\"",
                        mediaInfo.FilePath, vf, audioArgs, outputPath);
                }
            }
            else
            {
                // Mask-based transitions: Brush Stroke, Watercolor, Smooth Fade
                string maskInPath = MaskGenerator.EnsureMask(ffmpegPath, masksCacheDir, style, targetWidth, targetHeight, mediaInfo.Fps, inDuration, false);
                string maskOutPath = MaskGenerator.EnsureMask(ffmpegPath, masksCacheDir, style, targetWidth, targetHeight, mediaInfo.Fps, outDuration, true);

                string inputArgs = "";
                string filterComplex = "";
                string mapArgs = "-map \"[outv]\"";

                bool useBgImage = !string.IsNullOrEmpty(bgImagePath) && File.Exists(bgImagePath);
                string bgSeg = useBgImage
                    ? string.Format(CultureInfo.InvariantCulture,
                        "[3:v]scale={0}:{1}:force_original_aspect_ratio=decrease:out_color_matrix=bt709,pad={0}:{1}:(ow-iw)/2:(oh-ih)/2,setsar=1,format=gbrp[bg];",
                        targetWidth, targetHeight)
                    : "";
                string bgColorSeg = !useBgImage
                    ? string.Format(CultureInfo.InvariantCulture,
                        "color=c={0}:s={1}x{2}:d={3}:r={4},format=gbrp[bg];",
                        bgColor, targetWidth, targetHeight, sTotDur, sFps)
                    : "";
                string bgInputArgs = useBgImage
                    ? string.Format(CultureInfo.InvariantCulture,
                        " -loop 1 -framerate {0} -t {1} -i \"{2}\"",
                        sFps, sTotDur, bgImagePath)
                    : "";

                if (mediaInfo.IsImage)
                {
                    inputArgs = string.Format(CultureInfo.InvariantCulture,
                        "-loop 1 -framerate {0} -t {1} -i \"{2}\" -i \"{3}\" -i \"{4}\"",
                        sFps, sTotDur, mediaInfo.FilePath, maskInPath, maskOutPath
                    ) + bgInputArgs;

                    filterComplex = string.Format(
                        CultureInfo.InvariantCulture,
                        "[0:v]" + anim + ",format=gbrp[src];" +
                        "[1:v]trim=0:{0},setpts=PTS-STARTPTS,format=gbrp[min];" +
                        "color=c=white:s={1}x{2}:d={3}:r={4},format=gbrp[mmid];" +
                        "[2:v]trim=0:{5},setpts=PTS-STARTPTS,format=gbrp[mout];" +
                        "[min][mmid][mout]concat=n=3:v=1:a=0,format=gbrp[m];" +
                        bgSeg + bgColorSeg +
                        "[bg][src][m]maskedmerge,format=yuv420p[outv]",
                        sInDur, targetWidth, targetHeight, sMidDur, sFps, sOutDur
                    );
                }
                else
                {
                    inputArgs = string.Format(CultureInfo.InvariantCulture,
                        "-i \"{0}\" -i \"{1}\" -i \"{2}\"",
                        mediaInfo.FilePath, maskInPath, maskOutPath
                    ) + bgInputArgs;

                    filterComplex = string.Format(
                        CultureInfo.InvariantCulture,
                        "[0:v]" + anim + ",format=gbrp[src];" +
                        "[1:v]trim=0:{0},setpts=PTS-STARTPTS,format=gbrp[min];" +
                        "color=c=white:s={1}x{2}:d={3}:r={4},format=gbrp[mmid];" +
                        "[2:v]trim=0:{5},setpts=PTS-STARTPTS,format=gbrp[mout];" +
                        "[min][mmid][mout]concat=n=3:v=1:a=0,format=gbrp[m];" +
                        bgSeg + bgColorSeg +
                        "[bg][src][m]maskedmerge,format=yuv420p[outv]",
                        sInDur, targetWidth, targetHeight, sMidDur, sFps, sOutDur
                    );

                    if (mediaInfo.HasAudio)
                    {
                        string audioFilter = string.Format(CultureInfo.InvariantCulture,
                            ";[0:a]afade=t=in:ss=0:d={0},afade=t=out:st={1}:d={2}[outa]",
                            sInDur, sOutStart, sOutDur);
                        filterComplex += audioFilter;
                        mapArgs += " -map \"[outa]\" -c:a aac -b:a 192k";
                    }
                }

                arguments = string.Format(
                    "-y {0} -filter_complex \"{1}\" {2} -c:v libx264 -preset fast -crf 16 -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 -pix_fmt yuv420p \"{3}\"",
                    inputArgs, filterComplex, mapArgs, outputPath
                );
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.EnableRaisingEvents = true;

                p.ErrorDataReceived += (senderProc, eventArgs) =>
                {
                    if (string.IsNullOrEmpty(eventArgs.Data)) return;

                    Match mTime = Regex.Match(eventArgs.Data, @"time=(\d+):(\d+):(\d+(\.\d+)?)");
                    Match mSpeed = Regex.Match(eventArgs.Data, @"speed=\s*([\d\.]+)x");

                    if (mTime.Success && mediaInfo.DurationSeconds > 0)
                    {
                        int h = int.Parse(mTime.Groups[1].Value);
                        int m = int.Parse(mTime.Groups[2].Value);
                        double secs = double.Parse(mTime.Groups[3].Value, CultureInfo.InvariantCulture);
                        double curSec = h * 3600 + m * 60 + secs;
                        double pct = Math.Min(100.0, (curSec / mediaInfo.DurationSeconds) * 100.0);
                        string speedStr = mSpeed.Success ? mSpeed.Groups[1].Value + "x" : "";

                        if (onProgress != null)
                        {
                            onProgress(new ProcessProgressEventArgs
                            {
                                Percent = pct,
                                CurrentTime = string.Format("{0:0.0}s / {1:0.0}s", curSec, mediaInfo.DurationSeconds),
                                Speed = speedStr,
                                RawLine = eventArgs.Data
                            });
                        }
                    }
                };

                p.Start();
                p.BeginErrorReadLine();

                while (!p.HasExited)
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        try { p.Kill(); } catch { }
                        onLog("Dibatalkan.");
                        return false;
                    }
                    Thread.Sleep(100);
                }

                p.WaitForExit();
                return p.ExitCode == 0;
            }
        }
    }
}
