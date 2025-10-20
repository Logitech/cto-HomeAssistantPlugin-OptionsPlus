// Tiles/TilePainter.cs
namespace Loupedeck.HomeAssistantPlugin
{
    using System;

    /// <summary>
    /// Small helpers to draw centered icons with padding and fallbacks.
    /// </summary>
    internal static class TilePainter
    {
        // ====================================================================
        // CONSTANTS - UI Rendering and Layout Constants
        // ====================================================================

        // --- Default UI Values ---
        private const Int32 DefaultPaddingPercentage = 10;             // Default padding percentage for icon layout
        private const Int32 DefaultFontSize = 56;                      // Default font size for glyph text

        // --- Color Constants ---
        private const Byte WhiteColorRed = 255;                        // Red component for white color
        private const Byte WhiteColorGreen = 255;                      // Green component for white color
        private const Byte WhiteColorBlue = 255;                       // Blue component for white color

        // --- Mathematical Constants ---
        private const Double PercentageToDecimalFactor = 100.0;        // Factor to convert percentage to decimal
        private const Int32 PaddingMultiplier = 2;                     // Multiplier for padding on both sides
        private const Int32 CenteringDivisor = 2;                      // Divisor for centering calculations
        private const Int32 FallbackSizeDivisor = 2;                   // Divisor for fallback size calculation

        /// <summary>Draw a centered square icon with % padding; if null, draws glyph text.</summary>
        public static BitmapImage IconOrGlyph(BitmapBuilder bb, BitmapImage? icon, String glyph, Int32 padPct = DefaultPaddingPercentage, Int32 font = DefaultFontSize)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Trace(() => $"[TilePainter] IconOrGlyph() called - canvas: {bb.Width}x{bb.Height}, glyph: '{glyph}', padding: {padPct}%, font: {font}");

            try
            {
                if (icon != null)
                {
                    var (x, y, side) = CenteredSquare(bb.Width, bb.Height, padPct);
                    PluginLog.Trace(() => $"[TilePainter] Drawing icon at ({x},{y}) with size {side}x{side}");
                    bb.DrawImage(icon, x, y, side, side);
                    PluginLog.Verbose("[TilePainter] Icon drawn successfully");
                }
                else
                {
                    PluginLog.Trace(() => $"[TilePainter] No icon provided - drawing glyph text '{glyph}' with font size {font}");
                    bb.DrawText(glyph, fontSize: font, color: new BitmapColor(WhiteColorRed, WhiteColorGreen, WhiteColorBlue));
                    PluginLog.Verbose("[TilePainter] Glyph text drawn successfully");
                }

                var result = bb.ToImage();
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                PluginLog.Debug(() => $"[TilePainter] Icon/glyph rendering completed in {elapsed:F2}ms");

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
            PluginLog.Trace(() => $"[TilePainter] Background() called - canvas: {bb.Width}x{bb.Height}, fallback color: R{fallback.R} G{fallback.G} B{fallback.B}");

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
            PluginLog.Trace(() => $"[TilePainter] CenteredSquare() called - dimensions: {w}x{h}, padding: {padPct}%");

            try
            {
                var pad = (Int32)Math.Round(Math.Min(w, h) * (padPct / PercentageToDecimalFactor));
                var side = Math.Min(w, h) - pad * PaddingMultiplier;
                var x = (w - side) / CenteringDivisor;
                var y = (h - side) / CenteringDivisor;

                PluginLog.Trace(() => $"[TilePainter] Calculated square: position ({x},{y}), size {side}x{side}, padding {pad}px");

                return (x, y, side);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"[TilePainter] Exception in CenteredSquare calculation: {ex.Message}");

                // Return safe defaults
                var fallbackSide = Math.Min(w, h) / FallbackSizeDivisor;
                var fallbackX = (w - fallbackSide) / CenteringDivisor;
                var fallbackY = (h - fallbackSide) / CenteringDivisor;

                PluginLog.Warning(() => $"[TilePainter] Using fallback square: ({fallbackX},{fallbackY}), size {fallbackSide}x{fallbackSide}");
                return (fallbackX, fallbackY, fallbackSide);
            }
        }
    }
}