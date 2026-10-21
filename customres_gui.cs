// CustomResGui.cs - WinForms front end for the shared core (C# 5).
// Look: dark surfaces, red accent and the angular "V" of the application icon. All controls are drawn here,
// because the standard WinForms controls cannot be themed.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
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
        public static readonly Color Bg = Color.FromArgb(11, 11, 14);
        public static readonly Color Card = Color.FromArgb(21, 22, 27);
        public static readonly Color CardBorder = Color.FromArgb(38, 40, 48);
        public static readonly Color Field = Color.FromArgb(14, 15, 18);
        public static readonly Color FieldBorder = Color.FromArgb(46, 48, 58);
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
            Dwm(handle, 35, 0x000E0B0B);        // Windows 11: caption colour  = #0B0B0E  (COLORREF 0x00BBGGRR)
            Dwm(handle, 36, 0x00EFECEC);        // Windows 11: caption text
            Dwm(handle, 34, 0x002E1DE1);        // Windows 11: border colour   = #E11D2E
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
        public DarkControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        // these controls draw Text themselves; the base Control does not repaint when Text changes
        // (without this a new caption only showed up after the mouse moved over the control)
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }
        protected override void OnForeColorChanged(EventArgs e) { base.OnForeColorChanged(e); Invalidate(); }
    }

    // rounded panel
    public class Card : Panel
    {
        public string Caption = "";

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            g.Clear(Theme.Bg);
            using (GraphicsPath p = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14))
            using (SolidBrush b = new SolidBrush(Theme.Card))
            using (Pen pen = new Pen(Theme.CardBorder))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }
            if (Caption.Length > 0)
            {
                using (SolidBrush tick = new SolidBrush(Theme.Red)) { g.FillRectangle(tick, 20, 17, 3, 12); }
                using (Font f = Theme.UiBold(8.5f))
                using (SolidBrush tb = new SolidBrush(Theme.Muted))
                {
                    g.DrawString(Caption.ToUpper(), f, tb, 30, 14);
                }
            }
        }
    }

    // thin red-to-transparent line under the header
    public class AccentLine : DarkControl
    {
        public AccentLine() { BackColor = Theme.Bg; Height = 2; }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 1) return;
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Theme.Red, Theme.Bg, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(b, 0, 0, Width, Height);
            }
        }
    }

    // the logo of the icon: dark tile, red ring, angular V
    public class LogoMark : DarkControl
    {
        public LogoMark() { BackColor = Theme.Bg; Width = 60; Height = 60; }

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
            using (GraphicsPath tp = Gfx.Round(tile, s * 0.21f))
            {
                using (LinearGradientBrush bg = new LinearGradientBrush(tile, Color.FromArgb(30, 31, 38), Color.FromArgb(8, 8, 11), LinearGradientMode.Vertical)) { g.FillPath(bg, tp); }
                using (Pen ring = new Pen(Theme.Red, Math.Max(1.5f, s * 0.028f))) { g.DrawPath(ring, tp); }
            }
            PointF[] left = Scale(new float[] { 176, 236, 396, 236, 505, 548, 505, 822, 410, 822 }, k, 1, 1);
            PointF[] right = Scale(new float[] { 848, 236, 628, 236, 519, 548, 519, 822, 614, 822 }, k, 1, 1);
            // glow
            using (Pen glow = new Pen(Color.FromArgb(70, 255, 40, 60), s * 0.05f))
            {
                glow.LineJoin = LineJoin.Round;
                g.DrawPolygon(glow, left); g.DrawPolygon(glow, right);
            }
            using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(1, 1 + 236 * k, s, 590 * k), Color.FromArgb(255, 82, 96), Color.FromArgb(196, 18, 34), LinearGradientMode.Vertical))
            using (LinearGradientBrush rb = new LinearGradientBrush(new RectangleF(1, 1 + 236 * k, s, 590 * k), Color.FromArgb(190, 22, 40), Color.FromArgb(88, 8, 18), LinearGradientMode.Vertical))
            {
                g.FillPolygon(lb, left);
                g.FillPolygon(rb, right);
            }
            using (SolidBrush cut = new SolidBrush(Color.FromArgb(8, 8, 11)))
            {
                g.FillPolygon(cut, Scale(new float[] { 250, 236, 318, 236, 352, 330, 284, 330 }, k, 1, 1));
            }
        }
    }

    // selectable preset
    public class Chip : DarkControl
    {
        public bool Selected;
        public bool Badge;   // a small dot: this is the original resolution
        private bool hover;

        public Chip()
        {
            Cursor = Cursors.Hand; Height = 30; Width = 84;
            SetStyle(ControlStyles.Selectable, true); TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
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
            Color fill = Selected ? Color.FromArgb(52, 14, 22) : (hover ? Color.FromArgb(27, 28, 34) : Theme.Field);
            Color border = Selected ? Theme.Red : ((hover || Focused) ? Color.FromArgb(96, 99, 112) : Theme.FieldBorder);
            Color text = !Enabled ? Theme.Dim : (Selected ? Theme.RedBright : Theme.Text);
            using (GraphicsPath p = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8))
            using (SolidBrush fb = new SolidBrush(fill))
            using (Pen bp = new Pen(border))
            {
                g.FillPath(fb, p); g.DrawPath(bp, p);
            }
            using (StringFormat sf = Gfx.Centered())
            using (SolidBrush tb = new SolidBrush(text))
            {
                g.DrawString(Text, Font, tb, new RectangleF(0, 0, Width, Height), sf);
            }
            if (Badge && Enabled)
            {
                using (SolidBrush bb = new SolidBrush(Theme.RedBright)) { g.FillEllipse(bb, Width - 11, 4, 5, 5); }
            }
        }
    }

    // number input with a spinner
    public class NumBox : DarkControl
    {
        private readonly TextBox tb = new TextBox();
        private decimal val, min = 0, max = 100;
        private int decimals;
        private bool focused;
        private int hoverPart;                 // 1 = up, 2 = down
        public decimal Increment = 1;
        public event EventHandler ValueChanged;

        public NumBox()
        {
            Height = 38;
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = Theme.Field;
            tb.ForeColor = Theme.Text;
            tb.Font = Theme.Ui(11f);
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
            tb.Left = 14;
            tb.Width = Math.Max(10, Width - 14 - 30);
            tb.Top = Math.Max(0, (Height - tb.Height) / 2);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); }
        protected override void OnMouseWheel(MouseEventArgs e) { Step(e.Delta > 0 ? 1 : -1); }
        protected override void OnMouseLeave(EventArgs e) { hoverPart = 0; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int part = (e.X > Width - 28) ? (e.Y < Height / 2 ? 1 : 2) : 0;
            if (part != hoverPart) { hoverPart = part; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.X > Width - 28) Step(e.Y < Height / 2 ? 1 : -1);
            else tb.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            tb.Enabled = Enabled;
            using (GraphicsPath p = Gfx.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 9))
            using (SolidBrush fb = new SolidBrush(Theme.Field))
            using (Pen bp = new Pen(focused ? Theme.Red : Theme.FieldBorder, focused ? 1.6f : 1f))
            {
                g.FillPath(fb, p); g.DrawPath(bp, p);
            }
            float cx = Width - 15, cy = Height / 2f;
            using (SolidBrush up = new SolidBrush(hoverPart == 1 ? Theme.RedBright : Theme.Muted))
            using (SolidBrush dn = new SolidBrush(hoverPart == 2 ? Theme.RedBright : Theme.Muted))
            {
                g.FillPolygon(up, new PointF[] { new PointF(cx - 4.5f, cy - 3), new PointF(cx + 4.5f, cy - 3), new PointF(cx, cy - 8) });
                g.FillPolygon(dn, new PointF[] { new PointF(cx - 4.5f, cy + 3), new PointF(cx + 4.5f, cy + 3), new PointF(cx, cy + 8) });
            }
        }
    }

    // switch with a label
    public class Toggle : DarkControl
    {
        private bool chk;
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            Height = 28; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true); TabStop = true;
        }

        public bool Checked
        {
            get { return chk; }
            set { if (chk != value) { chk = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            RectangleF track = new RectangleF(1, (Height - 22) / 2f, 42, 22);
            using (GraphicsPath tp = Gfx.Round(track, 11))
            {
                if (chk)
                {
                    Color a = Enabled ? Theme.RedBright : Color.FromArgb(120, 52, 60);
                    Color b = Enabled ? Theme.Red : Color.FromArgb(90, 30, 38);
                    using (LinearGradientBrush tb = new LinearGradientBrush(track, a, b, LinearGradientMode.Vertical)) { g.FillPath(tb, tp); }
                }
                else
                {
                    using (SolidBrush tb = new SolidBrush(Enabled ? Color.FromArgb(44, 46, 55) : Color.FromArgb(30, 31, 37))) { g.FillPath(tb, tp); }
                }
                if (Focused) { using (Pen fp = new Pen(Theme.RedBright, 1.4f)) { g.DrawPath(fp, tp); } }
            }
            float kx = chk ? track.Right - 19 : track.X + 3;
            using (SolidBrush kb = new SolidBrush(chk ? (Enabled ? Color.FromArgb(250, 250, 252) : Color.FromArgb(170, 150, 152)) : (Enabled ? Color.FromArgb(158, 160, 170) : Color.FromArgb(80, 82, 90))))
            {
                g.FillEllipse(kb, kx, track.Y + 3, 16, 16);
            }
            using (StringFormat sf = Gfx.Left())
            using (SolidBrush tb = new SolidBrush(Enabled ? Theme.Text : Theme.Dim))
            {
                g.DrawString(Text, Font, tb, new RectangleF(54, 0, Width - 54, Height), sf);
            }
        }
    }

    public enum BtnKind { Primary, Secondary }

    public class RedButton : DarkControl, IButtonControl
    {
        public BtnKind Kind = BtnKind.Secondary;
        private bool hover, down, isDefault;
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
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
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
            if (Width < 4 || Height < 4) return;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color textColor;
            using (GraphicsPath p = Gfx.Round(r, 11))
            {
                if (Kind == BtnKind.Primary)
                {
                    Color top, bottom;
                    if (!Enabled) { top = Color.FromArgb(66, 28, 33); bottom = Color.FromArgb(50, 20, 25); textColor = Color.FromArgb(150, 110, 114); }
                    else if (down) { top = Color.FromArgb(205, 24, 42); bottom = Color.FromArgb(140, 10, 22); textColor = Color.White; }
                    else if (hover) { top = Color.FromArgb(255, 100, 114); bottom = Color.FromArgb(222, 30, 50); textColor = Color.White; }
                    else { top = Color.FromArgb(255, 79, 96); bottom = Color.FromArgb(196, 18, 34); textColor = Color.White; }
                    using (LinearGradientBrush b = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical)) { g.FillPath(b, p); }
                    if (Focused || isDefault) { using (Pen fp = new Pen(Color.FromArgb(255, 200, 206), 1.5f)) { g.DrawPath(fp, p); } }
                }
                else
                {
                    Color fill = !Enabled ? Theme.Field : (down ? Color.FromArgb(34, 35, 42) : (hover ? Color.FromArgb(27, 28, 34) : Theme.Field));
                    Color border = !Enabled ? Theme.CardBorder : ((hover || Focused) ? Theme.Red : Theme.FieldBorder);
                    textColor = Enabled ? Theme.Text : Theme.Dim;
                    using (SolidBrush b = new SolidBrush(fill)) { g.FillPath(b, p); }
                    using (Pen bp = new Pen(border, 1.4f)) { g.DrawPath(bp, p); }
                }
            }
            using (StringFormat sf = Gfx.Centered())
            using (SolidBrush tb = new SolidBrush(textColor))
            {
                g.DrawString(Text, Font, tb, new RectangleF(0, 0, Width, Height), sf);
            }
        }
    }

    // two-segment switch (language)
    public class Segmented : DarkControl
    {
        public string[] Items = new string[] { "A", "B" };
        private int selected;
        public event EventHandler SelectedChanged;

        public Segmented() { BackColor = Theme.Bg; Height = 32; Cursor = Cursors.Hand; }

        public int Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); } }
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
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Round(r, Height / 2f - 1))
            using (SolidBrush fb = new SolidBrush(Theme.Field))
            using (Pen bp = new Pen(Theme.FieldBorder))
            {
                g.FillPath(fb, p); g.DrawPath(bp, p);
            }
            float w = (Width - 6f) / Items.Length;
            for (int i = 0; i < Items.Length; i++)
            {
                RectangleF seg = new RectangleF(3 + i * w, 3, w, Height - 6);
                bool on = (i == selected);
                if (on)
                {
                    using (GraphicsPath sp = Gfx.Round(seg, seg.Height / 2f))
                    using (LinearGradientBrush b = new LinearGradientBrush(seg, Enabled ? Theme.RedBright : Color.FromArgb(120, 52, 60), Enabled ? Theme.Red : Color.FromArgb(90, 30, 38), LinearGradientMode.Vertical))
                    {
                        g.FillPath(b, sp);
                    }
                }
                using (StringFormat sf = Gfx.Centered())
                using (Font f = Theme.UiBold(9f))
                using (SolidBrush tb = new SolidBrush(on ? Color.White : (Enabled ? Theme.Muted : Theme.Dim)))
                {
                    g.DrawString(Items[i], f, tb, seg, sf);
                }
            }
        }
    }

    public class ProgressLine : DarkControl
    {
        private float value = 1f;
        public ProgressLine() { Height = 8; }
        public float Value { get { return value; } set { this.value = Math.Max(0f, Math.Min(1f, value)); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.Smooth(g);
            RectangleF track = new RectangleF(0, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath tp = Gfx.Round(track, track.Height / 2f))
            using (SolidBrush tb = new SolidBrush(Theme.Field)) { g.FillPath(tb, tp); }
            float w = (Width - 1) * value;
            if (w >= track.Height)
            {
                RectangleF fill = new RectangleF(0, 0.5f, w, Height - 1);
                using (GraphicsPath fp = Gfx.Round(fill, fill.Height / 2f))
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(2, Width), Height), Theme.Red, Theme.RedBright, LinearGradientMode.Horizontal))
                {
                    g.FillPath(b, fp);
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

            lblQ = MakeLabel(question, Theme.UiBold(14f), Theme.Text, Theme.Bg);
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
        private readonly AccentLine accent = new AccentLine();
        private readonly Card cardRes = new Card();
        private readonly Card cardOpt = new Card();
        private readonly Card cardLog = new Card();
        private readonly List<Chip> chips = new List<Chip>();
        private readonly List<Preset> presets = new List<Preset>();
        private readonly Label lblW, lblH, lblHz, lblX, lblInfo;
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
            ClientSize = new Size(720, 804);
            MinimumSize = new Size(700, 720);
            // open on a second monitor when there is one: it keeps its resolution while the primary display changes
            StartPosition = FormStartPosition.Manual;
            Screen target = Screen.PrimaryScreen;
            foreach (Screen s in Screen.AllScreens) { if (!s.Primary) { target = s; break; } }
            Rectangle wa = target.WorkingArea;
            Location = new Point(wa.Left + Math.Max(0, (wa.Width - Width) / 2), wa.Top + Math.Max(0, (wa.Height - Height) / 2));

            // ---- header ----
            logo.SetBounds(28, 22, 60, 60);
            lblTitle = MakeLabel("NVIDIA Custom Resolution", Theme.UiBold(18f), Theme.Text, Theme.Bg);
            lblTitle.SetBounds(104, 20, 440, 34);
            lblDisplay = MakeLabel("", Theme.Ui(9.5f), Theme.Muted, Theme.Bg);
            lblDisplay.SetBounds(106, 56, 470, 24);
            lblDisplay.AutoEllipsis = true;
            segLang.Items = new string[] { "EN", "RU" };
            segLang.SetBounds(720 - 28 - 96, 30, 96, 32);
            segLang.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            segLang.Selected = (Strings.Lang == "ru") ? 1 : 0;
            accent.SetBounds(0, 96, 720, 2);
            accent.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // ---- resolution card ----
            cardRes.SetBounds(24, 114, 672, 208);
            cardRes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            presets.Add(new Preset("", 0, 0));   // first place: the resolution the user started with (filled in by ApplyOriginal)
            presets.Add(new Preset("1566\u00D71080", 1566, 1080));
            presets.Add(new Preset("1440\u00D7993", 1440, 993));
            presets.Add(new Preset("1350\u00D7931", 1350, 931));
            presets.Add(new Preset("1280\u00D7880", 1280, 880));
            presets.Add(new Preset("1280\u00D7815", 1280, 815));
            presets.Add(new Preset("1150\u00D7793", 1150, 793));
            int cx = 20;
            foreach (Preset p in presets)
            {
                Chip c = new Chip();
                c.Text = p.Label; c.Tag = p;
                c.SetBounds(cx, 44, 86, 30);
                c.Click += delegate(object s, EventArgs ev)
                {
                    Preset q = (Preset)((Chip)s).Tag;
                    numW.Value = q.W; numH.Value = q.H;
                    if (q.Hz >= 24 && q.Hz <= 1000) numHz.Value = q.Hz;   // the original preset also brings back the original refresh rate
                };
                cardRes.Controls.Add(c); chips.Add(c);
                cx += 86 + 6;
            }
            chips[0].Badge = true;
            lblW = MakeLabel("", Theme.Ui(8.5f), Theme.Muted, Theme.Card); lblW.SetBounds(20, 90, 150, 18);
            lblH = MakeLabel("", Theme.Ui(8.5f), Theme.Muted, Theme.Card); lblH.SetBounds(216, 90, 150, 18);
            lblHz = MakeLabel("", Theme.Ui(8.5f), Theme.Muted, Theme.Card); lblHz.SetBounds(430, 90, 200, 18);
            numW.BackColor = Theme.Card; numH.BackColor = Theme.Card; numHz.BackColor = Theme.Card;
            numW.SetBounds(20, 110, 160, 38); numW.Minimum = 320; numW.Maximum = 8192; numW.Value = 1566;
            lblX = MakeLabel("\u00D7", Theme.Ui(14f), Theme.Dim, Theme.Card); lblX.TextAlign = ContentAlignment.MiddleCenter; lblX.SetBounds(182, 110, 32, 38);
            numH.SetBounds(216, 110, 160, 38); numH.Minimum = 200; numH.Maximum = 8192; numH.Value = 1080;
            lblX.Name = "x";
            numHz.SetBounds(430, 110, 160, 38); numHz.Minimum = 24; numHz.Maximum = 1000; numHz.DecimalPlaces = 3; numHz.Value = 60;
            lblInfo = MakeLabel("", Theme.Ui(9.5f), Theme.Muted, Theme.Card); lblInfo.SetBounds(20, 158, 632, 40);
            EventHandler changed = delegate { if (!building) UpdateTiming(); };
            numW.ValueChanged += changed; numH.ValueChanged += changed; numHz.ValueChanged += changed;
            chkStretch.CheckedChanged += changed;
            cardRes.Controls.Add(lblW); cardRes.Controls.Add(lblH); cardRes.Controls.Add(lblHz);
            cardRes.Controls.Add(numW); cardRes.Controls.Add(lblX); cardRes.Controls.Add(numH); cardRes.Controls.Add(numHz);
            cardRes.Controls.Add(lblInfo);

            // ---- options card ----
            cardOpt.SetBounds(24, 336, 672, 150);
            cardOpt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chkStretch.SetBounds(20, 40, 632, 28); chkStretch.Checked = true;
            chkDisable.SetBounds(20, 74, 632, 28); chkDisable.Checked = true;
            chkReenable.SetBounds(52, 108, 600, 28); chkReenable.Checked = false;   // monitors left disabled = no limits from the monitor = artifacts later
            chkDisable.CheckedChanged += delegate { chkReenable.Enabled = chkDisable.Checked && !busy; };
            cardOpt.Controls.Add(chkStretch); cardOpt.Controls.Add(chkDisable); cardOpt.Controls.Add(chkReenable);

            // ---- hint and buttons ----
            lblHint = MakeLabel("", Theme.Ui(9f), Theme.Dim, Theme.Bg);
            lblHint.SetBounds(28, 496, 664, 34);
            lblHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnApply.Kind = BtnKind.Primary; btnApply.BackColor = Theme.Bg; btnApply.SetBounds(24, 538, 316, 50);
            btnRestore.Kind = BtnKind.Secondary; btnRestore.BackColor = Theme.Bg; btnRestore.SetBounds(352, 538, 200, 50);
            btnClose.Kind = BtnKind.Secondary; btnClose.BackColor = Theme.Bg; btnClose.SetBounds(564, 538, 132, 50);
            btnApply.Click += delegate { OnApply(); };
            btnRestore.Click += delegate { OnRestore(); };
            btnClose.Click += delegate { Close(); };

            // ---- log ----
            cardLog.SetBounds(24, 604, 672, 176);
            cardLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            log.SetBounds(16, 40, 640, 124);
            log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            log.ReadOnly = true; log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.Card; log.ForeColor = Color.FromArgb(200, 202, 210);
            log.Font = Theme.Mono(9f); log.DetectUrls = false; log.TabStop = false;
            log.HandleCreated += delegate { Native.DarkScrollBars(log.Handle); };
            cardLog.Controls.Add(log);

            Controls.Add(logo); Controls.Add(lblTitle); Controls.Add(lblDisplay); Controls.Add(segLang); Controls.Add(accent);
            Controls.Add(cardRes); Controls.Add(cardOpt); Controls.Add(lblHint);
            Controls.Add(btnApply); Controls.Add(btnRestore); Controls.Add(btnClose);
            Controls.Add(cardLog);
            AcceptButton = btnApply;

            segLang.SelectedChanged += delegate
            {
                Strings.Lang = (segLang.Selected == 1) ? "ru" : "en";
                ApplyLanguage();
            };

            building = false;
            LoadDisplay();
            ApplyLanguage();
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            Shown += delegate
            {
                if (!IsAdmin()) Append(Strings.T("gui_not_admin"), Engine.LvWarn);
                try { if (platform.HasSavedMonitors()) Append(Strings.T("gui_monitors_off"), Engine.LvWarn); } catch (Exception) { }
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
            c.Enabled = p.W > 0;
            tips.SetToolTip(c, p.W > 0 ? Strings.T("gui_orig_tip", p.W, p.H, p.Hz) : "");
        }

        private void ApplyLanguage()
        {
            building = true;
            Text = Strings.T("gui_title");
            lblDisplay.Text = nvidiaPrimary ? displayLine : (Strings.T("gui_display_none") + (displayLine.Length > 0 ? "   \u00B7   " + displayLine : ""));
            lblDisplay.ForeColor = nvidiaPrimary ? Theme.Muted : Theme.Warn;
            cardRes.Caption = Strings.T("gui_resolution"); cardRes.Invalidate();
            cardOpt.Caption = Strings.T("gui_options"); cardOpt.Invalidate();
            cardLog.Caption = Strings.T("gui_log"); cardLog.Invalidate();
            lblW.Text = Strings.T("gui_width");
            lblH.Text = Strings.T("gui_height");
            lblHz.Text = Strings.T("gui_hz");
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
            btnApply.Enabled = !busy && nvidiaPrimary && v.Errors.Count == 0;
        }

        private void SetBusy(bool value)
        {
            busy = value;
            cardRes.Enabled = !value; cardOpt.Enabled = !value; segLang.Enabled = !value;
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