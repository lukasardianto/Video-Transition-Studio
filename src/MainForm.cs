using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VideoTransitionBatchApp
{
    public class MainForm : Form
    {
        // Fluent UI Controls
        private Label lblTitle;
        private Label lblFfmpegBadge;

        private ListView lvItems;
        private ColumnHeader colCheck;
        private ColumnHeader colName;
        private ColumnHeader colType;
        private ColumnHeader colDuration;
        private ColumnHeader colInOut;
        private ColumnHeader colTransition;
        private ColumnHeader colStatus;

        private Button btnAddVideos;
        private Button btnAddImages;
        private Button btnAddFolder;
        private Button btnRemoveSelected;
        private Button btnClear;
        private Button btnSelectAll;
        private Button btnDeselectAll;

        // Per-item inspector card
        private Label lblItemHeader;
        private Label lblItemFileName;
        private NumericUpDown numItemTotalDur;
        private NumericUpDown numItemInDur;
        private NumericUpDown numItemOutDur;
        private ComboBox cmbItemTransition;
        private ComboBox cmbKenBurns;
        private Label lblZoomLabel;
        private TrackBar trkZoom;
        private Label lblZoomValue;
        private Button btnApplyToChecked;

        // Global output card
        private ComboBox cmbBgMode;
        private Panel pnlBgSwatch;
        private Button btnBgPick;
        private Label lblBgHex;
        private TextBox txtBgImage;
        private Button btnBrowseBgImage;
        private ComboBox cmbResolution;
        private TextBox txtOutputDir;
        private Button btnBrowseOutput;
        private Color currentBgColor = Color.Black;

        // Execution & Status
        private Button btnStartBatch;
        private Button btnCancel;
        private Button btnOpenOutput;
        private ProgressBar pbOverall;
        private ProgressBar pbCurrent;
        private Label lblStatusText;
        private RichTextBox rtbLog;

        // State
        private string ffmpegPath;
        private string masksCacheDir;
        private List<MediaInfo> mediaList = new List<MediaInfo>();
        private CancellationTokenSource cts;
        private bool isProcessing = false;
        private bool isUpdatingControls = false;

        public MainForm()
        {
            InitializeFluentUI();
            SetupEngine();
        }

        private void InitializeFluentUI()
        {
            // Windows 11 Fluent Style
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "src", "icon.ico");
                if (!File.Exists(iconPath))
                    iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                    this.Icon = new Icon(iconPath);
            }
            catch
            {
            }
            this.Text = "Video Transition Studio";
            this.Size = new Size(1140, 780);
            this.MinimumSize = new Size(1140, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(243, 243, 243);
            this.Font = GetFluentFont(9.5f, FontStyle.Regular);
            this.AllowDrop = true;
            this.DragEnter += MainForm_DragEnter;
            this.DragDrop += MainForm_DragDrop;

            // Top Header Bar
            Panel pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.White,
                Padding = new Padding(24, 12, 24, 12)
            };
            pnlTop.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(229, 229, 229))) {
                    e.Graphics.DrawLine(p, 0, pnlTop.Height - 1, pnlTop.Width, pnlTop.Height - 1);
                }
            };

            lblTitle = new Label
            {
                Text = "Video Transition Studio",
                Font = GetFluentFont(14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 31, 31),
                AutoSize = true,
                Location = new Point(24, 16)
            };

            lblFfmpegBadge = new Label
            {
                Text = "● FFmpeg Embedded",
                Font = GetFluentFont(9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 124, 65),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(960, 20)
            };

            pnlTop.Controls.AddRange(new Control[] { lblTitle, lblFfmpegBadge });

            // Main Splitter (fixed sidebar, not resizable)
            SplitContainer splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 730,
                SplitterWidth = 6,
                IsSplitterFixed = true,
                FixedPanel = FixedPanel.Panel1,
                BackColor = Color.FromArgb(235, 235, 235)
            };
            this.Load += (s, e) =>
            {
                try
                {
                    splitMain.SplitterDistance = 730;
                }
                catch
                {
                }
            };

            // LEFT PANEL: List and toolbar
            Panel pnlLeftCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16)
            };

            // Toolbar
            FlowLayoutPanel pnlToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.White,
                Padding = new Padding(0)
            };

            btnAddVideos = CreateFluentButton("➕ Tambah Video", 120, true);
            btnAddVideos.Click += BtnAddVideos_Click;

            btnAddImages = CreateFluentButton("🖼️ Tambah Gambar", 130, false);
            btnAddImages.Click += BtnAddImages_Click;

            btnAddFolder = CreateFluentButton("📁 Tambah Folder", 125, false);
            btnAddFolder.Click += BtnAddFolder_Click;

            btnRemoveSelected = CreateFluentButton("Hapus", 75, false);
            btnRemoveSelected.Click += BtnRemoveSelected_Click;

            btnClear = CreateFluentButton("Bersihkan", 85, false);
            btnClear.Click += (s, e) => ClearAllItems();

            pnlToolbar.Controls.AddRange(new Control[] { btnAddVideos, btnAddImages, btnAddFolder, btnRemoveSelected, btnClear });

            // List View
            lvItems = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                CheckBoxes = true,
                MultiSelect = true,
                BorderStyle = BorderStyle.None,
                Font = GetFluentFont(9.5f, FontStyle.Regular),
                BackColor = Color.White
            };
            lvItems.SelectedIndexChanged += LvItems_SelectedIndexChanged;

            colCheck = new ColumnHeader { Text = "", Width = 30 };
            colName = new ColumnHeader { Text = "File", Width = 230 };
            colType = new ColumnHeader { Text = "Tipe", Width = 75 };
            colDuration = new ColumnHeader { Text = "Durasi", Width = 75 };
            colInOut = new ColumnHeader { Text = "In / Out", Width = 90 };
            colTransition = new ColumnHeader { Text = "Transisi", Width = 140 };
            colStatus = new ColumnHeader { Text = "Status", Width = 85 };

            lvItems.Columns.AddRange(new ColumnHeader[] { colCheck, colName, colType, colDuration, colInOut, colTransition, colStatus });

            // Quick select bar
            FlowLayoutPanel pnlSelectBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = Color.White,
                Padding = new Padding(0, 6, 0, 0)
            };

            btnSelectAll = CreateLinkButton("Pilih Semua");
            btnSelectAll.Click += (s, e) => SetAllChecked(true);

            btnDeselectAll = CreateLinkButton("Batal Pilih");
            btnDeselectAll.Click += (s, e) => SetAllChecked(false);

            pnlSelectBar.Controls.AddRange(new Control[] { btnSelectAll, btnDeselectAll });

            pnlLeftCard.Controls.Add(lvItems);
            pnlLeftCard.Controls.Add(pnlSelectBar);
            pnlLeftCard.Controls.Add(pnlToolbar);
            splitMain.Panel1.Controls.Add(pnlLeftCard);

            // RIGHT PANEL: Property Cards
            Panel pnlRight = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(243, 243, 243),
                Padding = new Padding(12),
                AutoScroll = true
            };

            // CARD 1: Per-Item Properties
            Panel cardItem = CreateCardPanel(14, 10, 350, 320);

            lblItemHeader = new Label
            {
                Text = "Pengaturan Item",
                Font = GetFluentFont(10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 32, 32),
                Location = new Point(14, 12),
                AutoSize = true
            };

            lblItemFileName = new Label
            {
                Text = "Pilih item dari daftar untuk mengubah",
                Font = GetFluentFont(8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 100, 100),
                Location = new Point(14, 34),
                Width = 320,
                AutoEllipsis = true
            };

            Label lblTot = new Label { Text = "Durasi Total:", Location = new Point(14, 60), AutoSize = true };
            numItemTotalDur = new NumericUpDown
            {
                Location = new Point(14, 80),
                Width = 90,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Minimum = 0.5m,
                Maximum = 600m,
                Value = 7.0m
            };
            numItemTotalDur.ValueChanged += NumItemTotalDur_ValueChanged;

            Label lblIn = new Label { Text = "Transisi Masuk (In):", Location = new Point(118, 60), AutoSize = true };
            numItemInDur = new NumericUpDown
            {
                Location = new Point(118, 80),
                Width = 100,
                DecimalPlaces = 1,
                Increment = 0.1m,
                Minimum = 0.2m,
                Maximum = 10.0m,
                Value = 1.2m
            };
            numItemInDur.ValueChanged += NumItemInDur_ValueChanged;

            Label lblOut = new Label { Text = "Transisi Keluar (Out):", Location = new Point(230, 60), AutoSize = true };
            numItemOutDur = new NumericUpDown
            {
                Location = new Point(230, 80),
                Width = 100,
                DecimalPlaces = 1,
                Increment = 0.1m,
                Minimum = 0.2m,
                Maximum = 10.0m,
                Value = 1.2m
            };
            numItemOutDur.ValueChanged += NumItemOutDur_ValueChanged;

            Label lblTr = new Label { Text = "Transisi:", Location = new Point(14, 115), AutoSize = true };
            cmbItemTransition = new ComboBox
            {
                Location = new Point(14, 137),
                Width = 318,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = GetFluentFont(9f, FontStyle.Regular)
            };
            cmbItemTransition.Items.Add("Brush Stroke");
            cmbItemTransition.Items.Add("Watercolor");
            cmbItemTransition.Items.Add("Smooth Fade");
            cmbItemTransition.Items.Add("Wipe");
            cmbItemTransition.Items.Add("Split");
            cmbItemTransition.Items.Add("Dissolve");
            cmbItemTransition.Items.Add("Checkerboard");
            cmbItemTransition.Items.Add("Blinds");
            cmbItemTransition.Items.Add("Clock");
            cmbItemTransition.Items.Add("Tidak ada (Cut)");
            cmbItemTransition.SelectedIndex = 0;
            cmbItemTransition.SelectedIndexChanged += CmbItemTransition_SelectedIndexChanged;

            Label lblKb = new Label { Text = "Animasi:", Location = new Point(14, 170), AutoSize = true };
            cmbKenBurns = new ComboBox
            {
                Location = new Point(14, 192),
                Width = 318,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = GetFluentFont(9f, FontStyle.Regular)
            };
            cmbKenBurns.Items.Add("Tidak ada");
            cmbKenBurns.Items.Add("Zoom In");
            cmbKenBurns.Items.Add("Zoom Out");
            cmbKenBurns.SelectedIndex = 0;
            cmbKenBurns.SelectedIndexChanged += CmbKenBurns_SelectedIndexChanged;

            lblZoomLabel = new Label { Text = "Rasio Zoom (%):", Location = new Point(14, 222), AutoSize = true };
            trkZoom = new TrackBar
            {
                Location = new Point(14, 240),
                Width = 230,
                Height = 28,
                Minimum = 0,
                Maximum = 30,
                Value = 12,
                TickFrequency = 5
            };
            trkZoom.ValueChanged += TrkZoom_ValueChanged;
            lblZoomValue = new Label { Text = "12%", Location = new Point(252, 244), Width = 60 };

            btnApplyToChecked = CreateFluentButton("Terapkan ke Semua", 318, false);
            btnApplyToChecked.Location = new Point(14, 274);
            btnApplyToChecked.Height = 28;
            btnApplyToChecked.Click += BtnApplyToChecked_Click;

            cardItem.Controls.AddRange(new Control[] {
                lblItemHeader, lblItemFileName,
                lblTot, numItemTotalDur,
                lblIn, numItemInDur,
                lblOut, numItemOutDur,
                lblTr, cmbItemTransition,
                lblKb, cmbKenBurns,
                lblZoomLabel, trkZoom, lblZoomValue,
                btnApplyToChecked
            });

            UpdateKenBurnsVisibility();

            // CARD 2: Output Settings
            Panel cardOutput = CreateCardPanel(14, 340, 350, 240);

            Label lblOutHeader = new Label
            {
                Text = "Pengaturan Output",
                Font = GetFluentFont(10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 32, 32),
                Location = new Point(14, 12),
                AutoSize = true
            };

            Label lblBg = new Label { Text = "Latar:", Location = new Point(14, 38), AutoSize = true };
            cmbBgMode = new ComboBox
            {
                Location = new Point(14, 58),
                Width = 318,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = GetFluentFont(9f, FontStyle.Regular)
            };
            cmbBgMode.Items.Add("Warna");
            cmbBgMode.Items.Add("Gambar");
            cmbBgMode.SelectedIndex = 0;
            cmbBgMode.SelectedIndexChanged += CmbBgMode_SelectedIndexChanged;

            pnlBgSwatch = new Panel
            {
                Location = new Point(14, 84),
                Width = 60,
                Height = 24,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };
            btnBgPick = CreateFluentButton("Pilih Warna", 120, false);
            btnBgPick.Location = new Point(82, 83);
            btnBgPick.Height = 25;
            btnBgPick.Click += BtnBgPick_Click;
            lblBgHex = new Label { Text = "#000000", Location = new Point(210, 88), Width = 120 };

            txtBgImage = new TextBox { Location = new Point(14, 84), Width = 245, Font = GetFluentFont(8.5f, FontStyle.Regular), Visible = false };
            btnBrowseBgImage = CreateFluentButton("Cari Gambar...", 65, false);
            btnBrowseBgImage.Location = new Point(265, 83);
            btnBrowseBgImage.Height = 25;
            btnBrowseBgImage.Visible = false;
            btnBrowseBgImage.Click += BtnBrowseBgImage_Click;

            Label lblRes = new Label { Text = "Resolusi Output:", Location = new Point(14, 118), AutoSize = true };
            cmbResolution = new ComboBox
            {
                Location = new Point(14, 138),
                Width = 318,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = GetFluentFont(9f, FontStyle.Regular)
            };
            cmbResolution.Items.Add("1080p (1920x1080)");
            cmbResolution.Items.Add("720p (1280x720)");
            cmbResolution.SelectedIndex = 0;

            Label lblOutFolder = new Label { Text = "Folder Output:", Location = new Point(14, 170), AutoSize = true };
            txtOutputDir = new TextBox { Location = new Point(14, 188), Width = 245, Font = GetFluentFont(8.5f, FontStyle.Regular) };
            btnBrowseOutput = CreateFluentButton("Cari...", 65, false);
            btnBrowseOutput.Location = new Point(265, 187);
            btnBrowseOutput.Height = 25;
            btnBrowseOutput.Click += BtnBrowseOutput_Click;

            cardOutput.Controls.AddRange(new Control[] {
                lblOutHeader,
                lblBg, cmbBgMode,
                pnlBgSwatch, btnBgPick, lblBgHex,
                txtBgImage, btnBrowseBgImage,
                lblRes, cmbResolution,
                lblOutFolder, txtOutputDir, btnBrowseOutput
            });

            // CARD 3: Actions
            Panel cardActions = CreateCardPanel(14, 590, 350, 130);

            btnStartBatch = CreateFluentButton("🚀 Mulai Proses Batch", 200, true);
            btnStartBatch.Location = new Point(14, 14);
            btnStartBatch.Height = 36;
            btnStartBatch.Font = GetFluentFont(10f, FontStyle.Bold);
            btnStartBatch.Click += BtnStartBatch_Click;

            btnCancel = CreateFluentButton("Batal", 108, false);
            btnCancel.Location = new Point(224, 14);
            btnCancel.Height = 36;
            btnCancel.Enabled = false;
            btnCancel.ForeColor = Color.FromArgb(196, 43, 28);
            btnCancel.Click += BtnCancel_Click;

            btnOpenOutput = CreateFluentButton("Buka Folder Output", 318, false);
            btnOpenOutput.Location = new Point(14, 56);
            btnOpenOutput.Height = 28;
            btnOpenOutput.Click += (s, e) => OpenOutputDirectory();

            Button btnAbout = CreateFluentButton("Tentang", 318, false);
            btnAbout.Location = new Point(14, 88);
            btnAbout.Height = 28;
            btnAbout.Click += (s, e) => ShowAbout();

            cardActions.Controls.AddRange(new Control[] { btnStartBatch, btnCancel, btnOpenOutput, btnAbout });

            pnlRight.Controls.AddRange(new Control[] { cardItem, cardOutput, cardActions });
            splitMain.Panel2.Controls.Add(pnlRight);

            // BOTTOM PANEL: Progress & Log
            Panel pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 175,
                BackColor = Color.White,
                Padding = new Padding(16, 8, 16, 10)
            };
            pnlBottom.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(229, 229, 229))) {
                    e.Graphics.DrawLine(p, 0, 0, pnlBottom.Width, 0);
                }
            };

            lblStatusText = new Label
            {
                Text = "Siap.",
                Font = GetFluentFont(9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(40, 40, 40),
                Dock = DockStyle.Top,
                Height = 20
            };

            pbOverall = new ProgressBar { Dock = DockStyle.Top, Height = 12, Style = ProgressBarStyle.Continuous };
            pbCurrent = new ProgressBar { Dock = DockStyle.Top, Height = 6, Style = ProgressBarStyle.Continuous };

            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 8.5f),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None
            };

            pnlBottom.Controls.Add(rtbLog);
            pnlBottom.Controls.Add(pbCurrent);
            pnlBottom.Controls.Add(pbOverall);
            pnlBottom.Controls.Add(lblStatusText);

            this.Controls.Add(splitMain);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlTop);
        }

        private Font GetFluentFont(float size, FontStyle style)
        {
            try
            {
                return new Font("Segoe UI Variable Text", size, style);
            }
            catch
            {
                return new Font("Segoe UI", size, style);
            }
        }

        private Panel CreateCardPanel(int x, int y, int w, int h)
        {
            Panel p = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.White,
                Padding = new Padding(14)
            };
            p.Paint += (s, e) => {
                using (Pen pen = new Pen(Color.FromArgb(229, 229, 229))) {
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                }
            };
            return p;
        }

        private Button CreateFluentButton(string text, int width, bool isPrimary)
        {
            Button btn = new Button
            {
                Text = text,
                Width = width,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                Font = GetFluentFont(9f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            if (isPrimary)
            {
                btn.BackColor = Color.FromArgb(15, 108, 189);
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderSize = 0;
            }
            else
            {
                btn.BackColor = Color.White;
                btn.ForeColor = Color.FromArgb(36, 36, 36);
                btn.FlatAppearance.BorderColor = Color.FromArgb(209, 209, 209);
                btn.FlatAppearance.BorderSize = 1;
            }
            return btn;
        }

        private Button CreateLinkButton(string text)
        {
            Button btn = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 22,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(15, 108, 189),
                Cursor = Cursors.Hand,
                Font = GetFluentFont(8.5f, FontStyle.Regular)
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void SetupEngine()
        {
            masksCacheDir = VideoProcessor.GetWritableMasksCacheDir();
            try { Directory.CreateDirectory(masksCacheDir); } catch { }

            ffmpegPath = VideoProcessor.FindFFmpeg();
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                lblFfmpegBadge.Text = "● FFmpeg Ready";
                lblFfmpegBadge.ForeColor = Color.FromArgb(16, 124, 65);
                AppendLog("FFmpeg siap: " + ffmpegPath);
            }
            else
            {
                lblFfmpegBadge.Text = "● FFmpeg Missing";
                lblFfmpegBadge.ForeColor = Color.FromArgb(196, 43, 28);
                AppendLog("Peringatan: ffmpeg.exe tidak ditemukan.");
            }

            // Start fresh: no default source/output is preloaded.
            lblStatusText.Text = "Tambahkan video atau gambar untuk memulai.";
        }

        private void LvItems_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvItems.SelectedItems.Count == 0)
            {
                lblItemFileName.Text = "Pilih item untuk mengubah";
                return;
            }

            ListViewItem item = lvItems.SelectedItems[0];
            MediaInfo info = item.Tag as MediaInfo;
            if (info == null) return;

            isUpdatingControls = true;
            lblItemFileName.Text = info.FileName;
            numItemTotalDur.Value = (decimal)Math.Max(0.5, info.DurationSeconds);
            numItemTotalDur.Enabled = info.IsImage;
            numItemInDur.Value = (decimal)Math.Max(0.2, info.InDuration);
            numItemOutDur.Value = (decimal)Math.Max(0.2, info.OutDuration);
            cmbItemTransition.SelectedIndex = (int)info.Style;
            cmbKenBurns.SelectedIndex = (int)info.Animation;
            int zoomPct = (int)Math.Max(0, Math.Min(30, Math.Round(info.ZoomPercent)));
            trkZoom.Value = zoomPct;
            lblZoomValue.Text = zoomPct + "%";
            isUpdatingControls = false;
            UpdateKenBurnsVisibility();
        }

        private void NumItemTotalDur_ValueChanged(object sender, EventArgs e)
        {
            if (isUpdatingControls) return;
            if (lvItems.SelectedItems.Count == 0) return;

            double val = (double)numItemTotalDur.Value;
            foreach (ListViewItem item in lvItems.SelectedItems)
            {
                MediaInfo info = item.Tag as MediaInfo;
                if (info != null && info.IsImage)
                {
                    info.DurationSeconds = val;
                    item.SubItems[3].Text = string.Format("{0:0.0}s", val);
                }
            }
        }

        private void NumItemInDur_ValueChanged(object sender, EventArgs e)
        {
            if (isUpdatingControls) return;
            if (lvItems.SelectedItems.Count == 0) return;

            double val = (double)numItemInDur.Value;
            foreach (ListViewItem item in lvItems.SelectedItems)
            {
                MediaInfo info = item.Tag as MediaInfo;
                if (info != null)
                {
                    info.InDuration = val;
                    item.SubItems[4].Text = string.Format("{0:0.0}s / {1:0.0}s", info.InDuration, info.OutDuration);
                }
            }
        }

        private void NumItemOutDur_ValueChanged(object sender, EventArgs e)
        {
            if (isUpdatingControls) return;
            if (lvItems.SelectedItems.Count == 0) return;

            double val = (double)numItemOutDur.Value;
            foreach (ListViewItem item in lvItems.SelectedItems)
            {
                MediaInfo info = item.Tag as MediaInfo;
                if (info != null)
                {
                    info.OutDuration = val;
                    item.SubItems[4].Text = string.Format("{0:0.0}s / {1:0.0}s", info.InDuration, info.OutDuration);
                }
            }
        }

        private void CmbItemTransition_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isUpdatingControls) return;
            if (lvItems.SelectedItems.Count == 0) return;

            // Apply only to the inspected item; use "Terapkan ke Semua" for bulk.
            TransitionStyle style = (TransitionStyle)cmbItemTransition.SelectedIndex;
            ListViewItem item = lvItems.SelectedItems[0];
            MediaInfo info = item.Tag as MediaInfo;
            if (info != null)
            {
                info.Style = style;
                item.SubItems[5].Text = GetStyleDisplayName(style);
            }
        }

        private void CmbKenBurns_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateKenBurnsVisibility();
            if (isUpdatingControls) return;
            if (lvItems.SelectedItems.Count == 0) return;

            KenBurnsMode mode = (KenBurnsMode)cmbKenBurns.SelectedIndex;
            ListViewItem item = lvItems.SelectedItems[0];
            MediaInfo info = item.Tag as MediaInfo;
            if (info != null)
            {
                info.Animation = mode;
            }
        }

        private void UpdateKenBurnsVisibility()
        {
            bool isZoom = cmbKenBurns != null && cmbKenBurns.SelectedIndex != 0;
            if (lblZoomLabel != null) lblZoomLabel.Visible = isZoom;
            if (trkZoom != null) trkZoom.Visible = isZoom;
            if (lblZoomValue != null) lblZoomValue.Visible = isZoom;
        }

        private void TrkZoom_ValueChanged(object sender, EventArgs e)
        {
            int pct = trkZoom.Value;
            lblZoomValue.Text = pct + "%";
            if (isUpdatingControls) return;
            if (lvItems.SelectedItems.Count == 0) return;

            double val = pct;
            ListViewItem item = lvItems.SelectedItems[0];
            MediaInfo info = item.Tag as MediaInfo;
            if (info != null)
            {
                info.ZoomPercent = val;
            }
        }

        private void BtnApplyToChecked_Click(object sender, EventArgs e)
        {
            double inVal = (double)numItemInDur.Value;
            double outVal = (double)numItemOutDur.Value;
            double zoomPct = trkZoom.Value;
            TransitionStyle style = (TransitionStyle)cmbItemTransition.SelectedIndex;
            KenBurnsMode mode = (KenBurnsMode)cmbKenBurns.SelectedIndex;

            foreach (ListViewItem item in lvItems.Items)
            {
                if (item.Checked)
                {
                    MediaInfo info = item.Tag as MediaInfo;
                    if (info != null)
                    {
                        info.InDuration = inVal;
                        info.OutDuration = outVal;
                        info.ZoomPercent = zoomPct;
                        info.Style = style;
                        info.Animation = mode;
                        item.SubItems[4].Text = string.Format("{0:0.0}s / {1:0.0}s", inVal, outVal);
                        item.SubItems[5].Text = GetStyleDisplayName(style);
                    }
                }
            }
            lblStatusText.Text = "Pengaturan berhasil diterapkan ke semua item yang dicentang.";
        }

        private string GetStyleDisplayName(TransitionStyle style)
        {
            switch (style)
            {
                case TransitionStyle.BrushStroke: return "Brush Stroke";
                case TransitionStyle.Watercolor: return "Watercolor";
                case TransitionStyle.SmoothFade: return "Smooth Fade";
                case TransitionStyle.Wipe: return "Wipe";
                case TransitionStyle.Split: return "Split";
                case TransitionStyle.Dissolve: return "Dissolve";
                case TransitionStyle.Checkerboard: return "Checkerboard";
                case TransitionStyle.Blinds: return "Blinds";
                case TransitionStyle.Clock: return "Clock";
                case TransitionStyle.None: return "Tidak ada (Cut)";
                default: return style.ToString();
            }
        }

        private void BtnAddVideos_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;
                ofd.Filter = "Video (*.mp4;*.mov;*.avi;*.mkv)|*.mp4;*.mov;*.avi;*.mkv|All (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK) AddFiles(ofd.FileNames);
            }
        }

        private void BtnAddImages_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;
                ofd.Filter = "Gambar (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK) AddFiles(ofd.FileNames);
            }
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            string selected = ShowFolderPicker();
            if (!string.IsNullOrEmpty(selected)) LoadMediaFromDirectory(selected);
        }

        private void BtnRemoveSelected_Click(object sender, EventArgs e)
        {
            if (isProcessing) return;
            for (int i = lvItems.SelectedItems.Count - 1; i >= 0; i--)
            {
                ListViewItem item = lvItems.SelectedItems[i];
                MediaInfo info = item.Tag as MediaInfo;
                if (info != null) mediaList.Remove(info);
                lvItems.Items.Remove(item);
            }
            lblStatusText.Text = string.Format("Total {0} item tersisa.", mediaList.Count);
        }

        private void ClearAllItems()
        {
            if (isProcessing) return;
            mediaList.Clear();
            lvItems.Items.Clear();
            lblStatusText.Text = "Daftar kosong.";
        }

        private void SetAllChecked(bool isChecked)
        {
            foreach (ListViewItem item in lvItems.Items) item.Checked = isChecked;
        }

        private void BtnBrowseOutput_Click(object sender, EventArgs e)
        {
            string selected = ShowFolderPicker();
            if (!string.IsNullOrEmpty(selected)) txtOutputDir.Text = selected;
        }

        private string ShowFolderPicker()
        {
            try
            {
                string selected = FolderPicker.Show(this.Handle);
                if (!string.IsNullOrEmpty(selected)) return selected;
            }
            catch
            {
            }

            // Fallback bila dialog modern tidak tersedia: tetap bisa pilih folder.
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Pilih Folder";
                if (fbd.ShowDialog() == DialogResult.OK) return fbd.SelectedPath;
            }
            return null;
        }

        private void CmbBgMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isImage = cmbBgMode.SelectedIndex == 1;
            pnlBgSwatch.Visible = !isImage;
            btnBgPick.Visible = !isImage;
            lblBgHex.Visible = !isImage;
            txtBgImage.Visible = isImage;
            btnBrowseBgImage.Visible = isImage;
        }

        private void BtnBrowseBgImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Multiselect = false;
                ofd.Filter = "Gambar (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK) txtBgImage.Text = ofd.FileName;
            }
        }

        private void BtnBgPick_Click(object sender, EventArgs e)
        {
            using (ColorDialog cd = new ColorDialog())
            {
                cd.Color = currentBgColor;
                if (cd.ShowDialog() == DialogResult.OK)
                    SetBackgroundColor(cd.Color);
            }
        }

        private void SetBackgroundColor(Color color)
        {
            currentBgColor = color;
            pnlBgSwatch.BackColor = color;
            lblBgHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private string GetSelectedBgColor()
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", currentBgColor.R, currentBgColor.G, currentBgColor.B);
        }

        private string GetSelectedBgImage()
        {
            if (cmbBgMode.SelectedIndex != 1) return null;
            string path = txtBgImage.Text.Trim();
            return File.Exists(path) ? path : null;
        }

        private void GetSelectedResolution(out int width, out int height)
        {
            if (cmbResolution.SelectedIndex == 1)
            {
                width = 1280;
                height = 720;
            }
            else
            {
                width = 1920;
                height = 1080;
            }
        }

        public void LoadMediaFromDirectory(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return;
            List<string> files = new List<string>();
            foreach (string ext in VideoProcessor.VideoExtensions) files.AddRange(Directory.GetFiles(dirPath, "*" + ext));
            foreach (string ext in VideoProcessor.ImageExtensions) files.AddRange(Directory.GetFiles(dirPath, "*" + ext));

            if (files.Count > 0)
            {
                AddFiles(files.ToArray());
                if (string.IsNullOrEmpty(txtOutputDir.Text))
                    txtOutputDir.Text = Path.Combine(dirPath, "Output_Transitions");
            }
        }

        private void AddFiles(string[] filePaths)
        {
            if (string.IsNullOrEmpty(ffmpegPath)) return;

            double defIn = (double)numItemInDur.Value;
            double defOut = (double)numItemOutDur.Value;
            double defTot = (double)numItemTotalDur.Value;
            double defZoom = trkZoom.Value;
            TransitionStyle defStyle = (TransitionStyle)cmbItemTransition.SelectedIndex;
            KenBurnsMode defKb = (KenBurnsMode)cmbKenBurns.SelectedIndex;
            int defOutW, defOutH;
            GetSelectedResolution(out defOutW, out defOutH);

            foreach (string file in filePaths)
            {
                if (mediaList.Exists(v => v.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase))) continue;

                MediaInfo info = VideoProcessor.ProbeMedia(ffmpegPath, file, defTot, defStyle, defIn, defOut);
                info.ZoomPercent = defZoom;
                info.Animation = defKb;
                info.OutputWidth = defOutW;
                info.OutputHeight = defOutH;
                mediaList.Add(info);

                ListViewItem item = new ListViewItem("");
                item.Checked = true;
                item.SubItems.Add(info.FileName);
                item.SubItems.Add(info.IsImage ? "Gambar" : "Video");
                item.SubItems.Add(string.Format("{0:0.0}s", info.DurationSeconds));
                item.SubItems.Add(string.Format("{0:0.0}s / {1:0.0}s", info.InDuration, info.OutDuration));
                item.SubItems.Add(GetStyleDisplayName(info.Style));
                item.SubItems.Add("Siap");
                item.Tag = info;

                lvItems.Items.Add(item);
            }

            lblStatusText.Text = string.Format("Total {0} item dimuat.", mediaList.Count);
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                List<string> fileList = new List<string>();
                foreach (string path in files)
                {
                    if (Directory.Exists(path)) LoadMediaFromDirectory(path);
                    else if (File.Exists(path)) fileList.Add(path);
                }
                if (fileList.Count > 0) AddFiles(fileList.ToArray());
            }
        }

        private async void BtnStartBatch_Click(object sender, EventArgs e)
        {
            if (isProcessing) return;
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                MessageBox.Show("FFmpeg tidak ditemukan.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<ListViewItem> selectedItems = new List<ListViewItem>();
            foreach (ListViewItem item in lvItems.Items)
            {
                if (item.Checked) selectedItems.Add(item);
            }

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Pilih setidaknya 1 item dengan mencentang kotak.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string outDir = txtOutputDir.Text.Trim();
            if (string.IsNullOrEmpty(outDir))
            {
                MessageBox.Show("Tentukan folder output.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                string normOut = Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (ListViewItem item in selectedItems)
                {
                    MediaInfo info = item.Tag as MediaInfo;
                    if (info == null) continue;
                    string srcDir = Path.GetDirectoryName(Path.GetFullPath(info.FilePath));
                    if (srcDir == null) continue;
                    string normSrc = srcDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (normOut.Equals(normSrc, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            string.Format("Folder output ({0}) sama dengan folder input ({1}).\nPilih folder output lain.", normOut, normSrc),
                            "Folder output tidak boleh sama",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                }
            }
            catch
            {
            }

            Directory.CreateDirectory(outDir);

            isProcessing = true;
            btnStartBatch.Enabled = false;
            btnCancel.Enabled = true;
            btnAddVideos.Enabled = false;
            btnAddImages.Enabled = false;
            btnAddFolder.Enabled = false;
            btnClear.Enabled = false;
            btnRemoveSelected.Enabled = false;
            cts = new CancellationTokenSource();

            string bgColor = GetSelectedBgColor();
            string bgImage = GetSelectedBgImage();
            pbOverall.Maximum = selectedItems.Count;
            pbOverall.Value = 0;

            AppendLog(string.Format("Memulai batch {0} item ke: {1}", selectedItems.Count, outDir));

            int success = 0;
            int failed = 0;

            for (int i = 0; i < selectedItems.Count; i++)
            {
                if (cts.IsCancellationRequested) break;

                ListViewItem item = selectedItems[i];
                MediaInfo info = item.Tag as MediaInfo;
                if (info == null) continue;

                int batchOutW, batchOutH;
                GetSelectedResolution(out batchOutW, out batchOutH);
                info.OutputWidth = batchOutW;
                info.OutputHeight = batchOutH;

                item.SubItems[6].Text = "Proses 0%";
                item.Selected = true;
                item.EnsureVisible();

                lblStatusText.Text = string.Format("Memproses ({0}/{1}): {2}", i + 1, selectedItems.Count, info.FileName);
                AppendLog(string.Format("[{0}/{1}] {2} (In: {3:0.0}s, Out: {4:0.0}s, Efek: {5})", 
                    i + 1, selectedItems.Count, info.FileName, info.InDuration, info.OutDuration, info.Style));

                string outFileName = Path.GetFileNameWithoutExtension(info.FilePath) + ".mp4";
                string outFilePath = Path.Combine(outDir, outFileName);

                if (File.Exists(outFilePath))
                {
                    DialogResult overwriteResult = MessageBox.Show(
                        string.Format("File output sudah ada:\n{0}\nTimpa (overwrite)?", outFilePath),
                        "Konfirmasi overwrite",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (overwriteResult != DialogResult.Yes)
                    {
                        item.SubItems[6].Text = "Dilewati";
                        AppendLog(" -> Dilewati (file ada): " + outFileName);
                        pbOverall.Value = i + 1;
                        continue;
                    }
                }

                pbCurrent.Value = 0;

                bool ok = await Task.Run(() =>
                {
                    return VideoProcessor.ProcessMedia(
                        ffmpegPath,
                        masksCacheDir,
                        info,
                        outFilePath,
                        bgColor,
                        bgImage,
                        (prog) =>
                        {
                            this.BeginInvoke((Action)(() =>
                            {
                                int val = Math.Max(0, Math.Min(100, (int)prog.Percent));
                                pbCurrent.Value = val;
                                item.SubItems[6].Text = string.Format("{0}%", val);
                            }));
                        },
                        cts.Token,
                        (log) => { }
                    );
                });

                if (ok && File.Exists(outFilePath))
                {
                    success++;
                    item.SubItems[6].Text = "Selesai";
                    AppendLog(" -> Selesai: " + outFileName);
                }
                else
                {
                    failed++;
                    item.SubItems[6].Text = cts.IsCancellationRequested ? "Dibatalkan" : "Gagal";
                    AppendLog(" -> Gagal: " + info.FileName);
                }

                pbOverall.Value = i + 1;
            }

            isProcessing = false;
            btnStartBatch.Enabled = true;
            btnCancel.Enabled = false;
            btnAddVideos.Enabled = true;
            btnAddImages.Enabled = true;
            btnAddFolder.Enabled = true;
            btnClear.Enabled = true;
            btnRemoveSelected.Enabled = true;

            if (cts.IsCancellationRequested)
            {
                lblStatusText.Text = "Proses dibatalkan.";
                AppendLog("Proses dihentikan.");
            }
            else
            {
                lblStatusText.Text = string.Format("Selesai ({0} berhasil, {1} gagal).", success, failed);
                AppendLog(string.Format("Batch selesai: {0} berhasil, {1} gagal.", success, failed));
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (isProcessing && cts != null)
            {
                cts.Cancel();
                btnCancel.Enabled = false;
                lblStatusText.Text = "Membatalkan...";
            }
        }

        private void OpenOutputDirectory()
        {
            string outDir = txtOutputDir.Text.Trim();
            if (Directory.Exists(outDir)) Process.Start("explorer.exe", outDir);
        }

        private void ShowAbout()
        {
            using (Form form = new Form())
            {
                form.Text = "Tentang Video Transition Studio";
                form.StartPosition = FormStartPosition.CenterParent;
                form.ClientSize = new Size(460, 260);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.BackColor = Color.White;
                form.Font = GetFluentFont(9.5f, FontStyle.Regular);

                Label title = new Label
                {
                    Text = "Video Transition Studio",
                    Font = GetFluentFont(14f, FontStyle.Bold),
                    AutoSize = false,
                    Location = new Point(20, 18),
                    Width = 420,
                    Height = 28
                };

                Label verse = new Label
                {
                    Text = "Layanilah seorang akan yang lain, sesuai dengan karunia yang telah diperoleh tiap-tiap orang sebagai pengurus yang baik dari kasih karunia Allah.\n1 Petrus 4:10",
                    Font = GetFluentFont(9.5f, FontStyle.Italic),
                    AutoSize = false,
                    Location = new Point(20, 56),
                    Width = 420,
                    Height = 90
                };

                Label author = new Label
                {
                    Text = "Blessed Multimedia\n2026",
                    Font = GetFluentFont(10f, FontStyle.Bold),
                    AutoSize = false,
                    Location = new Point(20, 156),
                    Width = 420,
                    Height = 48,
                    ForeColor = Color.FromArgb(15, 108, 189)
                };

                Button btnOk = CreateFluentButton("OK", 110, true);
                btnOk.Location = new Point(330, 210);
                btnOk.Height = 30;
                btnOk.DialogResult = DialogResult.OK;

                form.Controls.Add(title);
                form.Controls.Add(verse);
                form.Controls.Add(author);
                form.Controls.Add(btnOk);
                form.ShowDialog(this);
            }
        }

        private void AppendLog(string message)
        {
            string line = string.Format("[{0:HH:mm:ss}] {1}\n", DateTime.Now, message);
            rtbLog.AppendText(line);
            rtbLog.SelectionStart = rtbLog.Text.Length;
            rtbLog.ScrollToCaret();
        }
    }

    internal static class FolderPicker
    {
        private const uint FOS_PICKFOLDERS = 0x20;
        private const uint FOS_FORCEFILESYSTEM = 0x40;
        private const uint FOS_NOCHANGEDIR = 0x08;
        private const uint FOS_PATHMUSTEXIST = 0x800;
        private const int SIGDN_FILESYSPATH = -2147458727;

        [ComImport, Guid("42F85138-8B8A-4110-A9F6-36F488B1D8E9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr hwndParent);
            [PreserveSig]
            void SetFolder([In] IShellItem psf);
            [PreserveSig]
            void GetResult([Out] out IShellItem psf);
            [PreserveSig]
            void SetOptions(uint opt);
            [PreserveSig]
            void GetOptions(out uint opt);
        }

        [ComImport, Guid("43826D1E-E21C-42AE-BAA8-679F9E76C853"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig]
            void BindToObject([In] IntPtr pbc, [In] IntPtr riid, [Out] out IntPtr ppv);
            [PreserveSig]
            void GetParent(out IShellItem ppsi);
            [PreserveSig]
            int GetDisplayName(int sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        }

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance([In] ref Guid rclsid, [In] IntPtr pUnkOuter, uint dwClsContext, [In] ref Guid riid, [Out] out IFileDialog ppv);

        public static string Show(IntPtr ownerHandle)
        {
            Guid clsid = new Guid("42F85138-8B8A-4110-A9F6-36F488B1D8E9");
            IFileDialog dialog = null;
            try
            {
                // CLSCTX_ALL = 23
                int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 23, ref clsid, out dialog);
                if (hr != 0) return null;
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR | FOS_PATHMUSTEXIST);
                hr = dialog.Show(ownerHandle);
                if (hr != 0) return null;
                IShellItem item;
                dialog.GetResult(out item);
                if (item == null) return null;
                try
                {
                    string path;
                    int hrName = item.GetDisplayName(SIGDN_FILESYSPATH, out path);
                    if (hrName != 0) return null;
                    return Directory.Exists(path) ? path : null;
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }
    }
}
