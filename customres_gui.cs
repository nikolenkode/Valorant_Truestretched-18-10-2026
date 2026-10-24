// CustomResGui.cs - WinForms front end for the shared core (C# 5).
// Look: minimal. Flat dark surfaces with rounded corners, one accent colour (the red of the application icon), plain type.
// Hover, switches and the stretch preview are animated. All controls are drawn here, because the standard WinForms controls cannot be themed.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace CustomRes
{
    // =====================================================================
    //  Theme and drawing helpers
    // =====================================================================
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(13, 13, 16);
        public static readonly Color Card = Color.FromArgb(21, 21, 26);
        public static readonly Color CardBorder = Color.FromArgb(34, 34, 42);
        public static readonly Color Field = Color.FromArgb(21, 21, 26);
        public static readonly Color FieldBorder = Color.FromArgb(44, 44, 54);
        public static readonly Color Text = Color.FromArgb(236, 236, 239);
        public static readonly Color Muted = Color.FromArgb(154, 156, 166);
        public static readonly Color Dim = Color.FromArgb(102, 104, 115);
        public static readonly Color Red = Color.FromArgb(225, 29, 46);
        public static readonly Color RedBright = Color.FromArgb(255, 79, 96);
        public static readonly Color RedDeep = Color.FromArgb(158, 15, 28);
        public static readonly Color Ok = Color.FromArgb(70, 213, 140);
        public static readonly Color Warn = Color.FromArgb(255, 176, 32);
        public static readonly Color Err = Color.FromArgb(255, 96, 96);

        public static Font Ui(float size) { return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font UiBold(float size) { return new Font("Segoe UI Semibold", size, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font Mono(float size) { return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point); }

        private static string displayFace;

        // the title uses Segoe UI Semibold, everything else Segoe UI
        private static string DisplayFace()
        {
            if (displayFace != null) return displayFace;
            displayFace = "Segoe UI Semibold";
            try
            {
                string[] want = new string[] { "Segoe UI Semibold" };
                using (InstalledFontCollection fc = new InstalledFontCollection())
                {
                    foreach (string w in want)
                    {
                        foreach (FontFamily f in fc.Families)
                        {
                            if (string.Equals(f.Name, w, StringComparison.OrdinalIgnoreCase)) { displayFace = f.Name; return displayFace; }
                        }
                    }
                }
            }
            catch (Exception) { }
            return displayFace;
        }

        public static Font Display(float size)
        {
            string face = DisplayFace();
            return new Font(face, size, face == "Bahnschrift" ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    public static class Gfx
    {
        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            GraphicsPath p = new GraphicsPath();
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c)) { g.FillPath(b, p); }
        }

        public static void StrokeRound(Graphics g, Color c, float width, RectangleF r, float radius)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, width)) { g.DrawPath(pen, p); }
        }

        public static Pen RoundPen(Color c, float width)
        {
            Pen p = new Pen(c, width);
            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
            return p;
        }

        public static StringFormat Centered()
        {
            StringFormat f = new StringFormat();
            f.Alignment = StringAlignment.Center; f.LineAlignment = StringAlignment.Center;
            f.FormatFlags = StringFormatFlags.NoWrap; f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }

        public static StringFormat Left()
        {
            StringFormat f = new StringFormat();
            f.Alignment = StringAlignment.Near; f.LineAlignment = StringAlignment.Center;
            f.FormatFlags = StringFormatFlags.NoWrap; f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    // window chrome: dark title bar (Windows 10/11) and dark scroll bars; every call is optional
    public static class Native
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        private static void Dwm(IntPtr h, int attr, int value)
        {
            try { DwmSetWindowAttribute(h, attr, ref value, 4); } catch (Exception) { }
        }

        public static void DarkTitleBar(IntPtr handle)
        {
            Dwm(handle, 20, 1);                 // immersive dark mode (Windows 10 2004+ / 11)
            Dwm(handle, 19, 1);                 // the same for older Windows 10 builds
            Dwm(handle, 35, 0x00100D0D);        // Windows 11: caption colour = the window colour #0D0D10 (COLORREF 0x00BBGGRR)
            Dwm(handle, 36, 0x00F1EEEE);        // Windows 11: caption text
            Dwm(handle, 34, 0x00100D0D);        // Windows 11: border colour, the same: no coloured frame
            RoundCorners(handle, true);
        }

        // Windows 11: rounded corners of the window (DWMWCP_ROUND) or square ones (DWMWCP_DONOTROUND, for full screen)
        public static void RoundCorners(IntPtr handle, bool round)
        {
            Dwm(handle, 33, round ? 2 : 1);
        }

        public static void DarkScrollBars(IntPtr handle)
        {
            try { SetWindowTheme(handle, "DarkMode_Explorer", null); } catch (Exception) { }
        }
    }

    // =====================================================================
    //  Controls
    // =====================================================================
    public class DarkControl : Control
    {
        private readonly Timer fx = new Timer();
        private float hoverT, hoverTarget;

        public DarkControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            fx.Interval = 15;
            fx.Tick += delegate { FxTick(); };
        }

        // 0..1, eases towards 1 while the pointer is over the control and back to 0 when it leaves
        protected float HoverT { get { return hoverT; } }
        protected void SetHover(bool on) { hoverTarget = on ? 1f : 0f; StartFx(); }
        protected void StartFx() { if (IsHandleCreated && !fx.Enabled) fx.Start(); }
        // a control with more animated values moves them here; true = still moving
        protected virtual bool StepFx() { return false; }

        private void FxTick()
        {
            bool more = false;
            float d = hoverTarget - hoverT;
            if (Math.Abs(d) > 0.01f) { hoverT += d * 0.3f; more = true; } else { hoverT = hoverTarget; }
            if (StepFx()) more = true;
            Invalidate();
            if (!more) fx.Stop();
        }

        protected override void Dispose(bool disposing) { if (disposing) fx.Dispose(); base.Dispose(disposing); }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        // these controls draw Text themselves; the base Control does not repaint when Text changes
        // (without this a new caption only showed up after the mouse moved over the control)
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }
    }

    // the well under the log: a rounded surface and a small caption
    public class Card : Panel
    {
        public string Caption = "";

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            g.Clear(Theme.Bg);
            Gfx.FillRound(g, Theme.Field, new RectangleF(0f, 0f, Width, Height), 14f);
            if (Caption.Length > 0)
            {
                using (Font f = Theme.UiBold(9.5f))
                using (SolidBrush tb = new SolidBrush(Theme.Muted)) { g.DrawString(Caption, f, tb, 18f, 12f); }
            }
        }
    }

    // the logo of the icon, flat: a dark tile and the V in two reds
    public class LogoMark : DarkControl
    {
        public LogoMark() { BackColor = Theme.Bg; Width = 44; Height = 44; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }          // WM_NCHITTEST -> HTTRANSPARENT: the window below gets the mouse
            base.WndProc(ref m);
        }

        private static PointF[] Scale(float[] xy, float k, float ox, float oy)
        {
            PointF[] p = new PointF[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new PointF(ox + xy[2 * i] * k, oy + xy[2 * i + 1] * k);
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            float s = Math.Min(Width, Height) - 2;
            RectangleF tile = new RectangleF(1, 1, s, s);
            float k = s / 1024f;
            Color tileColor = Color.FromArgb(24, 24, 30);
            Gfx.FillRound(g, tileColor, tile, s * 0.27f);
            using (SolidBrush lb = new SolidBrush(Theme.Red))
            using (SolidBrush rb = new SolidBrush(Color.FromArgb(172, 18, 34)))
            using (SolidBrush cut = new SolidBrush(tileColor))
            {
                g.FillPolygon(lb, Scale(new float[] { 176, 236, 396, 236, 505, 548, 505, 822, 410, 822 }, k, 1, 1));
                g.FillPolygon(rb, Scale(new float[] { 848, 236, 628, 236, 519, 548, 519, 822, 614, 822 }, k, 1, 1));
                g.FillPolygon(cut, Scale(new float[] { 250, 236, 318, 236, 352, 330, 284, 330 }, k, 1, 1));
            }
        }
    }

    // selectable preset: a rounded tile with the monitor in miniature and the part of it that this preset takes
    public class Chip : DarkControl
    {
        public bool Selected;
        public bool Badge;               // green dot: this is the original resolution
        public int PW, PH;               // the preset
        public int NW = 16, NH = 9;      // the monitor's own size: the miniature has its shape

        public Chip()
        {
            Cursor = Cursors.Hand; Height = 64; Width = 68;
            SetStyle(ControlStyles.Selectable, true); TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { SetHover(false); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            RectangleF tile = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
            Color fill = Selected ? Gfx.Mix(Theme.Field, Theme.Red, 0.16f) : Gfx.Mix(Theme.Field, Color.FromArgb(34, 34, 42), HoverT);
            Gfx.FillRound(g, fill, tile, 12f);
            if (Selected) Gfx.StrokeRound(g, Theme.Red, 1.5f, tile, 12f);
            else if (Focused) Gfx.StrokeRound(g, Color.FromArgb(120, 122, 134), 1.5f, tile, 12f);

            // the monitor in miniature and the part of it that this preset takes
            float aspect = (NW > 0 && NH > 0) ? (float)NW / NH : 16f / 9f;
            float bw = 38f, bh = bw / aspect;
            if (bh > 22f) { bh = 22f; bw = bh * aspect; }
            RectangleF box = new RectangleF(tile.X + (tile.Width - bw) / 2f, tile.Y + 10f + (22f - bh) / 2f, bw, bh);
            Color lineColor = Selected ? Theme.RedBright : Color.FromArgb(84, 86, 98);
            Gfx.StrokeRound(g, lineColor, 1.2f, box, 3f);
            if (PW > 0 && PH > 0 && NW > 0 && NH > 0)
            {
                float iw = box.Width * Math.Min(1f, (float)PW / NW), ih = box.Height * Math.Min(1f, (float)PH / NH);
                RectangleF inner = new RectangleF(box.X + (box.Width - iw) / 2f, box.Y + (box.Height - ih) / 2f, iw, ih);
                Color ic = !Enabled ? Theme.Dim : (Selected ? Theme.Red : Color.FromArgb(96, 98, 110));
                Gfx.FillRound(g, ic, inner, 2f);
            }

            // the label: it gets smaller before it would be cut
            float fsize = 9f;
            Font f = Theme.Ui(fsize);
            while (fsize > 7f && g.MeasureString(Text, f).Width > tile.Width - 6f) { f.Dispose(); fsize -= 0.5f; f = Theme.Ui(fsize); }
            Color tc = !Enabled ? Theme.Dim : (Selected ? Color.White : Gfx.Mix(Theme.Muted, Theme.Text, HoverT));
            using (f)
            using (StringFormat sf = Gfx.Centered())
            using (SolidBrush tb = new SolidBrush(tc))
            {
                g.DrawString(Text, f, tb, new RectangleF(tile.X, tile.Bottom - 26f, tile.Width, 20f), sf);
            }
            if (Badge && Enabled)
            {
                using (SolidBrush bb = new SolidBrush(Theme.Ok)) { g.FillEllipse(bb, tile.Right - 13f, tile.Y + 8f, 6f, 6f); }
            }
        }
    }

    // number input: a rounded field, a red ring while it has the focus, two small chevrons for the steps
    public class NumBox : DarkControl
    {
        private const int StepW = 36;
        private readonly TextBox tb = new TextBox();
        private decimal val, min = 0, max = 100;
        private int decimals;
        private bool focused;
        private int hoverPart;                 // 1 = up, 2 = down
        public decimal Increment = 1;
        public event EventHandler ValueChanged;

        public NumBox()
        {
            Height = 52;
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = Theme.Field;
            tb.ForeColor = Theme.Text;
            tb.Font = Theme.Ui(19f);
            tb.GotFocus += delegate { focused = true; Invalidate(); };
            tb.LostFocus += delegate { focused = false; SetValue(val, false); Invalidate(); };
            tb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { SetValue(val, false); }
            };
            tb.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (!(char.IsDigit(e.KeyChar) || e.KeyChar == '.' || e.KeyChar == ',' || char.IsControl(e.KeyChar))) e.Handled = true;
            };
            tb.TextChanged += delegate
            {
                decimal d;
                if (decimal.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out d) && d >= min && d <= max)
                {
                    d = Math.Round(d, decimals);
                    if (d != val) { val = d; if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
                }
            };
            Controls.Add(tb);
            Layout2();
        }

        public decimal Minimum { get { return min; } set { min = value; if (val < min) SetValue(min, false); } }
        public decimal Maximum { get { return max; } set { max = value; if (val > max) SetValue(max, false); } }
        public int DecimalPlaces { get { return decimals; } set { decimals = value; SetValue(val, false); } }
        public decimal Value { get { return val; } set { SetValue(value, true); } }

        private void SetValue(decimal v, bool raise)
        {
            if (v < min) v = min;
            if (v > max) v = max;
            v = Math.Round(v, decimals);
            bool changed = v != val;
            val = v;
            string t = val.ToString("F" + decimals, CultureInfo.InvariantCulture);
            if (tb.Text != t) tb.Text = t;
            if (changed && raise && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            Invalidate();
        }

        private void Step(int dir) { SetValue(val + dir * Increment, true); }

        private void Layout2()
        {
            tb.Left = 16;
            tb.Width = Math.Max(10, Width - 16 - StepW);
            tb.Top = Math.Max(0, (Height - tb.Height) / 2);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); }
        protected override void OnMouseWheel(MouseEventArgs e) { Step(e.Delta > 0 ? 1 : -1); }
        protected override void OnMouseLeave(EventArgs e) { hoverPart = 0; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int part = (e.X >= Width - StepW) ? (e.Y < Height / 2 ? 1 : 2) : 0;
            if (part != hoverPart) { hoverPart = part; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.X >= Width - StepW) Step(e.Y < Height / 2 ? 1 : -1);
            else tb.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            tb.Enabled = Enabled;
            Gfx.FillRound(g, Theme.Field, new RectangleF(0f, 0f, Width, Height), 12f);
            if (focused) Gfx.StrokeRound(g, Theme.Red, 1.5f, new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f), 11f);
            float cx = Width - StepW / 2f - 2f;
            for (int i = 1; i <= 2; i++)
            {
                float cy = Height / 2f + (i == 1 ? -9f : 9f);
                bool hot = (hoverPart == i) && Enabled;
                if (hot) Gfx.FillRound(g, Color.FromArgb(40, 40, 50), new RectangleF(cx - 12f, cy - 9f, 24f, 18f), 6f);
                float d = (i == 1) ? 1f : -1f;            // the chevron points up for the first cell, down for the second
                using (Pen p = Gfx.RoundPen(hot ? Color.White : Theme.Muted, 1.6f))
                {
                    g.DrawLines(p, new PointF[] { new PointF(cx - 4f, cy + 2f * d), new PointF(cx, cy - 2f * d), new PointF(cx + 4f, cy + 2f * d) });
                }
            }
        }
    }

    // a tick box with a label (the label wraps): a rounded square, a thin check mark that fades in
    public class Toggle : DarkControl
    {
        private bool chk;
        private float knob;                  // 0 = off ... 1 = on, moves smoothly
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            Height = 28; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true); TabStop = true;
        }

        public bool Checked
        {
            get { return chk; }
            set
            {
                if (chk != value)
                {
                    chk = value;
                    if (!IsHandleCreated) knob = chk ? 1f : 0f; else StartFx();
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        protected override bool StepFx()
        {
            float target = chk ? 1f : 0f;
            float d = target - knob;
            if (Math.Abs(d) > 0.01f) { knob += d * 0.3f; return true; }
            knob = target;
            return false;
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { SetHover(false); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            float k = Math.Max(0f, Math.Min(1f, knob));
            RectangleF box = new RectangleF(1f, 4f, 20f, 20f);
            Color onFill = Enabled ? Theme.Red : Color.FromArgb(86, 34, 40);
            Gfx.FillRound(g, Gfx.Mix(Theme.Field, onFill, k), box, 6f);
            if (k < 0.98f)
            {
                Color off = Enabled ? Gfx.Mix(Color.FromArgb(76, 78, 90), Color.FromArgb(150, 152, 164), HoverT) : Color.FromArgb(50, 52, 60);
                Gfx.StrokeRound(g, Color.FromArgb((int)(255 * (1f - k)), off), 1.5f, new RectangleF(box.X + 0.75f, box.Y + 0.75f, box.Width - 1.5f, box.Height - 1.5f), 5.25f);
            }
            if (k > 0.02f)
            {
                using (Pen p = Gfx.RoundPen(Color.FromArgb((int)(255 * k), Enabled ? Color.White : Color.FromArgb(190, 170, 172)), 2f))
                {
                    g.DrawLines(p, new PointF[] { new PointF(box.X + 5.2f, box.Y + 10.6f), new PointF(box.X + 8.6f, box.Y + 14f), new PointF(box.X + 14.8f, box.Y + 6.6f) });
                }
            }
            if (Focused) Gfx.StrokeRound(g, Color.FromArgb(150, Theme.RedBright), 1.5f, new RectangleF(box.X - 2.5f, box.Y - 2.5f, box.Width + 5f, box.Height + 5f), 8f);
            RectangleF tr = new RectangleF(32f, 5f, Math.Max(10f, Width - 32f), Math.Max(10f, Height - 5f));
            Font tf = Font;
            bool own = false;
            using (StringFormat sf = new StringFormat())
            using (SolidBrush tb = new SolidBrush(Enabled ? Theme.Text : Theme.Dim))
            {
                sf.Alignment = StringAlignment.Near; sf.LineAlignment = StringAlignment.Near; sf.Trimming = StringTrimming.EllipsisWord;
                // the label wraps; when it needs more lines than the control has room for, the type gets smaller instead of running into the next line
                float size = Font.Size;
                while (size > 7.5f && g.MeasureString(Text, tf, (int)tr.Width, sf).Height > tr.Height)
                {
                    if (own) tf.Dispose();
                    size -= 0.5f;
                    tf = new Font(Font.FontFamily, size, Font.Style, GraphicsUnit.Point);
                    own = true;
                }
                g.DrawString(Text, tf, tb, tr, sf);
            }
            if (own) tf.Dispose();
        }
    }

    public enum BtnKind { Primary, Secondary }

    // primary: solid red; secondary: a quiet grey surface. Rounded, no outline, no shadow; only the fill reacts to the pointer
    public class RedButton : DarkControl, IButtonControl
    {
        public BtnKind Kind = BtnKind.Secondary;
        private bool down, isDefault;
        private DialogResult dr = DialogResult.None;

        public RedButton()
        {
            Cursor = Cursors.Hand; Height = 44;
            Font = Theme.UiBold(10.5f);
            SetStyle(ControlStyles.Selectable, true); TabStop = true;
        }

        public DialogResult DialogResult { get { return dr; } set { dr = value; } }
        public void NotifyDefault(bool value) { isDefault = value; Invalidate(); }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            Form f = FindForm();
            if (f != null && dr != DialogResult.None) f.DialogResult = dr;
            base.OnClick(e);
        }
        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { down = false; SetHover(false); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { PerformClick(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            if (Width < 24 || Height < 20) return;
            bool primary = (Kind == BtnKind.Primary);
            float radius = Math.Min(12f, Height / 2f);
            Color fill, textColor;
            if (primary)
            {
                if (!Enabled) { fill = Color.FromArgb(30, 30, 36); textColor = Theme.Dim; }
                else
                {
                    fill = down ? Gfx.Mix(Theme.Red, Theme.RedDeep, 0.55f) : Gfx.Mix(Theme.Red, Theme.RedBright, 0.35f * HoverT);
                    textColor = Color.White;
                }
            }
            else
            {
                if (!Enabled) { fill = Color.FromArgb(22, 22, 27); textColor = Theme.Dim; }
                else
                {
                    fill = down ? Color.FromArgb(22, 22, 27) : Gfx.Mix(Color.FromArgb(27, 27, 33), Color.FromArgb(38, 38, 47), HoverT);
                    textColor = Theme.Text;
                }
            }
            Gfx.FillRound(g, fill, new RectangleF(0f, 0f, Width, Height), radius);
            if (Focused || isDefault && Focused)
            {
                Gfx.StrokeRound(g, Color.FromArgb(130, 255, 255, 255), 1.5f, new RectangleF(2f, 2f, Width - 4f, Height - 4f), Math.Max(2f, radius - 2f));
            }

            // the label, centred; it gets smaller before it would be cut
            Font f = Font;
            bool own = false;
            float size = Font.Size;
            while (size > 8f && g.MeasureString(Text, f).Width > Width - 24f)
            {
                if (own) f.Dispose();
                size -= 0.5f;
                f = new Font(Font.FontFamily, size, Font.Style, GraphicsUnit.Point);
                own = true;
            }
            using (StringFormat sf = Gfx.Centered())
            using (SolidBrush tb = new SolidBrush(textColor)) { g.DrawString(Text, f, tb, new RectangleF(12f, 0f, Width - 24f, Height), sf); }
            if (own) f.Dispose();
        }
    }

    // two-segment switch (language): a pill with a lighter pill sliding under the chosen label
    public class Segmented : DarkControl
    {
        public string[] Items = new string[] { "A", "B" };
        private int selected;
        private float pos;
        public event EventHandler SelectedChanged;

        public Segmented() { BackColor = Theme.Bg; Height = 30; Cursor = Cursors.Hand; }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (selected != value)
                {
                    selected = value;
                    if (!IsHandleCreated) pos = selected; else StartFx();
                    Invalidate();
                    if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
                }
            }
        }

        protected override bool StepFx()
        {
            float d = selected - pos;
            if (Math.Abs(d) > 0.01f) { pos += d * 0.3f; return true; }
            pos = selected;
            return false;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (!Enabled) return;
            int idx = Math.Min(Items.Length - 1, Math.Max(0, e.X * Items.Length / Math.Max(1, Width)));
            Selected = idx;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            Gfx.FillRound(g, Theme.Field, new RectangleF(0f, 0f, Width, Height), Height / 2f);
            float w = (Width - 6f) / Items.Length;
            Gfx.FillRound(g, Color.FromArgb(46, 46, 57), new RectangleF(3f + pos * w, 3f, w, Height - 6f), (Height - 6f) / 2f);
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF cell = new RectangleF(3f + i * w, 0f, w, Height);
                float near = Math.Max(0f, 1f - Math.Abs(i - pos));
                using (Font f = Theme.UiBold(9f))
                using (StringFormat sf = Gfx.Centered())
                using (SolidBrush tb = new SolidBrush(Gfx.Mix(Enabled ? Theme.Muted : Theme.Dim, Color.White, near))) { g.DrawString(Items[i], f, tb, cell, sf); }
            }
        }
    }

    public class ProgressLine : DarkControl
    {
        private float value = 1f;
        public ProgressLine() { Height = 6; }
        public float Value { get { return value; } set { this.value = Math.Max(0f, Math.Min(1f, value)); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            Gfx.FillRound(g, Color.FromArgb(34, 34, 42), new RectangleF(0f, 0f, Width, Height), Height / 2f);
            float w = Width * value;
            if (w >= 1f) Gfx.FillRound(g, Theme.Red, new RectangleF(0f, 0f, Math.Max(w, Height), Height), Height / 2f);
        }
    }

    // a small caption for a field
    public class TagLabel : DarkControl
    {
        public TagLabel() { Height = 18; Font = Theme.Ui(9f); ForeColor = Theme.Muted; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            using (StringFormat sf = Gfx.Left())
            using (SolidBrush tb = new SolidBrush(ForeColor)) { g.DrawString(Text, Font, tb, new RectangleF(0f, 0f, Width, Height), sf); }
        }
    }

    public enum WinKind { Minimize, Full, Close }

    // the buttons of the window (it has no system frame): minimise, full screen on / off, close
    public class WinButton : DarkControl
    {
        public WinKind Kind;
        public bool Active;                  // Full: the window covers the screen now

        public WinButton(WinKind kind)
        {
            Kind = kind; BackColor = Theme.Bg; Width = 36; Height = 28; Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { SetHover(false); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            bool close = (Kind == WinKind.Close);
            Gfx.FillRound(g, Gfx.Mix(Theme.Bg, close ? Theme.Red : Color.FromArgb(38, 38, 47), HoverT), new RectangleF(0f, 0f, Width, Height), 8f);
            float cx = Width / 2f, cy = Height / 2f;
            using (Pen p = Gfx.RoundPen(Gfx.Mix(Theme.Muted, Color.White, HoverT), 1.6f))
            {
                if (Kind == WinKind.Minimize)
                {
                    g.DrawLine(p, cx - 5f, cy + 3f, cx + 5f, cy + 3f);
                }
                else if (close)
                {
                    g.DrawLine(p, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
                    g.DrawLine(p, cx + 4.5f, cy - 4.5f, cx - 4.5f, cy + 4.5f);
                }
                else
                {
                    float arm = 3.5f;
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int sy = -1; sy <= 1; sy += 2)
                        {
                            if (!Active)
                            {
                                float px = cx + sx * 5.5f, py = cy + sy * 5.5f;          // the marks sit in the corners and point at them
                                g.DrawLines(p, new PointF[] { new PointF(px - sx * arm, py), new PointF(px, py), new PointF(px, py - sy * arm) });
                            }
                            else
                            {
                                float px = cx + sx * 1.8f, py = cy + sy * 1.8f;          // the marks are flipped and point at the centre
                                g.DrawLines(p, new PointF[] { new PointF(px + sx * arm, py), new PointF(px, py), new PointF(px, py + sy * arm) });
                            }
                        }
                    }
                }
            }
        }
    }

    // a label that lets the mouse through to the window below it, so that the header can be dragged by it
    public class CaptionLabel : Label
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }          // WM_NCHITTEST -> HTTRANSPARENT
            base.WndProc(ref m);
        }
    }

    // The picture inside the circle of the preview (agent.png). It is built into the .exe by build.bat; a file next to the .exe is the fallback.
    // A silhouette saved with its "transparency" checkerboard baked in as ordinary pixels is made transparent here, and the picture
    // is cropped to the figure, so that it fills the circle.
    public static class AgentArt
    {
        private static Bitmap art;
        private static bool tried;

        public static Bitmap Get()
        {
            if (tried) return art;
            tried = true;
            try
            {
                Bitmap raw = null;
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith("agent.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream s = asm.GetManifestResourceStream(n))
                        using (Bitmap b = new Bitmap(s)) { raw = new Bitmap(b); }
                        break;
                    }
                }
                if (raw == null)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent.png");
                    if (File.Exists(path)) { using (Bitmap b = new Bitmap(path)) { raw = new Bitmap(b); } }
                }
                if (raw != null) { art = Prepare(raw); raw.Dispose(); }
            }
            catch (Exception) { art = null; }
            return art;
        }

        private static bool Light(byte[] px, int w, int x, int y)
        {
            int i = (y * w + x) * 4;
            int lum = (px[i + 2] * 30 + px[i + 1] * 59 + px[i] * 11) / 100;
            return px[i + 3] > 250 && lum > 200;
        }

        private static Bitmap Prepare(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            byte[] px = new byte[w * h * 4];
            BitmapData bd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) { Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), px, y * w * 4, w * 4); }
            }
            finally { src.UnlockBits(bd); }

            // light, opaque corners: the background is part of the picture (a baked checkerboard); brightness becomes transparency
            if (Light(px, w, 0, 0) && Light(px, w, w - 1, 0) && Light(px, w, 0, h - 1) && Light(px, w, w - 1, h - 1))
            {
                for (int i = 0; i < px.Length; i += 4)
                {
                    int lum = (px[i + 2] * 30 + px[i + 1] * 59 + px[i] * 11) / 100;
                    int a = (235 - lum) * 255 / 195;
                    if (a < 0) a = 0;
                    if (a > 255) a = 255;
                    px[i] = 0; px[i + 1] = 0; px[i + 2] = 0; px[i + 3] = (byte)a;
                }
            }

            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (px[(y * w + x) * 4 + 3] > 10)
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
                }
            }
            if (x1 < x0 || y1 < y0) return null;
            int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
            Bitmap cropped = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
            BitmapData od = cropped.LockBits(new Rectangle(0, 0, cw, ch), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < ch; y++) { Marshal.Copy(px, ((y0 + y) * w + x0) * 4, new IntPtr(od.Scan0.ToInt64() + (long)y * od.Stride), cw * 4); }
            }
            finally { cropped.UnlockBits(od); }
            if (ch <= 480) return cropped;

            int nh = 480, nw = Math.Max(1, (int)Math.Round(cw * 480.0 / ch));
            Bitmap small = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(cropped, new Rectangle(0, 0, nw, nh));
            }
            cropped.Dispose();
            return small;
        }
    }



    // The screen you are shaping. The dark frame is the monitor, the red outline is the picture the game renders, and the test pattern
    // (grid of squares, circle with the agent in it, grey ramp) stretches with it: with "stretch" on the circle turns into an ellipse and
    // the agent gets wider, which is what happens in the game. Without it the monitor fills the rest with black bars.
    public class ScreenPreview : DarkControl
    {
        private int nativeW = 2560, nativeH = 1440, srcW = 1566, srcH = 1080;
        private bool nativeKnown;
        private bool stretch = true;
        private float t = 1f;                 // 0 = the picture as it is, in the middle; 1 = stretched over the whole screen

        public ScreenPreview() { BackColor = Theme.Bg; }

        public void SetState(int nw, int nh, int sw, int sh, bool stretched)
        {
            nativeKnown = (nw >= 320 && nh >= 200);
            if (nativeKnown) { nativeW = nw; nativeH = nh; } else { nativeW = 2560; nativeH = 1440; }
            srcW = Math.Max(1, sw); srcH = Math.Max(1, sh);
            if (stretched != stretch)
            {
                stretch = stretched;
                if (!IsHandleCreated) t = stretched ? 1f : 0f; else StartFx();
            }
            Invalidate();
        }

        // ease-out when "stretch" is switched: the picture grows to the whole screen or shrinks back
        protected override bool StepFx()
        {
            float target = stretch ? 1f : 0f;
            float d = target - t;
            if (Math.Abs(d) > 0.004f) { t += d * 0.2f; return true; }
            t = target;
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            g.Clear(Theme.Bg);
            if (Width < 160 || Height < 120) return;
            Gfx.FillRound(g, Theme.Field, new RectangleF(0f, 0f, Width, Height), 16f);

            // the monitor itself
            const float cr = 5f;
            float padX = 28f, padTop = 24f, below = 56f;
            RectangleF area = new RectangleF(padX, padTop, Width - 2 * padX, Height - padTop - below);
            float aspect = (float)nativeW / nativeH;
            float fw = area.Width, fh = fw / aspect;
            if (fh > area.Height) { fh = area.Height; fw = fh * aspect; }
            RectangleF frame = new RectangleF(area.X + (area.Width - fw) / 2f, area.Y + (area.Height - fh) / 2f, fw, fh);
            Gfx.FillRound(g, Color.FromArgb(6, 6, 8), frame, cr);                         // black glass: what the picture does not cover stays black
            Gfx.StrokeRound(g, Color.FromArgb(54, 56, 66), 1f, frame, cr);

            // the picture the game renders: its own size in the middle (t = 0) ... the whole screen (t = 1)
            float sx = Math.Min(1f, (float)srcW / nativeW), sy = Math.Min(1f, (float)srcH / nativeH);
            float kw = sx + (1f - sx) * t, kh = sy + (1f - sy) * t;
            RectangleF src = new RectangleF(frame.X + frame.Width * (1f - kw) / 2f, frame.Y + frame.Height * (1f - kh) / 2f, frame.Width * kw, frame.Height * kh);

            Gfx.FillRound(g, Gfx.Mix(Color.FromArgb(18, 18, 22), Theme.Red, 0.10f), src, cr);
            GraphicsState gs2 = g.Save();
            using (GraphicsPath clip = Gfx.Round(src, cr)) { g.SetClip(clip, CombineMode.Intersect); }
            float ux = src.Width / srcW, uy = src.Height / srcH;       // screen pixels per game pixel, across and down
            float cx = src.X + src.Width / 2f, cy = src.Y + src.Height / 2f;
            float cell = srcH / 8f;
            using (Pen grid = new Pen(Color.FromArgb(14, 255, 255, 255), 1f))
            using (Pen axis = new Pen(Color.FromArgb(34, 255, 255, 255), 1f))
            {
                for (int k = -60; k <= 60; k++)
                {
                    float gx = cx + k * cell * ux, gy = cy + k * cell * uy;
                    if (gx >= src.Left - 1f && gx <= src.Right + 1f) g.DrawLine(k == 0 ? axis : grid, gx, src.Top, gx, src.Bottom);
                    if (gy >= src.Top - 1f && gy <= src.Bottom + 1f) g.DrawLine(k == 0 ? axis : grid, src.Left, gy, src.Right, gy);
                }
            }

            // the circle: a flat red disc, so that the black figure stands out against it
            float rr = srcH * 0.36f;
            RectangleF disc = new RectangleF(cx - rr * ux, cy - rr * uy, 2f * rr * ux, 2f * rr * uy);
            using (SolidBrush db = new SolidBrush(Theme.Red)) { g.FillEllipse(db, disc); }

            // the agent: its height is a fixed share of the circle, its width follows its own proportions, and then it is scaled by the
            // same two factors as everything else in the picture, so it widens exactly as much as the circle does
            Bitmap agent = AgentArt.Get();
            if (agent != null)
            {
                float hg = rr * 1.80f, wg = hg * agent.Width / agent.Height;
                float dw = wg * ux, dh = hg * uy;
                PointF[] dst = new PointF[]
                {
                    new PointF(cx - dw / 2f, cy - dh / 2f), new PointF(cx + dw / 2f, cy - dh / 2f), new PointF(cx - dw / 2f, cy + dh / 2f)
                };
                GraphicsState gi = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(agent, dst, new RectangleF(0f, 0f, agent.Width, agent.Height), GraphicsUnit.Pixel, ia);
                }
                g.Restore(gi);
            }

            const int steps = 7;
            float rampW = 0.56f * srcW * ux / steps, rampX = cx - 0.28f * srcW * ux, rampY = src.Top + 0.07f * srcH * uy, rampH = 0.05f * srcH * uy;
            for (int i = 0; i < steps; i++)
            {
                int v = 50 + i * 30;
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(150, v, v, v))) { g.FillRectangle(bb, rampX + i * rampW, rampY, rampW + 0.6f, rampH); }
            }
            g.Restore(gs2);

            // the outline of the picture
            Gfx.StrokeRound(g, Color.FromArgb(215, Theme.Red), 1.5f, src, cr);

            // the size of the picture, as a small pill in a corner of it: bottom left, or top left when the circle is in the way
            if (src.Width > 150f && src.Height > 64f)
            {
                string size = srcW + "\u00D7" + srcH;
                using (Font tf = Theme.UiBold(9f))
                {
                    SizeF sz = g.MeasureString(size, tf);
                    float tw = sz.Width + 14f, th = sz.Height + 4f;
                    RectangleF tag = new RectangleF(src.Left + 10f, src.Bottom - th - 10f, tw, th);
                    float rx = rr * 1.12f * ux, ry = rr * 1.12f * uy;
                    float qx = (tag.Right - cx) / rx, qy = (tag.Top - cy) / ry;            // the corner of the pill that is nearest to the circle
                    bool hits = (qx * qx + qy * qy) < 1f;
                    if (hits)
                    {
                        tag = new RectangleF(src.Left + 10f, src.Top + 10f, tw, th);
                        qx = (tag.Right - cx) / rx; qy = (tag.Bottom - cy) / ry;
                        hits = ((qx * qx + qy * qy) < 1f) || (tag.Right > rampX - 4f && tag.Bottom > rampY - 2f);
                    }
                    if (!hits)
                    {
                        Gfx.FillRound(g, Color.FromArgb(170, 0, 0, 0), tag, th / 2f);
                        using (StringFormat sf = Gfx.Centered())
                        using (SolidBrush wb = new SolidBrush(Color.White)) { g.DrawString(size, tf, wb, tag, sf); }
                    }
                }
            }

            // under the frame: the monitor's size, and what happens to the picture. Both strings are measured: side by side when they fit
            // (the Russian ones are much longer than the English ones), one above the other when they do not
            using (Font ff = Theme.Ui(9f))
            using (StringFormat lf = new StringFormat())
            {
                lf.FormatFlags = StringFormatFlags.NoWrap; lf.Trimming = StringTrimming.EllipsisCharacter;
                bool bars = t <= 0.5f;
                string nat = nativeKnown ? Strings.T("gui_pv_native", nativeW, nativeH) : "";
                string state = bars ? Strings.T("gui_pv_bars") : Strings.T("gui_pv_stretch", (double)nativeW / srcW, (double)nativeH / srcH);
                float wNat = (nat.Length > 0) ? g.MeasureString(nat, ff, 10000, lf).Width : 0f;
                float wState = g.MeasureString(state, ff, 10000, lf).Width;
                float x0 = frame.Left, x1 = frame.Right;
                if (wNat + wState + 16f > x1 - x0) { x0 = 24f; x1 = Width - 24f; }       // the frame is too narrow: use the width of the panel
                bool oneRow = (wNat + wState + 16f <= x1 - x0);
                using (SolidBrush fb = new SolidBrush(Theme.Muted))
                using (SolidBrush sb2 = new SolidBrush(bars ? Theme.Warn : Theme.Ok))
                using (StringFormat rf = new StringFormat())
                {
                    rf.FormatFlags = StringFormatFlags.NoWrap; rf.Trimming = StringTrimming.EllipsisCharacter; rf.Alignment = StringAlignment.Far;
                    if (oneRow)
                    {
                        float fy = frame.Bottom + 16f;
                        if (nat.Length > 0) g.DrawString(nat, ff, fb, new RectangleF(x0, fy, wNat + 4f, 18f), lf);
                        g.DrawString(state, ff, sb2, new RectangleF(x0 + wNat + 8f, fy, x1 - x0 - wNat - 8f, 18f), rf);
                    }
                    else
                    {
                        float fy = frame.Bottom + 8f;
                        if (nat.Length > 0) g.DrawString(nat, ff, fb, new RectangleF(x0, fy, x1 - x0, 18f), lf);
                        g.DrawString(state, ff, sb2, new RectangleF(x0, fy + (nat.Length > 0 ? 17f : 0f), x1 - x0, 18f), lf);
                    }
                }
            }
        }
    }

    // =====================================================================
    //  Forms
    // =====================================================================
    public class DarkForm : Form
    {
        public DarkForm()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            DoubleBuffered = true;
            Icon ic = AppIcon.Get();
            if (ic != null) Icon = ic;
            else { try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { } }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        public static string L(string en, string ru) { return Strings.Lang == "ru" ? ru : en; }

        // a label that lets the mouse through, so that the window can be dragged by it
        public static Label MakeCaption(string text, Font font, Color color, Color back)
        {
            CaptionLabel l = new CaptionLabel();
            l.Text = text; l.Font = font; l.ForeColor = color; l.BackColor = back; l.AutoSize = false;
            return l;
        }

        public static Label MakeLabel(string text, Font font, Color color, Color back)
        {
            Label l = new Label();
            l.Text = text; l.Font = font; l.ForeColor = color; l.BackColor = back; l.AutoSize = false;
            return l;
        }
    }

    // "Keep this resolution?" with a countdown; no answer = revert
    public class KeepForm : DarkForm
    {
        private readonly Label lblQ, lblCount, lblKeys;
        private readonly ProgressLine bar = new ProgressLine();
        private readonly RedButton btnKeep = new RedButton();
        private readonly RedButton btnRevert = new RedButton();
        private readonly Timer timer = new Timer();
        private readonly int total;

        public KeepForm(string question, int seconds)
        {
            total = Math.Max(1, seconds);
            Text = Strings.T("gui_keep_title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            TopMost = true; ControlBox = false; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = true;
            ClientSize = new Size(480, 232);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);

            lblQ = MakeLabel(question, Theme.Display(16f), Theme.Text, Theme.Bg);
            lblQ.SetBounds(28, 26, 424, 32);
            lblCount = MakeLabel("", Theme.Ui(10f), Theme.Muted, Theme.Bg);
            lblCount.SetBounds(28, 64, 424, 24);
            bar.BackColor = Theme.Bg; bar.SetBounds(28, 96, 424, 8);
            btnKeep.Kind = BtnKind.Primary; btnKeep.Text = Strings.T("gui_keep"); btnKeep.DialogResult = DialogResult.Yes; btnKeep.BackColor = Theme.Bg;
            btnKeep.SetBounds(28, 128, 206, 46);
            btnRevert.Kind = BtnKind.Secondary; btnRevert.Text = Strings.T("gui_revert"); btnRevert.DialogResult = DialogResult.No; btnRevert.BackColor = Theme.Bg;
            btnRevert.SetBounds(246, 128, 206, 46);
            lblKeys = MakeLabel(L("Enter = keep     Esc = revert", "Enter = оставить     Esc = откатить"), Theme.Ui(9f), Theme.Dim, Theme.Bg);
            lblKeys.SetBounds(28, 190, 424, 22);
            Controls.Add(lblQ); Controls.Add(lblCount); Controls.Add(bar); Controls.Add(btnKeep); Controls.Add(btnRevert); Controls.Add(lblKeys);
            AcceptButton = btnKeep;
            CancelButton = btnRevert;

            timer.Interval = 250;
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            timer.Tick += delegate
            {
                double rest = (end - DateTime.UtcNow).TotalSeconds;
                Update2(rest);
                if (rest <= 0) { timer.Stop(); DialogResult = DialogResult.No; }
            };
            Update2(seconds);
            timer.Start();
        }

        private void Update2(double rest)
        {
            lblCount.Text = Strings.T("gui_revert_in", (int)Math.Ceiling(Math.Max(0, rest)));
            bar.Value = (float)(Math.Max(0, rest) / total);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); btnKeep.Focus(); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // message box in the same style
    public class DarkDialog : DarkForm
    {
        public DarkDialog(string title, string message, string yes, string no)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            int w = 480;
            Size ms = TextRenderer.MeasureText(message, Theme.Ui(10f), new Size(w - 56, 0), TextFormatFlags.WordBreak);
            int h = Math.Max(24, ms.Height + 8);
            Label lbl = MakeLabel(message, Theme.Ui(10f), Theme.Text, Theme.Bg);
            lbl.SetBounds(28, 28, w - 56, h);
            RedButton ok = new RedButton();
            ok.Kind = BtnKind.Primary; ok.Text = yes; ok.DialogResult = DialogResult.Yes; ok.BackColor = Theme.Bg;
            RedButton cancel = null;
            int by = 28 + h + 26;
            if (no != null)
            {
                cancel = new RedButton();
                cancel.Kind = BtnKind.Secondary; cancel.Text = no; cancel.DialogResult = DialogResult.No; cancel.BackColor = Theme.Bg;
                ok.SetBounds(28, by, (w - 56 - 12) / 2, 44);
                cancel.SetBounds(28 + (w - 56 - 12) / 2 + 12, by, (w - 56 - 12) / 2, 44);
                CancelButton = cancel;
            }
            else
            {
                ok.SetBounds(28, by, w - 56, 44);
                CancelButton = ok;
            }
            ClientSize = new Size(w, by + 44 + 26);
            Controls.Add(lbl); Controls.Add(ok); if (cancel != null) Controls.Add(cancel);
            AcceptButton = ok;
        }

        public static bool Ask(IWin32Window owner, string title, string message, string yes, string no)
        {
            using (DarkDialog d = new DarkDialog(title, message, yes, no)) { return d.ShowDialog(owner) == DialogResult.Yes; }
        }

        public static void Info(IWin32Window owner, string title, string message)
        {
            using (DarkDialog d = new DarkDialog(title, message, "OK", null)) { d.ShowDialog(owner); }
        }
    }

    public class Preset
    {
        public string Label; public int W; public int H;
        public int Hz;   // only the "original resolution" preset carries a refresh rate
        public Preset(string label, int w, int h) { Label = label; W = w; H = h; }
    }

    public class MainForm : DarkForm
    {
        private readonly IPlatform platform = new WinPlatform();
        private readonly LogoMark logo = new LogoMark();
        private readonly Label lblTitle, lblDisplay;
        private readonly Segmented segLang = new Segmented();
        private readonly ScreenPreview preview = new ScreenPreview();
        private readonly Panel pnlControls = new Panel();
        private readonly Card cardLog = new Card();
        private readonly List<Chip> chips = new List<Chip>();
        private readonly List<Preset> presets = new List<Preset>();
        private readonly TagLabel lblW = new TagLabel(), lblH = new TagLabel(), lblHz = new TagLabel();
        private readonly Label lblX, lblInfo;
        private readonly NumBox numW = new NumBox();
        private readonly NumBox numH = new NumBox();
        private readonly NumBox numHz = new NumBox();
        private readonly Toggle chkStretch = new Toggle();
        private readonly Toggle chkDisable = new Toggle();
        private readonly Toggle chkReenable = new Toggle();
        private readonly Label lblHint;
        private readonly RedButton btnApply = new RedButton();
        private readonly RedButton btnRestore = new RedButton();
        private readonly RedButton btnClose = new RedButton();
        private readonly WinButton btnMin = new WinButton(WinKind.Minimize), btnFull = new WinButton(WinKind.Full), btnX = new WinButton(WinKind.Close);
        private bool fullScreen;
        private Rectangle prevBounds;
        private readonly RichTextBox log = new RichTextBox();
        private readonly ToolTip tips = new ToolTip();
        private OriginalMode original;   // the resolution the user started with (remembered between runs)

        private volatile bool busy;
        private bool nvidiaPrimary;
        private bool building = true;
        private string displayLine = "";

        public MainForm()
        {
            Strings.Lang = "en";   // English by default; the EN/RU switch in the header changes it
            FormBorderStyle = FormBorderStyle.None;                       // no system frame: the window has its own buttons
            MaximizeBox = false;
            ClientSize = new Size(940, 760);
            ResizeRedraw = true;
            MinimumSize = SizeFromClientSize(new Size(940, 760));     // the layout below is made for 940 x 760 at least
            // open on a second monitor when there is one: it keeps its resolution while the primary display changes
            StartPosition = FormStartPosition.Manual;
            Screen target = Screen.PrimaryScreen;
            foreach (Screen s in Screen.AllScreens) { if (!s.Primary) { target = s; break; } }
            Rectangle wa = target.WorkingArea;
            Location = new Point(wa.Left + Math.Max(0, (wa.Width - Width) / 2), wa.Top + Math.Max(0, (wa.Height - Height) / 2));

            // ---- header ----
            logo.SetBounds(32, 24, 44, 44);
            lblTitle = MakeCaption("VALORANT Custom Resolution", Theme.Display(17f), Theme.Text, Theme.Bg);
            lblTitle.SetBounds(88, 20, 640, 30);
            lblDisplay = MakeCaption("", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            lblDisplay.SetBounds(89, 50, 640, 20);
            lblDisplay.AutoEllipsis = true;
            segLang.Items = new string[] { "EN", "RU" };
            segLang.SetBounds(940 - 32 - 88, 28, 88, 30);
            segLang.Selected = (Strings.Lang == "ru") ? 1 : 0;

            // ---- left: the screen you are shaping, and the presets under it ----
            preview.SetBounds(32, 96, 508, 288);

            // everything that can be changed lives in one panel, so that it can be locked while a run is going
            pnlControls.BackColor = Theme.Bg;
            pnlControls.SetBounds(0, 96, 940, 380);

            presets.Add(new Preset("", 0, 0));   // first place: the resolution the user started with (filled in by ApplyOriginal)
            presets.Add(new Preset("1566\u00D71080", 1566, 1080));
            presets.Add(new Preset("1440\u00D7993", 1440, 993));
            presets.Add(new Preset("1350\u00D7931", 1350, 931));
            presets.Add(new Preset("1280\u00D7880", 1280, 880));
            presets.Add(new Preset("1280\u00D7815", 1280, 815));
            presets.Add(new Preset("1150\u00D7793", 1150, 793));
            int cx = 32;
            foreach (Preset p in presets)
            {
                Chip c = new Chip();
                c.Text = p.Label; c.Tag = p; c.PW = p.W; c.PH = p.H;
                c.SetBounds(cx, 300, 68, 64);
                c.Click += delegate(object s, EventArgs ev)
                {
                    Preset q = (Preset)((Chip)s).Tag;
                    numW.Value = q.W; numH.Value = q.H;
                    if (q.Hz >= 24 && q.Hz <= 1000) numHz.Value = q.Hz;   // the original preset also brings back the original refresh rate
                };
                pnlControls.Controls.Add(c); chips.Add(c);
                cx += 73;
            }
            chips[0].Badge = true;

            // ---- right: the numbers ----
            lblW.SetBounds(576, 0, 150, 18);
            lblH.SetBounds(758, 0, 150, 18);
            lblHz.SetBounds(576, 90, 332, 18);
            numW.BackColor = Theme.Bg; numH.BackColor = Theme.Bg; numHz.BackColor = Theme.Bg;
            numW.SetBounds(576, 22, 150, 52); numW.Minimum = 320; numW.Maximum = 8192; numW.Value = 1566;
            lblX = MakeLabel("\u00D7", Theme.Display(18f), Theme.Dim, Theme.Bg); lblX.TextAlign = ContentAlignment.MiddleCenter; lblX.SetBounds(726, 22, 32, 52);
            numH.SetBounds(758, 22, 150, 52); numH.Minimum = 200; numH.Maximum = 8192; numH.Value = 1080;
            lblX.Name = "x";
            numHz.SetBounds(576, 112, 150, 52); numHz.Minimum = 24; numHz.Maximum = 1000; numHz.DecimalPlaces = 3; numHz.Value = 60;
            lblInfo = MakeLabel("", Theme.Ui(9f), Theme.Muted, Theme.Bg); lblInfo.SetBounds(576, 178, 332, 60); lblInfo.AutoEllipsis = true;
            EventHandler changed = delegate { if (!building) UpdateTiming(); };
            numW.ValueChanged += changed; numH.ValueChanged += changed; numHz.ValueChanged += changed;
            chkStretch.CheckedChanged += changed;

            // ---- right: the options ----
            chkStretch.SetBounds(576, 248, 332, 28); chkStretch.Checked = true;
            chkDisable.SetBounds(576, 282, 332, 46); chkDisable.Checked = true;
            chkReenable.SetBounds(604, 332, 304, 40); chkReenable.Checked = false;
            chkDisable.CheckedChanged += delegate { chkReenable.Enabled = chkDisable.Checked && !busy; };

            pnlControls.Controls.Add(lblW); pnlControls.Controls.Add(lblH); pnlControls.Controls.Add(lblHz);
            pnlControls.Controls.Add(numW); pnlControls.Controls.Add(lblX); pnlControls.Controls.Add(numH); pnlControls.Controls.Add(numHz);
            pnlControls.Controls.Add(lblInfo);
            pnlControls.Controls.Add(chkStretch); pnlControls.Controls.Add(chkDisable); pnlControls.Controls.Add(chkReenable);

            // ---- hint and buttons ----
            lblHint = MakeLabel("", Theme.Ui(9f), Theme.Dim, Theme.Bg);
            lblHint.SetBounds(32, 482, 876, 22);
            btnApply.Kind = BtnKind.Primary; btnApply.BackColor = Theme.Bg; btnApply.SetBounds(32, 512, 380, 48);
            btnRestore.Kind = BtnKind.Secondary; btnRestore.BackColor = Theme.Bg; btnRestore.SetBounds(424, 512, 320, 48);
            btnClose.Kind = BtnKind.Secondary; btnClose.BackColor = Theme.Bg; btnClose.SetBounds(756, 512, 152, 48);
            btnApply.Click += delegate { OnApply(); };
            btnRestore.Click += delegate { OnRestore(); };
            btnClose.Click += delegate { Close(); };
            btnFull.Click += delegate { ToggleFullScreen(); };
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            btnX.Click += delegate { Close(); };

            // ---- log ----
            cardLog.SetBounds(32, 580, 876, 148);
            log.SetBounds(16, 40, 844, 94);
            log.ReadOnly = true; log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.Field; log.ForeColor = Color.FromArgb(200, 202, 210);
            log.Font = Theme.Mono(9f); log.DetectUrls = false; log.TabStop = false;
            log.HandleCreated += delegate { Native.DarkScrollBars(log.Handle); };
            cardLog.Controls.Add(log);

            Controls.Add(logo); Controls.Add(lblTitle); Controls.Add(lblDisplay); Controls.Add(btnMin); Controls.Add(btnFull); Controls.Add(btnX); Controls.Add(segLang);
            Controls.Add(preview); Controls.Add(pnlControls); Controls.Add(lblHint);
            Controls.Add(btnApply); Controls.Add(btnRestore); Controls.Add(btnClose);
            Controls.Add(cardLog);
            AcceptButton = btnApply;

            segLang.SelectedChanged += delegate
            {
                Strings.Lang = (segLang.Selected == 1) ? "ru" : "en";
                ApplyLanguage();
            };

            LayoutAll();
            SizeChanged += delegate { if (!building) LayoutAll(); };
            building = false;
            LoadDisplay();
            ApplyLanguage();
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            FormClosed += delegate { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; };
            Shown += delegate
            {
                if (!IsAdmin()) Append(Strings.T("gui_not_admin"), Engine.LvWarn);
                try { if (platform.HasSavedMonitors()) Append(Strings.T("gui_monitors_off"), Engine.LvWarn); } catch (Exception) { }
            };
        }

        private const int WM_NCHITTEST = 0x84;
        private const int ResizeBand = 7;                        // px along the edge of the window that resize it
        private const int HeaderH = 96;                          // the header drags the window

        // no frame and no system buttons (the window has its own); the taskbar button can still minimise and restore it
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000;             // WS_MINIMIZEBOX | WS_SYSMENU
                return cp;
            }
        }

        // the edges resize the window and the empty part of the header drags it (the labels and the logo let the mouse through)
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && !fullScreen && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if (m.Result == (IntPtr)1)                       // HTCLIENT: the surface of the window itself, not a control on it
                {
                    long lp = m.LParam.ToInt64();
                    Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    bool l = p.X < ResizeBand, r = p.X >= ClientSize.Width - ResizeBand, t = p.Y < ResizeBand, b = p.Y >= ClientSize.Height - ResizeBand;
                    if (t && l) m.Result = (IntPtr)13;
                    else if (t && r) m.Result = (IntPtr)14;
                    else if (b && l) m.Result = (IntPtr)16;
                    else if (b && r) m.Result = (IntPtr)17;
                    else if (l) m.Result = (IntPtr)10;
                    else if (r) m.Result = (IntPtr)11;
                    else if (t) m.Result = (IntPtr)12;
                    else if (b) m.Result = (IntPtr)15;
                    else if (p.Y < HeaderH) m.Result = (IntPtr)2;     // HTCAPTION
                }
                return;
            }
            base.WndProc(ref m);
        }

        // a hairline round the window, now that there is no system frame
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!fullScreen)
            {
                using (Pen p = new Pen(Theme.CardBorder, 1f)) { e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1); }
            }
        }

        // Lays everything out for the current size of the window. The content fills the whole width (no empty bands at the sides): the left
        // column (preview, presets) takes the extra width, the right column grows a little; the preview and the log share the extra height (8 : 2);
        // the options stay level with the bottom of the presets.
        private void LayoutAll()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W < 400 || H < 400) return;                                   // minimised
            int x0 = 32;
            int CW = W - 2 * x0;
            int extra = Math.Max(0, H - 760);
            int RW = Math.Max(332, Math.Min(560, (int)(CW * 0.30)));          // the right column
            int LW = CW - RW - 36;                                            // the left column
            int previewH = 288 + (int)(extra * 0.8);
            int px = 8;                                                       // the panel with the presets and numbers stays off the edge, which resizes the window

            // header: the window buttons above the language switch, both against the right margin
            int right = x0 + CW;
            logo.SetBounds(x0, 24, 44, 44);
            lblTitle.SetBounds(x0 + 56, 20, Math.Max(100, CW - 56 - 130), 30);
            lblDisplay.SetBounds(x0 + 57, 50, Math.Max(100, CW - 57 - 130), 20);
            btnX.SetBounds(right - 36, 10, 36, 28);
            btnFull.SetBounds(right - 76, 10, 36, 28);
            btnMin.SetBounds(right - 116, 10, 36, 28);
            segLang.SetBounds(right - 88, 46, 88, 30);

            // left: the screen and the presets under it
            preview.SetBounds(x0, 96, LW, previewH);
            int panelH = previewH + 12 + 64;
            int tt = Math.Max(248, panelH - 124);                             // the three options end level with the presets
            pnlControls.SetBounds(px, 96, W - 2 * px, Math.Max(panelH, tt + 130));
            int n = Math.Max(1, chips.Count);
            int chipW = (LW - (n - 1) * 5) / n;
            for (int i = 0; i < chips.Count; i++) chips[i].SetBounds(x0 - px + i * (chipW + 5), previewH + 12, chipW, 64);

            // right: the numbers, the info and the options (inside the panel, whose top is at y = 96)
            int rx = x0 + LW + 36 - px;
            int half = (RW - 32) / 2;
            lblW.SetBounds(rx, 0, half, 18);
            numW.SetBounds(rx, 22, half, 52);
            lblX.SetBounds(rx + half, 22, 32, 52);
            lblH.SetBounds(rx + half + 32, 0, half, 18);
            numH.SetBounds(rx + half + 32, 22, half, 52);
            lblHz.SetBounds(rx, 90, RW, 18);
            numHz.SetBounds(rx, 112, half, 52);
            lblInfo.SetBounds(rx, 178, RW, 60);
            chkStretch.SetBounds(rx, tt, RW, 28);
            chkDisable.SetBounds(rx, tt + 34, RW, 46);
            chkReenable.SetBounds(rx + 28, tt + 84, RW - 28, 44);

            // hint, buttons, log
            int hintY = 96 + panelH + 22;
            int btnY = hintY + 30;
            lblHint.SetBounds(x0, hintY, CW, 22);
            int applyW = (int)(CW * 0.4338), restoreW = (int)(CW * 0.3653);
            int closeW = CW - applyW - restoreW - 24;
            btnApply.SetBounds(x0, btnY, applyW, 48);
            btnRestore.SetBounds(x0 + applyW + 12, btnY, restoreW, 48);
            btnClose.SetBounds(x0 + applyW + restoreW + 24, btnY, closeW, 48);
            int logY = btnY + 68;
            int logH = Math.Max(100, H - 32 - logY);
            cardLog.SetBounds(x0, logY, CW, logH);
            log.SetBounds(16, 40, CW - 32, Math.Max(40, logH - 54));
        }

        // F11 or the button in the header: the window covers the whole monitor it is on; Esc or the same key brings it back
        private void ToggleFullScreen()
        {
            if (!fullScreen)
            {
                prevBounds = Bounds;
                fullScreen = true;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                fullScreen = false;
                Bounds = prevBounds;
            }
            btnFull.Active = fullScreen;
            btnFull.Invalidate();
            Native.RoundCorners(Handle, !fullScreen);
            LayoutAll();
            Invalidate();
        }

        // the resolution changes while the window covers the screen (that is what this program does): fit it to the new screen
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (fullScreen) { Bounds = Screen.FromControl(this).Bounds; LayoutAll(); }
                }));
            }
            catch (Exception) { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11) { ToggleFullScreen(); return true; }
            if (keyData == Keys.Escape && fullScreen) { ToggleFullScreen(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { return true; }
        }

        private void LoadDisplay()
        {
            nvidiaPrimary = false;
            displayLine = "";
            try
            {
                foreach (DispInfo d in platform.Displays())
                {
                    if (!d.Primary) continue;
                    nvidiaPrimary = d.Desc != null && d.Desc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
                    displayLine = d.Name + "   \u00B7   " + d.Desc + "   \u00B7   " + d.W + "\u00D7" + d.H + " @ " + d.Hz + " Hz";
                    if (d.Hz >= 24 && d.Hz <= 1000) numHz.Value = d.Hz;
                    RememberOriginal(d);
                    break;
                }
            }
            catch (Exception ex) { displayLine = ex.Message; }
        }

        // works out (and remembers between runs) the resolution the user started with
        private void RememberOriginal(DispInfo d)
        {
            try
            {
                int sw = 0, sh = 0;
                if (nvidiaPrimary)
                {
                    try
                    {
                        platform.NvInit();
                        uint id = platform.NvDisplayId(d.Name);
                        double[] sg = platform.NvCurrentSignal(id, (uint)d.W, (uint)d.H, d.Hz);   // the signal that really goes to the monitor
                        if (sg != null && sg.Length >= 2) { sw = (int)sg[0]; sh = (int)sg[1]; }
                    }
                    catch (Exception) { sw = 0; sh = 0; }
                }
                original = OriginalMode.Resolve(d.Desc, d.Monitor, d.W, d.H, d.Hz, sw, sh);
            }
            catch (Exception) { original = null; }
        }

        // the first preset is always the original resolution
        private void ApplyOriginal()
        {
            if (chips.Count == 0) return;
            Chip c = chips[0];
            Preset p = (Preset)c.Tag;
            if (original != null)
            {
                p.W = original.W; p.H = original.H; p.Hz = original.Hz;
                p.Label = original.W + "\u00D7" + original.H;
            }
            else { p.W = 0; p.H = 0; p.Hz = 0; p.Label = "\u2014"; }
            c.Text = p.Label;
            c.PW = p.W; c.PH = p.H;
            int nw = (original != null) ? original.W : 0, nh = (original != null) ? original.H : 0;
            foreach (Chip k in chips) { k.NW = (nw > 0) ? nw : 16; k.NH = (nh > 0) ? nh : 9; k.Invalidate(); }
            c.Enabled = p.W > 0;
            tips.SetToolTip(c, p.W > 0 ? Strings.T("gui_orig_tip", p.W, p.H, p.Hz) : "");
        }

        private void ApplyLanguage()
        {
            building = true;
            Text = Strings.T("gui_title");
            lblDisplay.Text = nvidiaPrimary ? displayLine : (Strings.T("gui_display_none") + (displayLine.Length > 0 ? "   \u00B7   " + displayLine : ""));
            lblDisplay.ForeColor = nvidiaPrimary ? Theme.Muted : Theme.Warn;
            cardLog.Caption = Strings.T("gui_log"); cardLog.Invalidate();
            lblW.Text = Strings.T("gui_width");
            lblH.Text = Strings.T("gui_height");
            lblHz.Text = Strings.T("gui_hz");
            tips.SetToolTip(btnMin, L("Minimize", "\u0421\u0432\u0435\u0440\u043D\u0443\u0442\u044C"));
            tips.SetToolTip(btnFull, L("Full screen (F11)", "\u041F\u043E\u043B\u043D\u044B\u0439 \u044D\u043A\u0440\u0430\u043D (F11)"));
            tips.SetToolTip(btnX, L("Close", "\u0417\u0430\u043A\u0440\u044B\u0442\u044C"));
            chkStretch.Text = Strings.T("gui_stretch");
            chkDisable.Text = Strings.T("gui_disable");
            chkReenable.Text = Strings.T("gui_reenable");
            lblHint.Text = Strings.T("gui_hint");
            btnApply.Text = Strings.T("gui_apply");
            btnRestore.Text = Strings.T("gui_restore");
            btnClose.Text = Strings.T("gui_close");
            ApplyOriginal();
            building = false;
            UpdateTiming();
            Invalidate(true);   // repaint every control right away, not only the ones under the mouse
        }

        private void UpdateTiming()
        {
            bool stretch = chkStretch.Checked;
            Validation v = Cvt.Validate((int)numW.Value, (int)numH.Value, (double)numHz.Value, stretch);
            if (v.Errors.Count > 0)
            {
                lblInfo.ForeColor = Theme.Err;
                lblInfo.Text = v.Errors[0];
            }
            else if (stretch)
            {
                lblInfo.ForeColor = Theme.Muted;
                lblInfo.Text = Strings.T("gui_timing_stretch", v.Timing.W, v.Timing.H);
            }
            else
            {
                Timing t = v.Timing;
                lblInfo.ForeColor = Theme.Muted;
                string s = Strings.T("gui_timing", t.W, t.HTotal, t.H, t.VTotal, t.PclkMHz, t.RealHz);
                if (v.Caveats.Count > 0) { s += "\r\n" + Strings.T("lbl_warning") + ": " + v.Caveats[0]; lblInfo.ForeColor = Theme.Warn; }
                else if (v.Warnings.Count > 0) { s += "\r\n" + v.Warnings[0]; lblInfo.ForeColor = Theme.Warn; }
                lblInfo.Text = s;
            }
            foreach (Chip c in chips)
            {
                Preset p = (Preset)c.Tag;
                bool sel = (p.W == (int)numW.Value && p.H == (int)numH.Value);
                if (c.Selected != sel) { c.Selected = sel; c.Invalidate(); }
            }
            UpdatePreview();
            btnApply.Enabled = !busy && nvidiaPrimary && v.Errors.Count == 0;
        }

        // the screen in the window follows the numbers and the "stretch" switch
        private void UpdatePreview()
        {
            int nw = (original != null) ? original.W : 0, nh = (original != null) ? original.H : 0;
            preview.SetState(nw, nh, (int)numW.Value, (int)numH.Value, chkStretch.Checked);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            pnlControls.Enabled = !value; segLang.Enabled = !value;
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
                case Engine.LvOk: c = Theme.Ok; break;
                case Engine.LvWarn: c = Theme.Warn; break;
                case Engine.LvErr: c = Theme.Err; break;
                case Engine.LvHead: c = Theme.RedBright; break;
                case Engine.LvDim: c = Theme.Dim; break;
                default: c = Color.FromArgb(200, 202, 210); break;
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
                yes = DarkDialog.Ask(this, Strings.T("gui_confirm_title"), question, L("Yes", "Да"), L("No", "Нет"));
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
                DarkDialog.Info(this, Strings.T("gui_invalid"), v.Errors[0]);
                return;
            }
            if (v.Caveats.Count > 0)
            {
                if (!DarkDialog.Ask(this, Strings.T("gui_confirm_title"), v.Caveats[0], L("Continue", "Продолжить"), L("Cancel", "Отмена"))) return;
            }

            Options o = new Options();
            o.Width = w; o.Height = h; o.Hz = hz;
            o.Stretch = chkStretch.Checked;
            o.DisableMonitors = chkDisable.Checked;
            o.AskReenable = false;
            o.ReenableMonitors = chkReenable.Checked;
            o.TestSeconds = 15;
            o.RestoreHint = Strings.T("gui_restore_hint");

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
                    LoadDisplay();
                    ApplyLanguage();
                });
            });
            th.IsBackground = true;
            th.Start();
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