using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KksSceneConv
{
    /// <summary>CLI output: console when a console is attached.</summary>
    static class Cli
    {
        public static void W(string s)
        {
            try { Console.WriteLine(s); } catch { }
        }
    }

    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")]
        static extern uint GetFileType(IntPtr h);
        const int ATTACH_PARENT_PROCESS = -1;
        const int STD_OUTPUT_HANDLE = -11;

        /// <summary>true when stdout was handed to us (redirected to a file / pipe);
        /// attaching a console then would steal the output from the redirection.</summary>
        static bool HasStdout()
        {
            var h = GetStdHandle(STD_OUTPUT_HANDLE);
            return h != IntPtr.Zero && h != new IntPtr(-1) && GetFileType(h) != 0;
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && IsCommand(args[0]))
            {
                if (!HasStdout() && !AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
                HookConsole();
                return RunCli(args);
            }
            // Not a command -> GUI. Paths dropped onto the exe icon / "Open with" arrive here.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            L.Load();
            try { Application.Run(new MainForm(args.Length > 0 ? args : null)); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "KksSceneConv", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
            return 0;
        }

        /// <summary>A WinExe has no console of its own; after AttachConsole the stdout
        /// handle may still be invalid and Console.WriteLine silently drops output.
        /// Re-wrapping Console.OpenStandardOutput() fixes that.</summary>
        static void HookConsole()
        {
            try
            {
                var so = Console.OpenStandardOutput();
                var sw = new StreamWriter(so, new UTF8Encoding(false)) { AutoFlush = true };
                Console.SetOut(sw);
                var se = Console.OpenStandardError();
                var ew = new StreamWriter(se, new UTF8Encoding(false)) { AutoFlush = true };
                Console.SetError(ew);
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            }
            catch { }
        }

        static bool IsCommand(string a)
        {
            switch (a.ToLowerInvariant())
            {
                case "convert": case "check": case "batch": case "help": case "-h": case "--help": case "/?":
                    return true;
            }
            return false;
        }

        static void Help()
        {
            Cli.W("KksSceneConv - down-convert Koikatsu Sunshine (KKS) Studio scenes for Koikatsu (KK)");
            Cli.W("");
            Cli.W("  KksSceneConv.exe                                   open the GUI");
            Cli.W("  KksSceneConv.exe <scene.png> ...                   open the GUI with the files preloaded");
            Cli.W("  KksSceneConv.exe convert <in.png> [<out.png>] [-v] convert (default out: <in>_kk.png)");
            Cli.W("  KksSceneConv.exe check <scene.png>                 parse only, print structure (KK or KKS)");
            Cli.W("  KksSceneConv.exe batch <dir> [<outdir>] [-r] [-v]  convert every *.png (-r: subfolders)");
            Cli.W("                                                     non-scene .png files (pictures, cards) are skipped");
            Cli.W("  KksSceneConv.exe help");
            Cli.W("");
            Cli.W("Exit codes: 0 ok, 1 failed / NG, 2 usage.");
        }

        static int RunCli(string[] args)
        {
            string cmd = args[0].ToLowerInvariant();
            var pos = new List<string>();
            bool verbose = false, recurse = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "-v" || args[i] == "--verbose") verbose = true;
                else if (args[i] == "-r" || args[i] == "--recurse") recurse = true;
                else pos.Add(args[i]);
            }
            Action<string> log = verbose ? (Action<string>)Cli.W : (s => { });
            try
            {
                switch (cmd)
                {
                    case "help": case "-h": case "--help": case "/?":
                        Help();
                        return 0;
                    case "check":
                        {
                            if (pos.Count < 1) { Help(); return 2; }
                            return Check(pos[0]);
                        }
                    case "convert":
                        {
                            if (pos.Count < 1) { Help(); return 2; }
                            string src = pos[0];
                            string dst = pos.Count > 1 ? pos[1] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(src)), Path.GetFileNameWithoutExtension(src) + "_kk.png");
                            ConvertFile(src, dst, log);
                            return 0;
                        }
                    case "batch":
                        {
                            if (pos.Count < 1) { Help(); return 2; }
                            string dir = pos[0];
                            string outdir = pos.Count > 1 ? pos[1] : dir;
                            Directory.CreateDirectory(outdir);
                            int failed = 0;
                            foreach (var job in Jobs.Build(dir, outdir, "_kk", recurse))
                            {
                                try { ConvertFile(job.Key, job.Value, log); }
                                catch (NotASceneException e) { Cli.W("SKIP " + Path.GetFileName(job.Key) + ": " + e.Message); }
                                catch (Exception e) { failed++; Cli.W("FAILED " + Path.GetFileName(job.Key) + ": " + e.Message); }
                            }
                            return failed == 0 ? 0 : 1;
                        }
                }
                Help();
                return 2;
            }
            catch (Exception e)
            {
                Cli.W("ERROR: " + e.Message);
                return 1;
            }
        }

        public static byte[] ConvertFile(string src, string dst, Action<string> log)
        {
            var data = File.ReadAllBytes(src);
            var t = new Transcoder(data, log);
            var outp = t.Scene(true);
            WriteFileAtomic(dst, outp);
            Cli.W(Transcoder.SummaryLine(Path.GetFileName(src), Path.GetFileName(dst), data.Length, outp.Length, t.Stats));
            return outp;
        }

        /// <summary>Write to a temp file, then rename over <paramref name="path"/>, so an
        /// interrupted write never leaves a truncated scene behind.</summary>
        public static void WriteFileAtomic(string path, byte[] data)
        {
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string tmp = full + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, data);
                File.Move(tmp, full, true);
            }
            catch
            {
                try { File.Delete(tmp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        /// <summary>Parse a KK or KKS scene without writing; prove the stream stays in
        /// sync all the way to the ExtensibleSaveFormat tail marker.</summary>
        static int Check(string path)
        {
            var data = File.ReadAllBytes(path);
            var t = new Transcoder(data, Cli.W);
            t.Scene(false);
            Cli.W("stats: " + t.Stats);
            if (t.TailMarkFound != Transcoder.TailMark)
            {
                Cli.W("NG: tail marker " + (t.TailMarkFound ?? "null") + " not found where expected -> layout desync");
                return 1;
            }
            Cli.W("OK: parsed cleanly up to \"" + Transcoder.TailMark + "\" (" + t.TailLength + " tail bytes incl. KKEx)");
            return 0;
        }
    }

    /// <summary>Builds (src, dst) pairs for batch conversion.</summary>
    static class Jobs
    {
        public static List<KeyValuePair<string, string>> Build(string dir, string outdir, string suffix, bool recurse)
        {
            var jobs = new List<KeyValuePair<string, string>>();
            string dirFull = Path.GetFullPath(dir).TrimEnd('\\', '/');
            string outFull = Path.GetFullPath(outdir).TrimEnd('\\', '/');
            bool outInside = outFull.Length > dirFull.Length
                && outFull.StartsWith(dirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            string skipTail = suffix.Length > 0 ? suffix + ".png" : null;
            var files = new List<string>(Directory.GetFiles(dirFull, "*.png", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                string name = Path.GetFileName(f);
                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                if (skipTail != null && name.EndsWith(skipTail, StringComparison.OrdinalIgnoreCase)) continue;
                if (outInside && f.StartsWith(outFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                string rel = Path.GetRelativePath(dirFull, Path.GetDirectoryName(f));
                string dstDir = rel == "." ? outFull : Path.Combine(outFull, rel);
                string dst = Path.Combine(dstDir, Path.GetFileNameWithoutExtension(f) + suffix + ".png");
                jobs.Add(new KeyValuePair<string, string>(f, dst));
            }
            return jobs;
        }
    }
}
