using UnityEngine;
using Verse;

namespace TorannMagic.Utils
{
    /// <summary>
    /// Shared drawing helpers for the Isekai-styled UI: dark panels with a thin
    /// dim-gold border and decorative bracket corners, matching the look of the
    /// Isekai RPG family windows (Runeforge-style).
    /// </summary>
    public static class TMIsekaiUI
    {
        public static readonly Color Bg = new Color(0.055f, 0.055f, 0.075f, 0.94f);
        public static readonly Color Border = new Color(0.40f, 0.34f, 0.20f, 0.9f);
        public static readonly Color Corner = new Color(0.85f, 0.65f, 0.30f, 0.95f);
        public static readonly Color HeaderGold = new Color(0.85f, 0.65f, 0.30f);
        public static readonly Color LabelGold = new Color(0.95f, 0.80f, 0.35f);
        public static readonly Color Divider = new Color(0.45f, 0.38f, 0.20f, 0.8f);

        /// <summary>Dark panel with a 1px border and bracket corners.</summary>
        public static void Panel(Rect r, bool fill = true)
        {
            if (fill)
            {
                Widgets.DrawBoxSolid(r, Bg);
            }
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width, 1f), Border);
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 1f, r.width, 1f), Border);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, 1f, r.height), Border);
            Widgets.DrawBoxSolid(new Rect(r.xMax - 1f, r.y, 1f, r.height), Border);

            const float c = 10f;
            const float t = 1.6f;
            // top-left
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, c, t), Corner);
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, t, c), Corner);
            // top-right
            Widgets.DrawBoxSolid(new Rect(r.xMax - c, r.y, c, t), Corner);
            Widgets.DrawBoxSolid(new Rect(r.xMax - t, r.y, t, c), Corner);
            // bottom-left
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - t, c, t), Corner);
            Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - c, t, c), Corner);
            // bottom-right
            Widgets.DrawBoxSolid(new Rect(r.xMax - c, r.yMax - t, c, t), Corner);
            Widgets.DrawBoxSolid(new Rect(r.xMax - t, r.yMax - c, t, c), Corner);
        }
    }
}
