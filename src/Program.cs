using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VideoTransitionBatchApp
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && HasCliArgs(args))
            {
                RunCliMode(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        static bool HasCliArgs(string[] args)
        {
            foreach (string a in args)
            {
                if (a.StartsWith("-") || a.StartsWith("/")) return true;
            }
            return false;
        }

        static void RunCliMode(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("Video Transition Studio (CLI Mode)");
            Console.WriteLine("=================================================");

            string inputPath = null;
            string outputPath = null;
            TransitionStyle style = TransitionStyle.BrushStroke;
            KenBurnsMode animation = KenBurnsMode.Off;
            double inDur = 1.2;
            double outDur = 1.2;
            double imgDur = 7.0;
            string bgColor = "black";
            string bgImage = null;
            string resolution = "1080p";
            double zoomPct = 12.0;
            string ffmpegPath = VideoProcessor.FindFFmpeg();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if ((arg == "-i" || arg == "--input") && i + 1 < args.Length)
                {
                    inputPath = args[++i];
                }
                else if ((arg == "-o" || arg == "--output") && i + 1 < args.Length)
                {
                    outputPath = args[++i];
                }
                else if ((arg == "-t" || arg == "--transition") && i + 1 < args.Length)
                {
                    string t = args[++i].ToLowerInvariant();
                    if (t.Contains("brush")) style = TransitionStyle.BrushStroke;
                    else if (t.Contains("water") || t.Contains("bleed") || t.Contains("splash")) style = TransitionStyle.Watercolor;
                    else if (t.Contains("wipe") || t.Contains("reveal") || t.Contains("cover") || t.Contains("uncover")) style = TransitionStyle.Wipe;
                    else if (t.Contains("split")) style = TransitionStyle.Split;
                    else if (t.Contains("dissolve")) style = TransitionStyle.Dissolve;
                    else if (t.Contains("checker")) style = TransitionStyle.Checkerboard;
                    else if (t.Contains("blind")) style = TransitionStyle.Blinds;
                    else if (t.Contains("clock")) style = TransitionStyle.Clock;
                    else if (t.Contains("fade")) style = TransitionStyle.SmoothFade;
                    else if (t == "none" || t == "cut") style = TransitionStyle.None;
                    else if (t == "zoomin" || t == "zoom-in") { style = TransitionStyle.None; animation = KenBurnsMode.ZoomIn; }
                    else if (t == "zoomout" || t == "zoom-out") { style = TransitionStyle.None; animation = KenBurnsMode.ZoomOut; }
                }
                else if ((arg == "--kenburns" || arg == "--animation") && i + 1 < args.Length)
                {
                    string k = args[++i].ToLowerInvariant();
                    if (k == "zoomin" || k == "zoom-in" || k == "in") animation = KenBurnsMode.ZoomIn;
                    else if (k == "zoomout" || k == "zoom-out" || k == "out") animation = KenBurnsMode.ZoomOut;
                    else animation = KenBurnsMode.Off;
                }
                else if (arg == "--in" && i + 1 < args.Length)
                {
                    double.TryParse(args[++i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out inDur);
                }
                else if (arg == "--out" && i + 1 < args.Length)
                {
                    double.TryParse(args[++i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out outDur);
                }
                else if ((arg == "--img-dur" || arg == "--image-dur") && i + 1 < args.Length)
                {
                    double.TryParse(args[++i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out imgDur);
                }
                else if (arg == "--bg" && i + 1 < args.Length)
                {
                    bgColor = args[++i];
                }
                else if (arg == "--bg-image" && i + 1 < args.Length)
                {
                    bgImage = args[++i];
                }
                else if ((arg == "--resolution" || arg == "--res") && i + 1 < args.Length)
                {
                    resolution = args[++i].ToLowerInvariant();
                }
                else if ((arg == "--zoom-pct" || arg == "--zoom") && i + 1 < args.Length)
                {
                    double.TryParse(args[++i], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out zoomPct);
                }
                else if (arg == "--ffmpeg" && i + 1 < args.Length)
                {
                    ffmpegPath = args[++i];
                }
                else if (arg == "-h" || arg == "--help")
                {
                    PrintHelp();
                    return;
                }
            }

            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                Console.WriteLine("Error: ffmpeg tidak ditemukan.");
                return;
            }

            if (string.IsNullOrEmpty(inputPath))
            {
                inputPath = @"G:\Downloads\Jadwal Ibadah 2026";
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Directory.Exists(inputPath)
                    ? Path.Combine(inputPath, "Output_Transitions_" + style.ToString())
                    : Path.Combine(Path.GetDirectoryName(inputPath), "Output_Transitions_" + style.ToString());
            }

            Directory.CreateDirectory(outputPath);

            try
            {
                if (Directory.Exists(inputPath) &&
                    Path.GetFullPath(outputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Equals(Path.GetFullPath(inputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Error: Folder output tidak boleh sama dengan folder input.");
                    return;
                }
                if (File.Exists(inputPath))
                {
                    string srcDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
                    if (srcDir != null &&
                        Path.GetFullPath(outputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Equals(srcDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Error: Folder output tidak boleh sama dengan folder input.");
                        return;
                    }
                }
            }
            catch
            {
            }

            List<string> filesToProcess = new List<string>();
            if (Directory.Exists(inputPath))
            {
                foreach (string ext in VideoProcessor.VideoExtensions)
                {
                    filesToProcess.AddRange(Directory.GetFiles(inputPath, "*" + ext));
                }
                foreach (string ext in VideoProcessor.ImageExtensions)
                {
                    filesToProcess.AddRange(Directory.GetFiles(inputPath, "*" + ext));
                }
            }
            else if (File.Exists(inputPath))
            {
                filesToProcess.Add(inputPath);
            }
            else
            {
                Console.WriteLine("Error: Input tidak valid: " + inputPath);
                return;
            }

            Console.WriteLine(string.Format("Memproses {0} file...", filesToProcess.Count));
            Console.WriteLine(string.Format("Transisi: {0} | Ken Burns: {1} {2}% | In: {3}s | Out: {4}s | Durasi Gambar: {5}s | Latar: {6} | Res: {7}", 
                style, animation, zoomPct, inDur, outDur, imgDur, bgColor, resolution));
            Console.WriteLine("Output: " + outputPath);
            Console.WriteLine("-------------------------------------------------");

            string masksDir = VideoProcessor.GetWritableMasksCacheDir();
            try { Directory.CreateDirectory(masksDir); } catch { }

            int done = 0;
            int failed = 0;
            CancellationTokenSource cts = new CancellationTokenSource();

            for (int i = 0; i < filesToProcess.Count; i++)
            {
                string src = filesToProcess[i];
                string name = Path.GetFileName(src);
                string dst = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(src) + ".mp4");

                Console.Write(string.Format("[{0}/{1}] {2} ... ", i + 1, filesToProcess.Count, name));

                MediaInfo mInfo = VideoProcessor.ProbeMedia(ffmpegPath, src, imgDur, style, inDur, outDur);
                if (resolution == "720p")
                {
                    mInfo.OutputWidth = 1280;
                    mInfo.OutputHeight = 720;
                }
                else
                {
                    mInfo.OutputWidth = 1920;
                    mInfo.OutputHeight = 1080;
                }
                mInfo.ZoomPercent = zoomPct;
                mInfo.Animation = animation;
                bool ok = VideoProcessor.ProcessMedia(
                    ffmpegPath,
                    masksDir,
                    mInfo,
                    dst,
                    bgColor,
                    bgImage,
                    (p) => { },
                    cts.Token,
                    (log) => { }
                );

                if (ok && File.Exists(dst))
                {
                    done++;
                    Console.WriteLine("OK");
                }
                else
                {
                    failed++;
                    Console.WriteLine("GAGAL");
                }
            }

            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine(string.Format("Selesai: {0} berhasil, {1} gagal.", done, failed));
        }

        static void PrintHelp()
        {
            Console.WriteLine("Penggunaan:");
            Console.WriteLine("  VideoTransitionStudio.exe [opsi]");
            Console.WriteLine("Opsi:");
            Console.WriteLine("  -i, --input <path>         Folder atau file");
            Console.WriteLine("  -o, --output <path>        Folder tujuan");
            Console.WriteLine("  -t, --transition <tipe>    brush, watercolor, fade, wipe, split, dissolve, checkerboard, blinds, clock, none");
            Console.WriteLine("  --kenburns <off/zoomin/zoomout>  Animasi Zoom (terpisah dari transisi)");
            Console.WriteLine("  --in <detik>               Durasi transisi masuk");
            Console.WriteLine("  --out <detik>              Durasi transisi keluar");
            Console.WriteLine("  --img-dur <detik>          Durasi video gambar");
            Console.WriteLine("  --bg <color>               Warna latar (black, white, #hex)");
            Console.WriteLine("  --bg-image <path>          Gambar latar (mengalahkan --bg)");
            Console.WriteLine("  --resolution <720p/1080p>  Resolusi output (default 1080p)");
            Console.WriteLine("  --zoom-pct <0-30>          Rasio Ken Burns persen (default 12)");
            Console.WriteLine("  --ffmpeg <path>            Lokasi ffmpeg.exe");
            Console.WriteLine("  -h, --help                 Bantuan");
        }
    }
}
