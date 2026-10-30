using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // A row of mutually exclusive choices (e.g. Small | Medium | Large).
    public class SegmentedControl : Control
    {
        private string[] segments = Array.Empty<string>();
        private int selectedIndex = -1;
        private int hoverIndex = -1;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Size = new Size(200, 30);
        }

        // Raised when the user picks a segment (not when SelectedIndex is set from code).
        public event EventHandler SelectionChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<string> Segments
        {
            get => segments;
            set
            {
                segments = (value ?? Array.Empty<string>()).ToArray();
                Invalidate();
            }
        }

        [DefaultValue(-1)]
        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                selectedIndex = value;
                Invalidate();
            }
        }

        public void Choose(int index)
        {
            if (index < 0 || index >= segments.Length || !Enabled)
                return;
            SelectedIndex = index;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            Choose(IndexAt(e.X));
            base.OnMouseClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = IndexAt(e.X);
            if (index != hoverIndex)
            {
                hoverIndex = index;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && selectedIndex > 0)
                Choose(selectedIndex - 1);
            else if (e.KeyCode == Keys.Right && selectedIndex < segments.Length - 1)
                Choose(selectedIndex + 1);
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        private int IndexAt(int x) => segments.Length == 0 ? -1 : Math.Max(0, Math.Min(segments.Length - 1, x * segments.Length / Math.Max(1, Width)));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 4 * s))
            using (var fill = new SolidBrush(Theme.ButtonFill))
            using (var pen = new Pen(Focused && ShowFocusCues ? Theme.TextPrimary : Theme.ButtonBorder, s))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            if (segments.Length == 0)
                return;

            float width = (Width - 1f) / segments.Length;
            for (int i = 0; i < segments.Length; i++)
            {
                var cell = new RectangleF(0.5f + i * width, 0.5f, width, Height - 1f);
                if (i == selectedIndex)
                {
                    using (GraphicsPath path = Theme.RoundedRectangle(RectangleF.Inflate(cell, -3 * s, -3 * s), 3 * s))
                    using (var fill = new SolidBrush(Enabled ? Theme.Accent : Theme.AccentDisabled))
                        g.FillPath(fill, path);
                }
                else if (i == hoverIndex)
                {
                    using (GraphicsPath path = Theme.RoundedRectangle(RectangleF.Inflate(cell, -3 * s, -3 * s), 3 * s))
                    using (var fill = new SolidBrush(Theme.ButtonHover))
                        g.FillPath(fill, path);
                }
                Color text = !Enabled ? Theme.TextDisabled : i == selectedIndex ? Theme.AccentText : Theme.TextPrimary;
                TextRenderer.DrawText(g, segments[i], Font, Rectangle.Round(cell), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }
}
