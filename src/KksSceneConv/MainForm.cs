using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KksSceneConv
{
    public sealed class MainForm : Form
    {
        // ---- controls -----------------------------------------------------
        readonly RadioButton rbFile = new RadioButton { AutoSize = true, Checked = true };
        readonly RadioButton rbDir = new RadioButton { AutoSize = true };
        readonly TextBox txtIn = new TextBox { Dock = DockStyle.Fill };
        readonly Button btnBrowseIn = new Button { AutoSize = true };
        readonly Label dropZone = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 247, 250), ForeColor = Color.FromArgb(80, 80, 90),
        };
        readonly TextBox txtOut = new TextBox { Dock = DockStyle.Fill };
        readonly Button btnBrowseOut = new Button { AutoSize = true };
        /// <summary>Output name suffix, fixed: "#01.png" -> "#01_kk.png". The CLI uses the
        /// same value and batch runs skip files that already carry it.</summary>
        const string Suffix = "_kk";
        readonly CheckBox chkRecurse = new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(12, 4, 3, 0) };
        readonly CheckBox chkOverwrite = new CheckBox { AutoSize = true, Margin = new Padding(12, 4, 3, 0) };
        readonly CheckBox chkVerify = new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(12, 4, 3, 0) };
        readonly CheckBox chkVerbose = new CheckBox { AutoSize = true, Margin = new Padding(12, 4, 3, 0) };
        readonly PictureBox pic = new PictureBox
        {
            Width = 176, Height = 99, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(235, 235, 238), Margin = new Padding(3, 3, 10, 3),
        };
        readonly TextBox lblInfo = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Control, TabStop = false,
        };
        readonly Button btnRun = new Button { AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        readonly Button btnCheck = new Button { AutoSize = true };
        readonly Button btnCancel = new Button { AutoSize = true, Enabled = false };
        readonly Button btnOpenOut = new Button { AutoSize = true };
        readonly CheckBox btnLog = new CheckBox { Appearance = Appearance.Button, AutoSize = true, Checked = true };
        readonly ProgressBar pb = new ProgressBar { Dock = DockStyle.Fill, Height = 18 };
        readonly Label lblStatus = new Label { AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
        readonly RichTextBox log = new RichTextBox
        {
            ReadOnly = true, Dock = DockStyle.Fill, WordWrap = false, ScrollBars = RichTextBoxScrollBars.Both,
            Font = new Font("Consolas", 8.5f), BackColor = Color.White, DetectUrls = false,
        };
        readonly ComboBox cbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 96 };
        readonly Label lblLang = new Label { AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
        readonly GroupBox grpIn = new GroupBox { Dock = DockStyle.Fill };
        readonly GroupBox grpOut = new GroupBox { Dock = DockStyle.Fill };
        readonly GroupBox grpInfo = new GroupBox { Dock = DockStyle.Fill };
        readonly TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };

        readonly Dictionary<Control, string> loc = new Dictionary<Control, string>();
        readonly List<string> inputs = new List<string>();  // explicit file list (multi-drop)
        CancellationTokenSource cts;
        bool busy;
        bool closeAfterRun;
        int analyzeGen;  // bumped on every input change; stale analyses compare and bail out
        // Output folder policy: default = same folder as the source (the _kk suffix already
        // tells the files apart). Once the user picks a folder, or one was remembered from a
        // previous session, it sticks and is no longer overwritten when the input changes.
        // In single-file mode the field holds the full output file path (…\#01_kk.png);
        // in folder / multi-file mode it holds the output folder.
        string outDirOverride;  // folder the user chose (Browse… / typed) or remembered; null = source folder
        bool settingOut;        // true while the code (not the user) writes txtOut
        bool outEdited;         // user typed into txtOut since the last programmatic set

        public MainForm(string[] preload)
        {
            Text = "KKS → KK Scene Converter";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            MinimumSize = new Size(640, 560);
            Size = new Size(760, 680);
            StartPosition = FormStartPosition.CenterScreen;

            BuildLayout();
            HookEvents();
            ApplyLang();
            SetLogVisible(true);
            EnableDrop(this);
            UpdateModeUi();

            outDirOverride = Settings.LoadLastOutputDir();

            if (preload != null && preload.Length > 0)
                Load += (s, e) => SetInputs(preload);
        }

        // ---- layout -------------------------------------------------------
        void Loc(Control c, string key) { loc[c] = key; }

        void BuildLayout()
        {
            // top bar
            var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            cbLang.Items.AddRange(L.LangNames);
            top.Controls.Add(cbLang);
            top.Controls.Add(lblLang);
            Loc(lblLang, "Language");

            // input group
            Loc(grpIn, "Input");
            var tin = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            tin.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tin.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tin.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tin.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tin.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var modes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            modes.Controls.Add(rbFile); modes.Controls.Add(rbDir);
            Loc(rbFile, "Single scene file"); Loc(rbDir, "Whole folder");
            tin.Controls.Add(modes, 0, 0); tin.SetColumnSpan(modes, 2);
            tin.Controls.Add(txtIn, 0, 1);
            tin.Controls.Add(btnBrowseIn, 1, 1);
            Loc(btnBrowseIn, "Browse…");
            tin.Controls.Add(dropZone, 0, 2); tin.SetColumnSpan(dropZone, 2);
            Loc(dropZone, "Drop a KKS Studio scene (.png), several scenes, or a folder here");
            grpIn.Controls.Add(tin);

            // output group
            Loc(grpOut, "Output");
            var tout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            tout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tout.Controls.Add(txtOut, 0, 0);
            tout.Controls.Add(btnBrowseOut, 1, 0);
            Loc(btnBrowseOut, "Browse…");
            var opts = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            opts.Controls.Add(chkRecurse); opts.Controls.Add(chkOverwrite); opts.Controls.Add(chkVerify); opts.Controls.Add(chkVerbose);
            Loc(chkRecurse, "Include subfolders"); Loc(chkOverwrite, "Overwrite existing");
            Loc(chkVerify, "Verify output"); Loc(chkVerbose, "Verbose log");
            tout.Controls.Add(opts, 0, 1); tout.SetColumnSpan(opts, 2);
            grpOut.Controls.Add(tout);

            // analysis group
            Loc(grpInfo, "Analysis");
            var tinfo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            tinfo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tinfo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tinfo.Controls.Add(pic, 0, 0);
            tinfo.Controls.Add(lblInfo, 1, 0);
            grpInfo.Controls.Add(tinfo);

            // buttons
            var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            btns.Controls.Add(btnRun); btns.Controls.Add(btnCheck); btns.Controls.Add(btnCancel);
            btns.Controls.Add(btnOpenOut); btns.Controls.Add(btnLog);
            Loc(btnRun, "▶  Convert"); Loc(btnCheck, "Check only"); Loc(btnCancel, "Cancel");
            Loc(btnOpenOut, "Open output folder");
            btnRun.Font = new Font(Font, FontStyle.Bold);

            // root
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // top
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); // input
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));  // output
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132)); // analysis
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // log
            root.Controls.Add(top, 0, 0);
            root.Controls.Add(grpIn, 0, 1);
            root.Controls.Add(grpOut, 0, 2);
            root.Controls.Add(grpInfo, 0, 3);
            root.Controls.Add(btns, 0, 4);
            root.Controls.Add(pb, 0, 5);
            root.Controls.Add(lblStatus, 0, 6);
            root.Controls.Add(log, 0, 7);
            Controls.Add(root);
        }

        void HookEvents()
        {
            cbLang.SelectedIndex = Math.Max(0, Array.IndexOf(L.Langs, L.Lang));
            cbLang.SelectedIndexChanged += (s, e) =>
            {
                L.Lang = L.Langs[cbLang.SelectedIndex];
                L.Save();
                ApplyLang();
            };
            rbFile.CheckedChanged += (s, e) => UpdateModeUi();
            rbDir.CheckedChanged += (s, e) => UpdateModeUi();
            btnBrowseIn.Click += (s, e) => BrowseIn();
            btnBrowseOut.Click += (s, e) => BrowseOut();
            txtIn.Leave += (s, e) => { if (inputs.Count <= 1) { inputs.Clear(); if (txtIn.Text.Length > 0) SetInputs(new[] { txtIn.Text }); } };
            txtOut.TextChanged += (s, e) => { if (!settingOut) outEdited = true; };
            txtOut.Leave += (s, e) =>
            {
                if (!outEdited) return;
                outEdited = false;
                string o = txtOut.Text.Trim();
                if (o.Length == 0) return;
                string dir = SingleFileMode && !Directory.Exists(o) ? Path.GetDirectoryName(o) : o;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) UserChoseOutDir(dir);
            };
            btnRun.Click += (s, e) => Run(false);
            btnCheck.Click += (s, e) => Run(true);
            btnCancel.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            btnOpenOut.Click += (s, e) => OpenOut();
            btnLog.CheckedChanged += (s, e) => SetLogVisible(btnLog.Checked);
        }

        void ApplyLang()
        {
            Text = L.T("KKS → KK Scene Converter");
            foreach (var kv in loc) kv.Key.Text = L.T(kv.Value);
            btnLog.Text = L.T(btnLog.Checked ? "Hide log" : "Show log");
            if (!busy) lblStatus.Text = L.T("Ready. Drop a scene (or click Browse…), then press ▶ Convert.");
            if (inputs.Count > 1) txtIn.Text = inputs.Count + " " + L.T("files selected");
        }

        void SetLogVisible(bool v)
        {
            log.Visible = v;
            root.RowStyles[7] = v ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, 0);
            btnLog.Text = L.T(v ? "Hide log" : "Show log");
        }

        void UpdateModeUi()
        {
            chkRecurse.Enabled = rbDir.Checked;
        }

        // ---- drag & drop --------------------------------------------------
        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) =>
            {
                if (!busy && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
                else e.Effect = DragDropEffects.None;
            };
            c.DragDrop += (s, e) =>
            {
                if (busy || e.Data == null) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) SetInputs(files);
            };
            var rtb = c as RichTextBox;
            if (rtb != null) rtb.EnableAutoDragDrop = false;
            foreach (Control ch in c.Controls) EnableDrop(ch);
        }

        // ---- input handling -----------------------------------------------
        void SetInputs(string[] paths)
        {
            analyzeGen++;
            inputs.Clear();
            var files = new List<string>();
            string dir = null;
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) { if (dir == null) dir = p; }
                else if (File.Exists(p)) files.Add(p);
            }
            if (dir != null && files.Count == 0)
            {
                rbDir.Checked = true;
                txtIn.Text = dir;
                RefreshOutDefault();
                AnalyzeFolder(dir);
                return;
            }
            if (files.Count == 0) return;
            rbFile.Checked = true;
            inputs.AddRange(files);
            if (files.Count == 1)
            {
                txtIn.Text = files[0];
                inputs.Clear();
                RefreshOutDefault();
                AnalyzeFileAsync(files[0]);
            }
            else
            {
                txtIn.Text = files.Count + " " + L.T("files selected");
                RefreshOutDefault();
                SetPreview(null);
                lblInfo.Text = string.Join(Environment.NewLine, files.ConvertAll(Path.GetFileName));
            }
        }

        void SetOutText(string text)
        {
            settingOut = true;
            try { txtOut.Text = text; outEdited = false; }
            finally { settingOut = false; }
        }

        /// <summary>True when exactly one scene file is selected: the output field then
        /// shows the output file path rather than a folder.</summary>
        bool SingleFileMode
        {
            get { return rbFile.Checked && inputs.Count == 0 && File.Exists(txtIn.Text); }
        }

        /// <summary>Default output folder = the source's own folder, unless the user chose
        /// (or a previous session remembered) one. Single file: full path of the output
        /// file ("#01.png" -> "#01_kk.png"); folder / multi-file: the folder.</summary>
        void RefreshOutDefault()
        {
            string srcDir = null;
            if (rbDir.Checked) srcDir = Directory.Exists(txtIn.Text) ? txtIn.Text : null;
            else if (inputs.Count > 0) srcDir = Path.GetDirectoryName(inputs[0]);
            else if (File.Exists(txtIn.Text)) srcDir = Path.GetDirectoryName(txtIn.Text);
            if (string.IsNullOrEmpty(srcDir)) return;
            string dir = outDirOverride ?? srcDir;
            if (SingleFileMode)
                SetOutText(Path.Combine(dir, Path.GetFileNameWithoutExtension(txtIn.Text) + Suffix + ".png"));
            else
                SetOutText(dir);
        }

        void UserChoseOutDir(string dir)
        {
            outDirOverride = dir;
            Settings.SaveLastOutputDir(dir);
        }

        void BrowseIn()
        {
            if (rbDir.Checked)
            {
                using (var d = new FolderBrowserDialog { Description = L.T("Select a folder of KKS scenes"), UseDescriptionForTitle = true })
                {
                    if (Directory.Exists(txtIn.Text)) d.SelectedPath = txtIn.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) SetInputs(new[] { d.SelectedPath });
                }
            }
            else
            {
                using (var d = new OpenFileDialog { Title = L.T("Select a KKS scene"), Filter = "Studio scene (*.png)|*.png|All files|*.*", Multiselect = true })
                {
                    if (d.ShowDialog(this) == DialogResult.OK) SetInputs(d.FileNames);
                }
            }
        }

        void BrowseOut()
        {
            if (SingleFileMode)
            {
                string cur = txtOut.Text.Trim();
                using (var d = new SaveFileDialog
                {
                    Title = L.T("Select the output file"), Filter = "Studio scene (*.png)|*.png|All files|*.*",
                    DefaultExt = "png", AddExtension = true, OverwritePrompt = false,  // overwrite is handled by the checkbox at run time
                })
                {
                    string curDir = null;
                    try { curDir = Path.GetDirectoryName(cur); } catch { }
                    if (!string.IsNullOrEmpty(curDir) && Directory.Exists(curDir)) d.InitialDirectory = curDir;
                    d.FileName = cur.Length > 0 ? Path.GetFileName(cur) : Path.GetFileNameWithoutExtension(txtIn.Text) + Suffix + ".png";
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        SetOutText(d.FileName);
                        UserChoseOutDir(Path.GetDirectoryName(d.FileName));
                    }
                }
                return;
            }
            using (var d = new FolderBrowserDialog { Description = L.T("Select the output folder"), UseDescriptionForTitle = true })
            {
                if (Directory.Exists(txtOut.Text)) d.SelectedPath = txtOut.Text;
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    SetOutText(d.SelectedPath);
                    UserChoseOutDir(d.SelectedPath);
                }
            }
        }

        void OpenOut()
        {
            string o = txtOut.Text.Trim();
            if (string.IsNullOrWhiteSpace(o)) return;
            try
            {
                if (SingleFileMode && !Directory.Exists(o)) o = Path.GetDirectoryName(o);  // field holds a file path
                if (string.IsNullOrEmpty(o)) return;
                if (!Directory.Exists(o)) Directory.CreateDirectory(o);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + o + "\"") { UseShellExecute = true });
            }
            catch (Exception e) { AppendLog("FAILED explorer: " + e.Message); }
        }

        // ---- analysis -----------------------------------------------------
        void SetPreview(Image img)
        {
            var old = pic.Image;
            pic.Image = img;
            if (old != null) old.Dispose();
        }

        void AnalyzeFolder(string dir)
        {
            SetPreview(null);
            try
            {
                var jobs = Jobs.Build(dir, txtOut.Text, Suffix, chkRecurse.Checked);
                lblInfo.Text = L.T("Folder") + ": " + dir + Environment.NewLine + jobs.Count + " " + L.T("scenes to convert");
            }
            catch (Exception e) { lblInfo.Text = L.T("Could not analyze") + ": " + e.Message; }
        }

        async void AnalyzeFileAsync(string path)
        {
            int gen = ++analyzeGen;
            lblStatus.Text = L.T("Analyzing…");
            SetPreview(null);
            lblInfo.Text = "";
            try
            {
                var lines = new List<string>();
                Transcoder t = null;
                byte[] data = null;
                await Task.Run(() =>
                {
                    data = File.ReadAllBytes(path);
                    t = new Transcoder(data, s => lines.Add(s));
                    t.Scene(false);
                });
                if (gen != analyzeGen || IsDisposed) return;  // a newer input replaced this one
                if (t.PngLength > 0)
                {
                    try
                    {
                        using (var ms = new MemoryStream(data, 0, t.PngLength))
                        using (var src = Image.FromStream(ms))
                            SetPreview(new Bitmap(src));
                    }
                    // thumbnail is not a decodable image
                    catch (ArgumentException) { SetPreview(null); }
                    catch (ExternalException) { SetPreview(null); }
                }
                lblInfo.Text = Describe(t);
                if (chkVerbose.Checked) foreach (var l in lines) AppendLog(l);
                lblStatus.Text = L.T("Ready. Drop a scene (or click Browse…), then press ▶ Convert.");
            }
            catch (NotASceneException e)
            {
                if (gen != analyzeGen || IsDisposed) return;
                lblInfo.Text = L.T("This file is not a Studio scene.") + Environment.NewLine + e.Message;
                lblStatus.Text = L.T("Not a Studio scene") + ": " + Path.GetFileName(path);
            }
            catch (Exception e)
            {
                if (gen != analyzeGen || IsDisposed) return;
                lblInfo.Text = L.T("Could not analyze") + ": " + e.Message;
                lblStatus.Text = L.T("Could not analyze") + ": " + Path.GetFileName(path);
            }
        }

        static string Describe(Transcoder t)
        {
            var sb = new StringBuilder();
            var s = t.Stats;
            if (!t.IsKks)
            {
                sb.AppendLine(L.T("This scene is already in KK format. No conversion needed."));
                sb.AppendLine(L.T("Scene version") + ": " + t.SrcVersion);
            }
            else
            {
                sb.AppendLine(L.T("Scene version") + ": " + t.SrcVersion + " → " + t.OutVersion + "  (KKS → KK)");
            }
            sb.AppendLine(L.T("Objects") + ": " + (s.KindsText().Length > 0 ? s.KindsText().Replace("=", " ") : "-"));
            if (s.TextsDropped > 0)
                sb.AppendLine(L.T("Text objects") + ": " + s.TextsDropped + " → " + L.T("will be removed (KK has no Text object)"));
            if (s.BlockVersions.Count > 0)
                sb.AppendLine(L.T("Card block versions") + ": " + s.BlockVersionsText());
            if (s.TimelineRenames.Count > 0)
                sb.AppendLine(L.T("Timeline owners") + ": " + string.Join(", ", s.TimelineRenames) + " → KKPE");
            sb.Append(L.T("Tail") + ": ");
            sb.Append(t.TailMarkFound == Transcoder.TailMark
                ? L.T("OK, parsed cleanly up to the KStudio marker") + " (" + t.TailLength + " bytes)"
                : L.T("NG, tail marker not found (layout desync)"));
            return sb.ToString();
        }

        // ---- run ----------------------------------------------------------
        List<KeyValuePair<string, string>> BuildJobs()
        {
            string outDir = txtOut.Text.Trim();
            var jobs = new List<KeyValuePair<string, string>>();
            if (rbDir.Checked)
            {
                if (!Directory.Exists(txtIn.Text)) return jobs;
                return Jobs.Build(txtIn.Text, outDir, Suffix, chkRecurse.Checked);
            }
            var files = inputs.Count > 0 ? new List<string>(inputs) : new List<string>();
            if (files.Count == 0 && File.Exists(txtIn.Text)) files.Add(txtIn.Text);
            if (SingleFileMode && files.Count == 1 && !Directory.Exists(outDir) && !outDir.EndsWith("\\") && !outDir.EndsWith("/"))
            {
                // The field holds the output file path itself.
                jobs.Add(new KeyValuePair<string, string>(files[0], outDir));
                return jobs;
            }
            foreach (var f in files)
                jobs.Add(new KeyValuePair<string, string>(f, Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + Suffix + ".png")));
            return jobs;
        }

        async void Run(bool checkOnly)
        {
            if (busy) return;
            if (!checkOnly && string.IsNullOrWhiteSpace(txtOut.Text))
            {
                MessageBox.Show(this, L.T("Output folder is required."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<KeyValuePair<string, string>> jobs;
            try { jobs = BuildJobs(); }
            catch (Exception e)  // async void: anything escaping here would bypass the UI entirely
            {
                lblStatus.Text = L.T("Could not list input files") + ": " + e.Message;
                return;
            }
            if (jobs.Count == 0)
            {
                lblStatus.Text = L.T(rbDir.Checked && Directory.Exists(txtIn.Text) ? "No .png scenes found." : "No input selected.");
                return;
            }
            if (!checkOnly)
            {
                foreach (var job in jobs)
                {
                    if (string.Equals(Path.GetFullPath(job.Key), Path.GetFullPath(job.Value), StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(this, L.T("Output file must be different from the input file.") + Environment.NewLine + job.Key,
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }
            bool overwrite = chkOverwrite.Checked, verify = chkVerify.Checked, verbose = chkVerbose.Checked;
            SetBusy(true);
            pb.Maximum = jobs.Count; pb.Value = 0;
            cts = new CancellationTokenSource();
            var tok = cts.Token;
            int ok = 0, skipped = 0, failed = 0, done = 0;
            AppendLog("=== " + L.T(checkOnly ? "Checking" : "Converting") + " " + jobs.Count + " file(s) ===");
            try
            {
                await Task.Run(() =>
                {
                    foreach (var job in jobs)
                    {
                        if (tok.IsCancellationRequested) break;
                        string src = job.Key, dst = job.Value;
                        string name = Path.GetFileName(src);
                        Post(() => lblStatus.Text = L.T(checkOnly ? "Checking" : "Converting") + ": " + name);
                        try
                        {
                            if (checkOnly)
                            {
                                var data = File.ReadAllBytes(src);
                                var t = new Transcoder(data, verbose ? (Action<string>)AppendLogBg : null);
                                t.Scene(false);
                                bool good = t.TailMarkFound == Transcoder.TailMark;
                                AppendLogBg((good ? "OK  " : "NG  ") + name + " : version " + t.SrcVersion + (t.IsKks ? " (KKS)" : " (KK)")
                                    + " | " + t.Stats.KindsText() + " | text=" + t.Stats.TextsDropped + " | tail=" + t.TailLength);
                                if (good) ok++; else failed++;
                            }
                            else
                            {
                                if (File.Exists(dst) && !overwrite)
                                {
                                    skipped++;
                                    AppendLogBg("SKIP " + name + " : " + L.T("exists, skipped (enable Overwrite to replace)"));
                                }
                                else
                                {
                                    var data = File.ReadAllBytes(src);
                                    var t = new Transcoder(data, verbose ? (Action<string>)AppendLogBg : null);
                                    byte[] outp;
                                    try { outp = t.Scene(true); }
                                    catch (AlreadyKkException)
                                    {
                                        skipped++;
                                        AppendLogBg("SKIP " + name + " : " + L.T("already KK format, skipped"));
                                        continue;
                                    }
                                    if (verify)
                                    {
                                        var c = new Transcoder(outp);
                                        c.Scene(false);
                                        if (c.TailMarkFound != Transcoder.TailMark)
                                            throw new InvalidDataException(L.T("verify NG: output could not be re-parsed"));
                                    }
                                    Program.WriteFileAtomic(dst, outp);
                                    ok++;
                                    AppendLogBg("OK   " + Transcoder.SummaryLine(name, Path.GetFileName(dst), data.Length, outp.Length, t.Stats)
                                        + (t.Stats.TimelineRenames.Count > 0 ? " | timeline: " + string.Join(", ", t.Stats.TimelineRenames) : ""));
                                }
                            }
                        }
                        catch (NotASceneException e)
                        {
                            // plain pictures / chara cards mixed into a scene folder are not errors
                            skipped++;
                            AppendLogBg("SKIP " + name + " : " + L.T("not a Studio scene, skipped") + " (" + e.Message + ")");
                        }
                        catch (Exception e)
                        {
                            failed++;
                            AppendLogBg(L.T("FAILED") + " " + name + " : " + e.Message);
                        }
                        finally
                        {
                            done++;
                            int d = done;
                            Post(() => { if (d <= pb.Maximum) pb.Value = d; });
                        }
                    }
                });
            }
            finally
            {
                bool cancelled = tok.IsCancellationRequested;
                cts.Dispose(); cts = null;
                SetBusy(false);
                string summary = (cancelled ? L.T("Cancelled") : L.T("Done")) + ": " + ok + " " + L.T(checkOnly ? "ok" : "converted")
                    + ", " + skipped + " " + L.T("skipped") + ", " + failed + " " + L.T(checkOnly ? "ng" : "failed");
                lblStatus.Text = summary;
                AppendLog("=== " + summary + " ===");
                // A folder that was actually converted into is worth remembering, but only
                // when the user chose it; the per-source default must not stick.
                if (!checkOnly && ok > 0 && outDirOverride != null) Settings.SaveLastOutputDir(outDirOverride);
                if (closeAfterRun) Close();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                // Let the file in progress finish; Run's finally closes the form afterwards.
                // Exiting now would kill the worker thread mid-write.
                e.Cancel = true;
                closeAfterRun = true;
                if (cts != null) cts.Cancel();
                lblStatus.Text = L.T("Cancelling…");
            }
            base.OnFormClosing(e);
        }

        void SetBusy(bool b)
        {
            busy = b;
            btnRun.Enabled = btnCheck.Enabled = btnBrowseIn.Enabled = btnBrowseOut.Enabled = !b;
            rbFile.Enabled = rbDir.Enabled = txtIn.Enabled = txtOut.Enabled = !b;
            chkOverwrite.Enabled = chkVerify.Enabled = chkVerbose.Enabled = !b;
            chkRecurse.Enabled = !b && rbDir.Checked;
            btnCancel.Enabled = b;
            UseWaitCursor = b;
        }

        // ---- logging ------------------------------------------------------
        void Post(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch { }
        }

        void AppendLogBg(string s) { Post(() => AppendLog(s)); }

        void AppendLog(string s)
        {
            if (log.TextLength > 4_000_000) log.Clear();
            log.AppendText(s + Environment.NewLine);
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
        }
    }
}
