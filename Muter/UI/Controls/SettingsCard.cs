using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Muter.UI.Controls
{
    // A card of related settings: a rounded surface with a 1 px border; its rows (any controls, usually SettingRow)
    // are stacked in the order they were added, separated by 1 px dividers inset 16 px. The card is as tall as its rows.
    public class SettingsCard : Panel
    {
        public SettingsCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
        }

        public void AddRow(Control row)
        {
            Controls.Add(row);
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            float s = DeviceDpi / 96f;
            int inset = (int)(SettingRow.Inset * s);
            int border = Math.Max(1, (int)s);
            int y = border;
            for (int i = 0; i < Controls.Count; i++)
            {
                Control row = Controls[i];
                if (i > 0)
                    y += border; // divider
                row.SetBounds(inset, y, Width - 2 * inset, 0, BoundsSpecified.X | BoundsSpecified.Y | BoundsSpecified.Width);
                int height = row is SettingRow settingRow ? settingRow.PreferredRowHeight() : row.Height;
                if (row.Height != height)
                    row.Height = height;
                y += height;
            }
            int total = y + border;
            if (Height != total)
            {
                Height = total;
                Parent?.PerformLayout();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = DeviceDpi / 96f;
            Theme.FillBox(g, ClientRectangle, 8 * s, Theme.Surface, Theme.Border, Math.Max(1, (int)s));
            int inset = (int)(16 * s);
            int border = Math.Max(1, (int)s);
            using (var brush = new SolidBrush(Theme.Border))
            {
                for (int i = 1; i < Controls.Count; i++)
                    g.FillRectangle(brush, inset, Controls[i].Top - border, Width - 2 * inset, border);
            }
        }
    }

    // A settings page: the page title, then sections (an optional header and a card) 20 px apart.
    public class SettingsPage : Panel
    {
        public SettingsPage()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Background;
        }

        // Bottom of the last card, in pixels.
        public int ContentHeight { get; private set; }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            float s = DeviceDpi / 96f;
            int y = 0;
            bool first = true;
            foreach (Control child in Controls)
            {
                if (first)
                {
                    // The page title.
                    child.Location = new Point(-(int)(3 * s), (int)(19 * s));
                    y = (int)(66 * s);
                    first = false;
                    continue;
                }
                if (child is SettingsCard)
                {
                    child.SetBounds(0, y, Width, 0, BoundsSpecified.Location | BoundsSpecified.Width);
                    y += child.Height + (int)(20 * s);
                }
                else
                {
                    // Section header: 24 px row, text inset 2 px.
                    child.Location = new Point(0, y);
                    y += (int)(24 * s);
                }
            }
            ContentHeight = y - (int)(20 * s);
        }
    }
}
