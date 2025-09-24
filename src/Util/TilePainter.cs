// Tiles/TilePainter.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    /// <summary>
    /// Small helpers to draw centered icons with padding and fallbacks.
    /// </summary>
    internal static class TilePainter
    {
        /// <summary>Draw a centered square icon with % padding; if null, draws glyph text.</summary>
        public static BitmapImage IconOrGlyph(BitmapBuilder bb, BitmapImage? icon, String glyph, Int32 padPct = 10, Int32 font = 56)
        {
            if (icon != null)
            {
                var (x, y, side) = CenteredSquare(bb.Width, bb.Height, padPct);
                bb.DrawImage(icon, x, y, side, side);
            }
            else
            {
                bb.DrawText(glyph, fontSize: font, color: new BitmapColor(255, 255, 255));
            }
            return bb.ToImage();
        }

        /// <summary>Set background image if provided; otherwise solid color.</summary>
        public static void Background(BitmapBuilder bb, BitmapImage? bgImage, BitmapColor fallback)
        {
            if (bgImage != null)
            {
                bb.SetBackgroundImage(bgImage);
            }
            else
            {
                bb.Clear(fallback);
            }
        }

        /// <summary>Compute a centered square inside width×height with padding percentage.</summary>
        private static (Int32 x, Int32 y, Int32 side) CenteredSquare(Int32 w, Int32 h, Int32 padPct)
        {
            var pad = (Int32)Math.Round(Math.Min(w, h) * (padPct / 100.0));
            var side = Math.Min(w, h) - pad * 2;
            var x = (w - side) / 2;
            var y = (h - side) / 2;
            return (x, y, side);
        }
    }
}