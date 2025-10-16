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
            var startTime = DateTime.UtcNow;
            PluginLog.Verbose($"[TilePainter] IconOrGlyph() called - canvas: {bb.Width}x{bb.Height}, glyph: '{glyph}', padding: {padPct}%, font: {font}");
            
            try
            {
                if (icon != null)
                {
                    var (x, y, side) = CenteredSquare(bb.Width, bb.Height, padPct);
                    PluginLog.Verbose($"[TilePainter] Drawing icon at ({x},{y}) with size {side}x{side}");
                    bb.DrawImage(icon, x, y, side, side);
                    PluginLog.Verbose("[TilePainter] Icon drawn successfully");
                }
                else
                {
                    PluginLog.Verbose($"[TilePainter] No icon provided - drawing glyph text '{glyph}' with font size {font}");
                    bb.DrawText(glyph, fontSize: font, color: new BitmapColor(255, 255, 255));
                    PluginLog.Verbose("[TilePainter] Glyph text drawn successfully");
                }
                
                var result = bb.ToImage();
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Info($"[TilePainter] Icon/glyph rendering completed in {elapsed:F2}ms");
                
                return result;
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[TilePainter] Exception during icon/glyph rendering after {elapsed:F2}ms: {ex.Message}");
                
                // Try to return something, even if it's just the basic bitmap
                try
                {
                    return bb.ToImage();
                }
                catch
                {
                    PluginLog.Error("[TilePainter] Failed to create fallback image - returning null");
                    return null!;
                }
            }
        }

        /// <summary>Set background image if provided; otherwise solid color.</summary>
        public static void Background(BitmapBuilder bb, BitmapImage? bgImage, BitmapColor fallback)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Verbose($"[TilePainter] Background() called - canvas: {bb.Width}x{bb.Height}, fallback color: R{fallback.R} G{fallback.G} B{fallback.B}");
            
            try
            {
                if (bgImage != null)
                {
                    PluginLog.Verbose("[TilePainter] Setting background image");
                    bb.SetBackgroundImage(bgImage);
                    PluginLog.Verbose("[TilePainter] Background image set successfully");
                }
                else
                {
                    PluginLog.Verbose($"[TilePainter] No background image - clearing with fallback color");
                    bb.Clear(fallback);
                    PluginLog.Verbose("[TilePainter] Background cleared with fallback color");
                }
                
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Verbose($"[TilePainter] Background setup completed in {elapsed:F2}ms");
            }
            catch (Exception ex)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Error($"[TilePainter] Exception during background setup after {elapsed:F2}ms: {ex.Message}");
                
                // Try fallback color as last resort
                try
                {
                    bb.Clear(fallback);
                    PluginLog.Info("[TilePainter] Applied fallback color after exception");
                }
                catch (Exception fallbackEx)
                {
                    PluginLog.Error($"[TilePainter] Failed to apply fallback color: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>Compute a centered square inside width×height with padding percentage.</summary>
        private static (Int32 x, Int32 y, Int32 side) CenteredSquare(Int32 w, Int32 h, Int32 padPct)
        {
            PluginLog.Verbose($"[TilePainter] CenteredSquare() called - dimensions: {w}x{h}, padding: {padPct}%");
            
            try
            {
                var pad = (Int32)Math.Round(Math.Min(w, h) * (padPct / 100.0));
                var side = Math.Min(w, h) - pad * 2;
                var x = (w - side) / 2;
                var y = (h - side) / 2;
                
                PluginLog.Verbose($"[TilePainter] Calculated square: position ({x},{y}), size {side}x{side}, padding {pad}px");
                
                return (x, y, side);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"[TilePainter] Exception in CenteredSquare calculation: {ex.Message}");
                
                // Return safe defaults
                var fallbackSide = Math.Min(w, h) / 2;
                var fallbackX = (w - fallbackSide) / 2;
                var fallbackY = (h - fallbackSide) / 2;
                
                PluginLog.Warning($"[TilePainter] Using fallback square: ({fallbackX},{fallbackY}), size {fallbackSide}x{fallbackSide}");
                return (fallbackX, fallbackY, fallbackSide);
            }
        }
    }
}