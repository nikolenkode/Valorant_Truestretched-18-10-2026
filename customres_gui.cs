// CustomResGui.cs - WinForms front end for the shared core (C# 5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Security.Principal;
using System.Windows.Forms;

namespace CustomRes
{
    public class Preset
    {
        public string Label; public int W; public int H;
        public Preset(string label, int w, int h) { Label = label; W = w; H = h; }
        public override string ToString() { return Label; }
    }

    // "Keep this resolution?" with a countdown; no answer = revert
    public class KeepForm : Form
    {
        private readonly Label lblText = new Label();
        private readonly Label lblCount = new Label();
        private readonly Button btnKeep = new Button();
        private readonly Button btnRevert = new Button();
        private readonly Timer timer = new Timer();
        private int left;

        public KeepForm(string question, int seconds)
        {
            left = seconds;
            Text = Strings.T("gui_keep_title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ControlBox = false; MinimizeBox = false; MaximizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(400, 150);
            Font = new Font("Segoe UI", 10f);

            lblText.Text = question;
            lblText.Left = 16; lblText.Top = 16; lblText.Width = 368; lblText.Height = 28;
            lblCount.Left = 16; lblCount.Top = 50; lblCount.Width = 368; lblCount.Height = 24;
            btnKeep.Text = Strings.T("gui_keep");
            btnKeep.Left = 70; btnKeep.Top = 96; btnKeep.Width = 120; btnKeep.Height = 34;
            btnKeep.DialogResult = DialogResult.Yes;
            btnRevert.Text = Strings.T("gui_revert");
            btnRevert.Left = 210; btnRevert.Top = 96; btnRevert.Width = 120; btnRevert.Height = 34;
            btnRevert.DialogResult = DialogResult.No;
            Controls.Add(lblText); Controls.Add(lblCount); Controls.Add(btnKeep); Controls.Add(btnRevert);
            AcceptButton = btnKeep;
            CancelButton = btnRevert;

            timer.Interval = 1000;
            timer.Tick += delegate
            {
                left--;
                UpdateCount();
                if (left <= 0) { timer.Stop(); DialogResult = DialogResult.No; }
            };
            UpdateCount();
            timer.Start();
        }

        private void UpdateCount() { lblCount.Text = Strings.T("gui_revert_in", left); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    public class MainForm : Form
    {
        private readonly IPlatform platform = new WinPlatform();
        private readonly Label lblLang = new Label();
        private readonly ComboBox cbLang = new ComboBox();
        private readonly GroupBox gbDisplay = new GroupBox();
        private readonly Label lblDisplay = new Label();
        private readonly GroupBox gbRes = new GroupBox();
        private readonly Label lblPreset = new Label();
        private readonly ComboBox cbPreset = new ComboBox();
        private readonly Label lblW = new Label();
        private readonly Label lblH = new Label();
        private readonly Label lblHz = new Label();
        private readonly NumericUpDown numW = new NumericUpDown();
        private readonly NumericUpDown numH = new NumericUpDown();
        private readonly NumericUpDown numHz = new NumericUpDown();
        private readonly Label lblTiming = new Label();
        private readonly GroupBox gbOpt = new GroupBox();
        private readonly CheckBox chkStretch = new CheckBox();
        private readonly CheckBox chkDisable = new CheckBox();
        private readonly CheckBox chkReenable = new CheckBox();
        private readonly Label lblHint = new Label();
        private readonly Button btnApply = new Button();
        private readonly Button btnRestore = new Button();
        private readonly Button btnClose = new Button();
        private readonly RichTextBox log = new RichTextBox();

        private volatile bool busy;
        private bool nvidiaPrimary;
        private bool building = true;

        public MainForm()
        {
            Strings.Lang = (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru") ? "ru" : "en";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 640);
            MinimumSize = new Size(560, 600);

            lblLang.Left = 400; lblLang.Top = 12; lblLang.Width = 110; lblLang.TextAlign = ContentAlignment.MiddleRight;
            lblLang.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cbLang.Left = 516; cbLang.Top = 9; cbLang.Width = 110; cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cbLang.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cbLang.Items.Add("English"); cbLang.Items.Add("Русский");
            cbLang.SelectedIndex = (Strings.Lang == "ru") ? 1 : 0;
            cbLang.SelectedIndexChanged += delegate
            {
                Strings.Lang = (cbLang.SelectedIndex == 1) ? "ru" : "en";
                ApplyLanguage();
            };

            gbDisplay.Left = 12; gbDisplay.Top = 40; gbDisplay.Width = 614; gbDisplay.Height = 56;
            gbDisplay.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblDisplay.Left = 12; lblDisplay.Top = 22; lblDisplay.Width = 590; lblDisplay.Height = 24;
            lblDisplay.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            gbDisplay.Controls.Add(lblDisplay);

            gbRes.Left = 12; gbRes.Top = 104; gbRes.Width = 614; gbRes.Height = 150;
            gbRes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblPreset.Left = 12; lblPreset.Top = 26; lblPreset.Width = 100;
            cbPreset.Left = 118; cbPreset.Top = 22; cbPreset.Width = 220; cbPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cbPreset.SelectedIndexChanged += delegate
            {
                Preset p = cbPreset.SelectedItem as Preset;
                if (p != null && !building) { numW.Value = p.W; numH.Value = p.H; }
            };
            lblW.Left = 12; lblW.Top = 62; lblW.Width = 100;
            numW.Left = 118; numW.Top = 58; numW.Width = 90; numW.Minimum = 320; numW.Maximum = 8192; numW.Value = 1280;
            lblH.Left = 222; lblH.Top = 62; lblH.Width = 70; lblH.TextAlign = ContentAlignment.MiddleRight;
            numH.Left = 298; numH.Top = 58; numH.Width = 90; numH.Minimum = 200; numH.Maximum = 8192; numH.Value = 960;
            lblHz.Left = 402; lblHz.Top = 62; lblHz.Width = 100; lblHz.TextAlign = ContentAlignment.MiddleRight;
            numHz.Left = 508; numHz.Top = 58; numHz.Width = 90; numHz.Minimum = 24; numHz.Maximum = 1000; numHz.DecimalPlaces = 3; numHz.Value = 60;
            lblTiming.Left = 12; lblTiming.Top = 96; lblTiming.Width = 590; lblTiming.Height = 44;
            lblTiming.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            EventHandler changed = delegate { if (!building) UpdateTiming(); };
            numW.ValueChanged += changed; numH.ValueChanged += changed; numHz.ValueChanged += changed;
            chkStretch.CheckedChanged += changed;
            gbRes.Controls.Add(lblPreset); gbRes.Controls.Add(cbPreset);
            gbRes.Controls.Add(lblW); gbRes.Controls.Add(numW);
            gbRes.Controls.Add(lblH); gbRes.Controls.Add(numH);
            gbRes.Controls.Add(lblHz); gbRes.Controls.Add(numHz);
            gbRes.Controls.Add(lblTiming);

            gbOpt.Left = 12; gbOpt.Top = 262; gbOpt.Width = 614; gbOpt.Height = 122;
            gbOpt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chkStretch.Left = 12; chkStretch.Top = 22; chkStretch.Width = 590; chkStretch.Checked = true;
            chkDisable.Left = 12; chkDisable.Top = 48; chkDisable.Width = 590; chkDisable.Checked = true;
            chkReenable.Left = 36; chkReenable.Top = 74; chkReenable.Width = 566; chkReenable.Checked = true;
            chkDisable.CheckedChanged += delegate { chkReenable.Enabled = chkDisable.Checked && !busy; };
            gbOpt.Controls.Add(chkStretch); gbOpt.Controls.Add(chkDisable); gbOpt.Controls.Add(chkReenable);

            lblHint.Left = 12; lblHint.Top = 390; lblHint.Width = 614; lblHint.Height = 34;
            lblHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblHint.ForeColor = SystemColors.GrayText;

            btnApply.Left = 12; btnApply.Top = 428; btnApply.Width = 200; btnApply.Height = 34;
            btnApply.Click += delegate { OnApply(); };
            btnRestore.Left = 222; btnRestore.Top = 428; btnRestore.Width = 170; btnRestore.Height = 34;
            btnRestore.Click += delegate { OnRestore(); };
            btnClose.Left = 526; btnClose.Top = 428; btnClose.Width = 100; btnClose.Height = 34;
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += delegate { Close(); };

            log.Left = 12; log.Top = 472; log.Width = 614; log.Height = 156;
            log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            log.ReadOnly = true;
            log.BackColor = Color.FromArgb(24, 24, 24);
            log.ForeColor = Color.Gainsboro;
            log.Font = new Font("Consolas", 9f);
            log.BorderStyle = BorderStyle.FixedSingle;
            log.DetectUrls = false;

            Controls.Add(lblLang); Controls.Add(cbLang);
            Controls.Add(gbDisplay); Controls.Add(gbRes); Controls.Add(gbOpt); Controls.Add(lblHint);
            Controls.Add(btnApply); Controls.Add(btnRestore); Controls.Add(btnClose);
            Controls.Add(log);
            AcceptButton = btnApply;

            building = false;
            LoadDisplay();
            ApplyLanguage();
            UpdateTiming();

            FormClosing += delegate(object s, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            Shown += delegate
            {
                if (!IsAdmin()) Append(Strings.T("gui_not_admin"), Engine.LvWarn);
            };
        }

        private static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { return true; }
        }

        private void LoadDisplay()
        {
            nvidiaPrimary = false;
            try
            {
                foreach (DispInfo d in platform.Displays())
                {
                    if (!d.Primary) continue;
                    nvidiaPrimary = d.Desc != null && d.Desc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
                    lblDisplay.Text = d.Name + "   " + d.Desc + "   " + d.W + "x" + d.H + " @ " + d.Hz + " Hz";
                    if (d.Hz >= 24 && d.Hz <= 1000) numHz.Value = d.Hz;
                    break;
                }
            }
            catch (Exception ex) { lblDisplay.Text = ex.Message; }
        }

        private void ApplyLanguage()
        {
            building = true;
            Text = Strings.T("gui_title");
            lblLang.Text = Strings.T("gui_lang");
            gbDisplay.Text = Strings.T("gui_display");
            if (!nvidiaPrimary) { }
            gbRes.Text = Strings.T("gui_resolution");
            lblPreset.Text = Strings.T("gui_presets");
            lblW.Text = Strings.T("gui_width");
            lblH.Text = Strings.T("gui_height");
            lblHz.Text = Strings.T("gui_hz");
            gbOpt.Text = Strings.T("gui_options");
            chkStretch.Text = Strings.T("gui_stretch");
            chkDisable.Text = Strings.T("gui_disable");
            chkReenable.Text = Strings.T("gui_reenable");
            lblHint.Text = Strings.T("gui_hint");
            btnApply.Text = Strings.T("gui_apply");
            btnRestore.Text = Strings.T("gui_restore");
            btnClose.Text = Strings.T("gui_close");

            cbPreset.Items.Clear();
            cbPreset.Items.Add(Strings.T("gui_preset_pick"));
            cbPreset.Items.Add(new Preset("1280 x 960   (4:3)", 1280, 960));
            cbPreset.Items.Add(new Preset("1440 x 1080  (4:3)", 1440, 1080));
            cbPreset.Items.Add(new Preset("1024 x 768   (4:3)", 1024, 768));
            cbPreset.Items.Add(new Preset("1600 x 1200  (4:3)", 1600, 1200));
            cbPreset.Items.Add(new Preset("1920 x 1440  (4:3)", 1920, 1440));
            cbPreset.Items.Add(new Preset("1280 x 1024  (5:4)", 1280, 1024));
            cbPreset.Items.Add(new Preset("1680 x 1050  (16:10)", 1680, 1050));
            cbPreset.SelectedIndex = 0;
            building = false;
            if (!nvidiaPrimary) lblDisplay.Text = Strings.T("gui_display_none") + "   " + lblDisplay.Text;
            UpdateTiming();
        }

        private void UpdateTiming()
        {
            bool stretch = chkStretch.Checked;
            Validation v = Cvt.Validate((int)numW.Value, (int)numH.Value, (double)numHz.Value, stretch);
            if (v.Errors.Count > 0)
            {
                lblTiming.ForeColor = Color.Firebrick;
                lblTiming.Text = v.Errors[0];
            }
            else if (stretch)
            {
                lblTiming.ForeColor = SystemColors.ControlText;
                lblTiming.Text = Strings.T("gui_timing_stretch", v.Timing.W, v.Timing.H);
            }
            else
            {
                Timing t = v.Timing;
                lblTiming.ForeColor = SystemColors.ControlText;
                string s = Strings.T("gui_timing", t.W, t.HTotal, t.H, t.VTotal, t.PclkMHz, t.RealHz);
                if (v.Caveats.Count > 0) { s += "\r\n" + Strings.T("lbl_warning") + ": " + v.Caveats[0]; lblTiming.ForeColor = Color.DarkOrange; }
                else if (v.Warnings.Count > 0) { s += "\r\n" + v.Warnings[0]; lblTiming.ForeColor = Color.DarkOrange; }
                lblTiming.Text = s;
            }
            btnApply.Enabled = !busy && nvidiaPrimary && v.Errors.Count == 0;
        }

        private void SetBusy(bool value)
        {
            busy = value;
            gbRes.Enabled = !value; gbOpt.Enabled = !value; cbLang.Enabled = !value;
            btnRestore.Enabled = !value; btnClose.Enabled = !value;
            chkReenable.Enabled = !value && chkDisable.Checked;
            UpdateTiming();
        }

        private void Append(string text, int level)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string, int>(Append), text, level); return; }
            Color c;
            switch (level)
            {
                case Engine.LvOk: c = Color.LightGreen; break;
                case Engine.LvWarn: c = Color.Orange; break;
                case Engine.LvErr: c = Color.Tomato; break;
                case Engine.LvHead: c = Color.DeepSkyBlue; break;
                case Engine.LvDim: c = Color.Gray; break;
                default: c = Color.Gainsboro; break;
            }
            log.SelectionStart = log.TextLength;
            log.SelectionLength = 0;
            log.SelectionColor = c;
            log.AppendText(text + "\n");
            log.SelectionColor = log.ForeColor;
            log.ScrollToCaret();
        }

        private bool ConfirmKeep(string question, int seconds)
        {
            bool keep = false;
            Invoke((MethodInvoker)delegate
            {
                using (KeepForm f = new KeepForm(question, seconds)) { keep = (f.ShowDialog(this) == DialogResult.Yes); }
            });
            return keep;
        }

        private bool AskYesNo(string question)
        {
            bool yes = false;
            Invoke((MethodInvoker)delegate
            {
                yes = (MessageBox.Show(this, question, Strings.T("gui_confirm_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
            });
            return yes;
        }

        private void OnApply()
        {
            if (busy) return;
            int w = (int)numW.Value, h = (int)numH.Value;
            double hz = (double)numHz.Value;
            Validation v = Cvt.Validate(w, h, hz, chkStretch.Checked);
            if (v.Errors.Count > 0)
            {
                MessageBox.Show(this, v.Errors[0], Strings.T("gui_invalid"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (v.Caveats.Count > 0)
            {
                if (MessageBox.Show(this, v.Caveats[0], Strings.T("gui_confirm_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }

            Options o = new Options();
            o.Width = w; o.Height = h; o.Hz = hz;
            o.Stretch = chkStretch.Checked;
            o.DisableMonitors = chkDisable.Checked;
            o.AskReenable = false;
            o.ReenableMonitors = chkReenable.Checked;
            o.TestSeconds = 15;

            Callbacks cb = new Callbacks();
            cb.Log = Append;
            cb.ConfirmKeep = ConfirmKeep;
            cb.AskYesNo = AskYesNo;

            SetBusy(true);
            Append("", Engine.LvNormal);
            Append(Strings.T("gui_busy"), Engine.LvHead);
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                RunResult r = null;
                try { r = Engine.Run(platform, o, cb); }
                catch (Exception ex) { r = new RunResult(); r.Status = ex.Message; }
                BeginInvoke((MethodInvoker)delegate
                {
                    Append("", Engine.LvNormal);
                    Append(Strings.T("gui_finished", r.Status), r.Success ? Engine.LvOk : Engine.LvWarn);
                    SetBusy(false);
                    LoadDisplayInfoOnly();
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        private void LoadDisplayInfoOnly()
        {
            try
            {
                foreach (DispInfo d in platform.Displays())
                {
                    if (!d.Primary) continue;
                    lblDisplay.Text = d.Name + "   " + d.Desc + "   " + d.W + "x" + d.H + " @ " + d.Hz + " Hz";
                    break;
                }
            }
            catch (Exception) { }
        }

        private void OnRestore()
        {
            if (busy) return;
            SetBusy(true);
            Action<string, int> lg = Append;
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try { platform.RestoreMonitors(lg); } catch (Exception ex) { Append(ex.Message, Engine.LvErr); }
                BeginInvoke((MethodInvoker)delegate
                {
                    Append(Strings.T("gui_restore_done"), Engine.LvOk);
                    SetBusy(false);
                });
            });
            th.IsBackground = true;
            th.Start();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}