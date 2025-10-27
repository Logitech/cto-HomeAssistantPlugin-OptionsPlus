namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;

    using Loupedeck;
    using Loupedeck.HomeAssistantPlugin.Models;
    using Loupedeck.HomeAssistantPlugin.Services;

    public class HomeAssistantLightsDynamicFolder : PluginDynamicFolder
    {


        private record LightItem(
            String EntityId,
            String FriendlyName,
            String State,
            String DeviceId,
            String DeviceName,
            String Manufacturer,
            String Model
        );


        private readonly IconService _icons;




        // What the user adjusted last per light (drives preview mode)
        private readonly Dictionary<String, LookMode> _lookModeByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        private const String CmdStatus = "status";
        private const String CmdRetry = "retry";
        private const String CmdBack = "back"; // our own back
        private const String CmdArea = "area";


        private CancellationTokenSource _cts = new();

        private readonly Dictionary<String, LightItem> _lightsByEntity = new();


        private readonly CapabilityService _capSvc = new();


        private LightCaps GetCaps(String eid) =>
            this._lightStateManager?.GetCapabilities(eid) ?? new LightCaps();




        // view state
        private Boolean _inDeviceView = false;

        // Navigation levels
        private enum ViewLevel { Root, Area, Device }
        private ViewLevel _level = ViewLevel.Root;

        private String? _currentAreaId = null; // when in Area view

        // Area data
        private readonly Dictionary<String, String> _areaIdToName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, String> _entityToAreaId =
            new(StringComparer.OrdinalIgnoreCase);

        // Synthetic “no area” bucket
        private const String UnassignedAreaId = "!unassigned";

        private String? _currentEntityId = null;

        // action parameter prefixes
        private const String PfxDevice = "device:"; // device:<entity_id>
        private const String PfxActOn = "act:on:"; // act:on:<entity_id>
        private const String PfxActOff = "act:off:"; // act:off:<entity_id>

        // ====================================================================
        // CONSTANTS - All magic numbers extracted to named constants with documentation
        // ====================================================================

        // --- RGB and Color Value Constants ---
        private const Int32 RgbMinValue = 0;                    // Minimum RGB component value
        private const Int32 RgbMaxValue = 255;                  // Maximum RGB component value
        private const Int32 InvalidColorMarker = -1;            // Marker for invalid color calculations
        private const Int32 DefaultGrayValue = 64;              // Default gray color for inactive states
        private const Int32 BlackColorValue = 0;                // Black color (off state)
        private const Int32 WhiteColorValue = 255;              // White color value

        // --- Brightness and Percentage Constants ---
        private const Double PercentageScale = 100.0;          // Scale factor for percentage calculations
        private const Double BrightnessScale = 255.0;          // Scale factor for brightness calculations
        private const Int32 MidBrightness = 128;               // Mid-range brightness value
        private const Int32 MinBrightness = 1;                 // Minimum non-zero brightness
        private const Int32 MaxBrightness = 255;               // Maximum brightness value
        private const Int32 BrightnessOff = 0;                 // Brightness value for off state

        // --- Default HSB Color Values ---
        private const Double DefaultHue = 0;                   // Default hue value (red)
        private const Double DefaultSaturation = 100;          // Default saturation (full color)
        private const Double MinSaturation = 0;                // Minimum saturation (grayscale)
        private const Double MaxSaturation = 100;              // Maximum saturation (full color)
        private const Double FullColorValue = 100.0;           // Full value for HSB color rendering

        // --- Color Change Detection Thresholds ---
        private const Double HueEps = 0.5;                     // Threshold for hue change detection (degrees)
        private const Double SatEps = 0.5;                     // Threshold for saturation change detection (percent)

        // --- UI Display Constants ---
        private const Int32 UiPaddingSmall = 8;                // Small padding for UI elements
        private const Int32 UiPaddingMedium = 10;              // Medium padding for UI elements
        private const Int32 FontSizeSmall = 22;                // Small font size for UI text
        private const Int32 FontSizeMedium = 56;               // Medium font size for icons
        private const Int32 FontSizeLarge = 58;                // Large font size for icons
        private const Int32 UiColorRed = 30;                   // Red component for UI colors
        private const Int32 UiColorGreen = 220;                // Green component for UI colors
        private const Int32 UiColorBlue = 30;                  // Blue component for UI colors
        private const Int32 WheelCounterReset = 0;             // Reset value for wheel counter

        // --- Status Colors ---
        private const Int32 StatusOkRed = 0;                   // Online status red component
        private const Int32 StatusOkGreen = 160;               // Online status green component
        private const Int32 StatusOkBlue = 60;                 // Online status blue component
        private const Int32 StatusErrorRed = 200;              // Error status red component
        private const Int32 StatusErrorGreen = 30;             // Error status green component
        private const Int32 StatusErrorBlue = 30;              // Error status blue component


        // --- Network and Timing Constants ---
        private const Int32 AuthTimeoutSeconds = 60;           // Authentication timeout in seconds
        private static readonly TimeSpan EchoSuppressWindow = TimeSpan.FromSeconds(3); // Echo suppression window

        // --- WHEEL: constants & state
        private const String AdjBri = "adj:bri";               // brightness wheel
        private Int32 _wheelCounter = 0;                       // just for display/log when not in device view

        // ---- COLOR TEMP state (mirrors brightness pattern) ----
        private const String AdjTemp = "adj:ha-temp";          // wheel id
        private const Int32 TempStepMireds = 2;                // step per tick (≈smooth)
        private const Int32 DefaultMinMireds = 153;            // ~6500K
        private const Int32 DefaultMaxMireds = 500;            // ~2000K
        private const Int32 DefaultWarmMired = 370;            // ~2700K (UI fallback)

        // ===== HUE control (rotation-only) =====
        private const String AdjHue = "adj:ha-hue";           // wheel id

        // ===== SATURATION control =====
        private const String AdjSat = "adj:ha-sat";


        // Tune these if you want
        private const Int32 SendDebounceMs = 10;               // how long to wait after the last tick before sending

        private readonly HaEventListener _events = new();
        private CancellationTokenSource _eventsCts = new();





        private LightControlService? _lightSvc;
        private IHaClient? _ha; // adapter over HaWebSocketClient


        // --- Echo suppression: ignore HA frames shortly after we sent a command ---
        private readonly Dictionary<String, DateTime> _lastCmdAt =
            new(StringComparer.OrdinalIgnoreCase);






        // --- WHEEL: label shown next to the dial
        public override String GetAdjustmentDisplayName(String actionParameter, PluginImageSize _)
        {
            return actionParameter == AdjBri
                ? this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) ? "Brightness" : "Test Wheel"
                : actionParameter == AdjTemp
                ? "Color Temp"
                : actionParameter == AdjHue
                ? "Hue"
                : actionParameter == AdjSat ? "Saturation" : base.GetAdjustmentDisplayName(actionParameter, _);
        }



        // --- WHEEL: small value shown next to the dial
        public override String GetAdjustmentValue(String actionParameter)
        {
            // Brightness wheel
            if (actionParameter == AdjBri)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var effB = this.GetEffectiveBrightnessForDisplay(this._currentEntityId);
                    var pct = (Int32)Math.Round(effB * PercentageScale / BrightnessScale);
                    return $"{pct}%";
                }

                // Root view: tick counter for diagnostics
                return this._wheelCounter.ToString();
            }
            if (actionParameter == AdjSat)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var hsb = this._lightStateManager?.GetHsbValues(this._currentEntityId) ?? (DefaultHue, MinSaturation, BrightnessOff);
                    return $"{(Int32)Math.Round(HSBHelper.Clamp(hsb.S, MinSaturation, MaxSaturation))}%";
                }
                return "—%";
            }


            if (actionParameter == AdjHue)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var hsb = this._lightStateManager?.GetHsbValues(this._currentEntityId) ?? (DefaultHue, MinSaturation, BrightnessOff);
                    return $"{(Int32)Math.Round(HSBHelper.Wrap360(hsb.H))}°";
                }
                return "—°";
            }

            // Color Temperature wheel
            if (actionParameter == AdjTemp)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var temp = this._lightStateManager?.GetColorTempMired(this._currentEntityId);
                    if (temp.HasValue)
                    {
                        var k = ColorTemp.MiredToKelvin(temp.Value.Cur);
                        return $"{k}K";
                    }
                    return "— K"; // no cache yet → neutral placeholder
                }

                // Root view: hint the per-tick step size
                return $"±{TempStepMireds} mired";
            }

            return base.GetAdjustmentValue(actionParameter);
        }




        // Simulate how the light *actually* looks, honoring last-look mode (HS vs Temp),
        // using blackbody CCT for Temp and gamma-correct dimming for both modes.
        private (Int32 R, Int32 G, Int32 B) GetSimulatedLightRgbForCurrentDevice()
        {
            if (!this._inDeviceView || String.IsNullOrEmpty(this._currentEntityId))
            {
                return (DefaultGrayValue, DefaultGrayValue, DefaultGrayValue);
            }

            var eid = this._currentEntityId;

            // Effective brightness (0 if off)
            var effB = this.GetEffectiveBrightnessForDisplay(eid); // 0..255
            if (effB <= BrightnessOff)
            {
                return (BlackColorValue, BlackColorValue, BlackColorValue);
            }

            var prefer = this._lookModeByEntity.TryGetValue(eid, out var pref) ? pref : LookMode.Hs;

            // --- Preferred: HS look ---
            (Int32 R, Int32 G, Int32 B) RenderFromHs()
            {
                var hsb = this._lightStateManager?.GetHsbValues(eid) ?? (DefaultHue, MinSaturation, BrightnessOff);
                if (hsb.B <= BrightnessOff && effB <= BrightnessOff)
                {
                    return (InvalidColorMarker, InvalidColorMarker, InvalidColorMarker);
                }

                // Get full-brightness sRGB from your HSV/HSB helper, then dim in linear space
                var (sr, sg, sb) = HSBHelper.HsbToRgb(
                    HSBHelper.Wrap360(hsb.H),
                    HSBHelper.Clamp(Math.Max(MinSaturation, hsb.S), MinSaturation, MaxSaturation),
                    FullColorValue // full value; we'll apply brightness correctly afterwards
                );

                // Optional: tiny desaturation at very low brightness for human-perception feel
                // (keeps extreme colors from looking too "inky" when almost off)
                // double l = effB / 255.0;
                // if (l < 0.10) { sr = (int)(sr * (0.9 + l)); sg = (int)(sg * (0.9 + l)); sb = (int)(sb * (0.9 + l)); }

                return ColorConv.ApplyBrightnessLinear((sr, sg, sb), effB);
            }

            // --- Preferred: Color Temp look ---
            (Int32 R, Int32 G, Int32 B) RenderFromTemp()
            {
                var temp = this._lightStateManager?.GetColorTempMired(eid);
                if (!temp.HasValue)
                {
                    return (InvalidColorMarker, InvalidColorMarker, InvalidColorMarker);
                }

                var k = ColorTemp.MiredToKelvin(temp.Value.Cur);
                var srgb = ColorTemp.KelvinToSrgb(k);      // blackbody approximate in sRGB
                return ColorConv.ApplyBrightnessLinear(srgb, effB);  // dim in linear light, back to sRGB
            }

            (Int32 R, Int32 G, Int32 B) rgb;

            rgb = (prefer == LookMode.Hs) ? RenderFromHs() : RenderFromTemp();
            if (rgb.R >= RgbMinValue)
            {
                return rgb;
            }

            rgb = (prefer == LookMode.Hs) ? RenderFromTemp() : RenderFromHs();
            if (rgb.R >= RgbMinValue)
            {
                return rgb;
            }

            // Fallback: neutral gray at brightness
            return (effB, effB, effB);
        }




        public override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == AdjBri)
            {
                var bri = MidBrightness;
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    bri = this.GetEffectiveBrightnessForDisplay(this._currentEntityId);
                }

                var pct = (Int32)Math.Round(bri * PercentageScale / BrightnessScale);

                Int32 r, g, b;
                if (bri <= BrightnessOff)
                { r = g = b = BlackColorValue; }
                else
                { r = Math.Min(UiColorRed + pct * 2, RgbMaxValue); g = Math.Min(UiColorRed + pct, UiColorGreen); b = UiColorBlue; }

                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Brightness), "☀", padPct: UiPaddingMedium, font: FontSizeLarge);
            }

            if (actionParameter == AdjSat)
            {
                var (r, g, b) = this.GetSimulatedLightRgbForCurrentDevice();
                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Saturation), "S", padPct: UiPaddingSmall, font: FontSizeMedium);
            }

            if (actionParameter == AdjTemp)
            {
                var (r, g, b) = this.GetSimulatedLightRgbForCurrentDevice();
                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Temperature), "⟷", padPct: UiPaddingMedium, font: FontSizeLarge);
            }

            if (actionParameter == AdjHue)
            {
                var (r, g, b) = this.GetSimulatedLightRgbForCurrentDevice();
                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Hue), "H", padPct: UiPaddingSmall, font: FontSizeMedium);
            }

            return base.GetAdjustmentImage(actionParameter, imageSize);
        }





        // --- WHEEL: rotation handler using Command Pattern
        public override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (diff == 0)
            {
                return;
            }

            try
            {
                // Handle brightness adjustment - special case for root view counter
                if (actionParameter == AdjBri)
                {
                    if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                    {
                        // Device view: use command pattern
                        var brightnessCommand = this._adjustmentCommandFactory?.CreateBrightnessCommand();
                        brightnessCommand?.Execute(this._currentEntityId, diff);
                    }
                    else
                    {
                        // Root view: counter behavior for diagnostics
                        this._wheelCounter += diff;
                        this.AdjustmentValueChanged(actionParameter);
                    }
                    return;
                }

                // For other adjustments, only work in device view
                if (!this._inDeviceView || String.IsNullOrEmpty(this._currentEntityId))
                {
                    return;
                }

                // Use command pattern for device adjustments
                IAdjustmentCommand? adjustmentCommand = actionParameter switch
                {
                    AdjSat => this._adjustmentCommandFactory?.CreateSaturationCommand(),
                    AdjHue => this._adjustmentCommandFactory?.CreateHueCommand(),
                    AdjTemp => this._adjustmentCommandFactory?.CreateTemperatureCommand(),
                    _ => null
                };

                adjustmentCommand?.Execute(this._currentEntityId, diff);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[ApplyAdjustment] Exception for {actionParameter}");
                HealthBus.Error("Adjustment error");
                this.AdjustmentValueChanged(actionParameter);
            }
        }









        // New service dependencies
        private IHomeAssistantDataService? _dataService;
        private IHomeAssistantDataParser? _dataParser;
        private ILightStateManager? _lightStateManager;
        private IRegistryService? _registryService;

        // Command pattern for adjustment handling
        private IAdjustmentCommandFactory? _adjustmentCommandFactory;

        public HomeAssistantLightsDynamicFolder()
        {
            PluginLog.Info("[LightsDynamicFolder] Constructor START");

            this.DisplayName = "All Light Controls";
            this.GroupName = "Lights";

            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.Bulb,        "light_bulb_icon.svg" },
                { IconId.Back,        "back_icon.svg" },
                { IconId.BulbOn,      "light_on_icon.svg" },
                { IconId.BulbOff,     "light_off_icon.svg" },
                { IconId.Brightness,  "brightness_icon.svg" },
                { IconId.Retry,       "reload_icon.svg" },
                { IconId.Saturation,  "saturation_icon.svg" },
                { IconId.Issue,       "issue_status_icon.svg" },
                { IconId.Temperature, "temperature_icon.svg" },
                { IconId.Online,      "online_status_icon.png" },
                { IconId.Hue,         "hue_icon.svg" },
                { IconId.Area,         "area_icon.svg" },
            });

            PluginLog.Info($"[LightsDynamicFolder] Constructor - this.Plugin is null: {this.Plugin == null}");
            PluginLog.Info("[LightsDynamicFolder] Constructor completed - dependency initialization deferred to OnLoad()");
        }

        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) =>
            PluginDynamicFolderNavigation.None;


        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _)
        {
            // Always show Back + Status
            yield return this.CreateCommandName(CmdBack);
            yield return this.CreateCommandName(CmdStatus);

            if (this._level == ViewLevel.Device && !String.IsNullOrEmpty(this._currentEntityId))
            {
                var caps = this.GetCaps(this._currentEntityId);

                yield return this.CreateCommandName($"{PfxActOn}{this._currentEntityId}");
                yield return this.CreateCommandName($"{PfxActOff}{this._currentEntityId}");


                if (caps.Brightness)
                {
                    yield return this.CreateAdjustmentName(AdjBri);
                }

                if (caps.ColorTemp)
                {
                    yield return this.CreateAdjustmentName(AdjTemp);
                }

                if (caps.ColorHs)
                { yield return this.CreateAdjustmentName(AdjHue); yield return this.CreateAdjustmentName(AdjSat); }
                yield break;
            }

            if (this._level == ViewLevel.Area && !String.IsNullOrEmpty(this._currentAreaId))
            {
                // Lights for current area
                foreach (var kv in this._lightsByEntity)
                {
                    if (this._entityToAreaId.TryGetValue(kv.Key, out var aid) && String.Equals(aid, this._currentAreaId, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return this.CreateCommandName($"{PfxDevice}{kv.Key}");
                    }
                }
                yield break;
            }

            // ROOT: list areas that actually have lights
            // (optional) include Retry at root
            var areaIds = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (var eid in this._lightsByEntity.Keys)
            {
                if (this._entityToAreaId.TryGetValue(eid, out var aid))
                {
                    areaIds.Add(aid);
                }
            }

            // Order by area name
            var ordered = areaIds
                .Select(aid => (aid, name: this._areaIdToName.TryGetValue(aid, out var n) ? n : aid))
                .OrderBy(t => t.name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var (aid, _) in ordered)
            {
                yield return this.CreateCommandName($"{CmdArea}{aid}");
            }

            yield return this.CreateCommandName(CmdRetry);
        }





        public override String GetCommandDisplayName(String actionParameter, PluginImageSize _)
        {
            if (actionParameter == CmdBack)
            {
                return "Back";
            }

            if (actionParameter == CmdStatus)
            {
                return String.Empty; // no caption under status
            }

            if (actionParameter == CmdRetry)
            {
                return "Retry";
            }

            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxDevice.Length);
                return this._lightsByEntity.TryGetValue(entityId, out var li) ? li.FriendlyName : entityId;
            }
            if (actionParameter.StartsWith(PfxActOn, StringComparison.OrdinalIgnoreCase))
            {
                return "On";
            }
            if (actionParameter.StartsWith(CmdArea, StringComparison.OrdinalIgnoreCase))
            {
                var areaId = actionParameter.Substring(CmdArea.Length);
                return this._areaIdToName.TryGetValue(areaId, out var name) ? name : areaId;
            }

            return actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase) ? "Off" : actionParameter;
        }

        private Int32 GetEffectiveBrightnessForDisplay(String entityId)
        {
            // Use the LightStateManager for getting effective brightness
            var effectiveBrightness = this._lightStateManager?.GetEffectiveBrightness(entityId) ?? BrightnessOff;
            var isOn = this._lightStateManager?.IsLightOn(entityId) ?? false;
            var cachedHsb = this._lightStateManager?.GetHsbValues(entityId) ?? (DefaultHue, MinSaturation, BrightnessOff);

            PluginLog.Verbose(() => $"[GetEffectiveBrightnessForDisplay] {entityId}: isOn={isOn}, effectiveBrightness={effectiveBrightness}, cachedB={cachedHsb.B}");
            return effectiveBrightness;
        }

        // Paint the tile: green when OK, red on error
        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {




            if (actionParameter == CmdBack)
            {
                return this._icons.Get(IconId.Back);
            }
            if (actionParameter == CmdRetry)
            {
                return this._icons.Get(IconId.Retry);
            }

            // STATUS (unchanged)
            if (actionParameter == CmdStatus)
            {
                var ok = HealthBus.State == HealthState.Ok;
                using (var bb = new BitmapBuilder(imageSize))
                {
                    var okImg = this._icons.Get(IconId.Online);
                    var issueImg = this._icons.Get(IconId.Issue);
                    TilePainter.Background(bb, ok ? okImg : issueImg, ok ? new BitmapColor(StatusOkRed, StatusOkGreen, StatusOkBlue) : new BitmapColor(StatusErrorRed, StatusErrorGreen, StatusErrorBlue));
                    bb.DrawText(ok ? "ONLINE" : "ISSUE", fontSize: FontSizeSmall, color: new BitmapColor(WhiteColorValue, WhiteColorValue, WhiteColorValue));
                    return bb.ToImage();
                }
            }


            // DEVICE tiles (light bulbs)
            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                return this._icons.Get(IconId.Bulb);
            }

            if (actionParameter.StartsWith(CmdArea, StringComparison.OrdinalIgnoreCase))
            {
                return this._icons.Get(IconId.Area);
            }

            // ACTION tiles
            if (actionParameter.StartsWith(PfxActOn, StringComparison.OrdinalIgnoreCase))
            {
                return this._icons.Get(IconId.BulbOn);
            }
            if (actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase))
            {
                return this._icons.Get(IconId.BulbOff);
            }

            // Fallback for any unhandled cases - return a default icon
            return this._icons.Get(IconId.Bulb);
        }





        public override void RunCommand(String actionParameter)
        {
            PluginLog.Debug(() => $"RunCommand: {actionParameter}");

            try
            {
                // Use switch expression to dispatch to focused command handlers
                var handled = actionParameter switch
                {
                    CmdBack => this.HandleBackCommand(),
                    CmdRetry => this.HandleRetryCommand(),
                    CmdStatus => this.HandleStatusCommand(),
                    var cmd when cmd.StartsWith(CmdArea, StringComparison.OrdinalIgnoreCase) => this.HandleAreaCommand(cmd),
                    var cmd when cmd.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase) => this.HandleDeviceCommand(cmd),
                    var cmd when cmd.StartsWith(PfxActOn, StringComparison.OrdinalIgnoreCase) => this.HandleLightOnCommand(cmd),
                    var cmd when cmd.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase) => this.HandleLightOffCommand(cmd),
                    _ => false
                };

                if (!handled)
                {
                    PluginLog.Warning($"Unhandled command: {actionParameter}");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[RunCommand] Exception for {actionParameter}");
                HealthBus.Error("Command error");
            }
        }

        private Boolean HandleBackCommand()
        {
            if (this._level == ViewLevel.Device)
            {
                if (!String.IsNullOrEmpty(this._currentEntityId))
                {
                    this._lightSvc?.CancelPending(this._currentEntityId);
                }
                this._inDeviceView = false;
                this._currentEntityId = null;
                this._level = ViewLevel.Area;
                this.ButtonActionNamesChanged();
                this.EncoderActionNamesChanged();
            }
            else if (this._level == ViewLevel.Area)
            {
                this._currentAreaId = null;
                this._level = ViewLevel.Root;
                this.ButtonActionNamesChanged();
                this.EncoderActionNamesChanged();
            }
            else // Root
            {
                this.Close();
            }
            return true;
        }

        private Boolean HandleRetryCommand()
        {
            this.AuthenticateSync();
            return true;
        }

        private Boolean HandleStatusCommand()
        {
            var ok = HealthBus.State == HealthState.Ok;
            this.Plugin.OnPluginStatusChanged(ok ? PluginStatus.Normal : PluginStatus.Error,
                ok ? "Home Assistant is connected." : HealthBus.LastMessage);
            return true;
        }

        private Boolean HandleAreaCommand(String actionParameter)
        {
            var areaId = actionParameter.Substring(CmdArea.Length);
            if (this._areaIdToName.ContainsKey(areaId) || String.Equals(areaId, UnassignedAreaId, StringComparison.OrdinalIgnoreCase))
            {
                this._currentAreaId = areaId;
                this._level = ViewLevel.Area;
                this._inDeviceView = false;
                this._currentEntityId = null;
                this.ButtonActionNamesChanged();
                this.EncoderActionNamesChanged();
                PluginLog.Debug(() => $"ENTER area view: {areaId}");
                return true;
            }
            return false;
        }

        private Boolean HandleDeviceCommand(String actionParameter)
        {
            PluginLog.Info($"Entering Device view");
            var entityId = actionParameter.Substring(PfxDevice.Length);
            if (!this._lightsByEntity.ContainsKey(entityId))
            {
                return false;
            }

            this._inDeviceView = true;
            this._level = ViewLevel.Device;
            this._currentEntityId = entityId;
            this._wheelCounter = WheelCounterReset; // avoids showing previous ticks anywhere

            // Initialize defaults in LightStateManager if needed - it handles all state internally
            var caps = this.GetCaps(entityId);
            var hsb = this._lightStateManager?.GetHsbValues(entityId) ?? (DefaultHue, MinSaturation, BrightnessOff);
            if (hsb.B <= BrightnessOff && hsb.H == DefaultHue && hsb.S == MinSaturation)
            {
                // Light not yet initialized in state manager, set defaults
                this._lightStateManager?.SetCachedBrightness(entityId, BrightnessOff);
                this._lightStateManager?.UpdateHsColor(entityId, DefaultHue, MinSaturation);
            }

            // Initialize color temp cache if device supports it
            if (caps.ColorTemp)
            {
                var temp = this._lightStateManager?.GetColorTempMired(entityId);
                if (!temp.HasValue)
                {
                    this._lightStateManager?.SetCachedTempMired(entityId, DefaultMinMireds, DefaultMaxMireds, DefaultWarmMired);
                }
            }

            this.ButtonActionNamesChanged();       // swap to device actions

            // 🔸 brightness-style UI refresh: force all wheels to redraw immediately
            this.AdjustmentValueChanged(AdjBri);
            this.AdjustmentValueChanged(AdjTemp);
            this.AdjustmentValueChanged(AdjHue);
            this.AdjustmentValueChanged(AdjSat);

            PluginLog.Debug(() => $"ENTER device view: {entityId}  level={this._level} inDevice={this._inDeviceView}");
            return true;
        }

        private Boolean HandleLightOnCommand(String actionParameter)
        {
            var entityId = actionParameter.Substring(PfxActOn.Length);

            // Optimistic: mark ON immediately using LightStateManager (UI becomes responsive)
            var caps = this.GetCaps(entityId);
            Int32? brightness = null;
            if (caps.Brightness)
            {
                var hsb = this._lightStateManager?.GetHsbValues(entityId) ?? (DefaultHue, MinSaturation, BrightnessOff);
                brightness = HSBHelper.Clamp(Math.Max(MinBrightness, hsb.B), MinBrightness, MaxBrightness);
            }

            this._lightStateManager?.UpdateLightState(entityId, true, brightness);
            PluginLog.Debug(() => $"[TurnOn] Updated LightStateManager for {entityId}: ON=true, brightness={brightness}");

            this.CommandImageChanged(this.CreateCommandName($"{PfxDevice}{entityId}"));
            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                this.AdjustmentValueChanged(AdjBri);
                this.AdjustmentValueChanged(AdjSat);
                this.AdjustmentValueChanged(AdjHue);
                this.AdjustmentValueChanged(AdjTemp);
            }

            JsonElement? data = null;
            if (brightness.HasValue)
            {
                data = JsonSerializer.SerializeToElement(new { brightness = brightness.Value });
            }
            this.MarkCommandSent(entityId);
            _ = this._lightSvc?.TurnOnAsync(entityId, data);
            return true;
        }

        private Boolean HandleLightOffCommand(String actionParameter)
        {
            var entityId = actionParameter.Substring(PfxActOff.Length);

            // Optimistic: mark OFF using LightStateManager (don't touch cached brightness)
            this._lightStateManager?.UpdateLightState(entityId, false);
            PluginLog.Debug(() => $"[TurnOff] Updated LightStateManager for {entityId}: ON=false");

            this.CommandImageChanged(this.CreateCommandName($"{PfxDevice}{entityId}"));
            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                this.AdjustmentValueChanged(AdjBri);
                this.AdjustmentValueChanged(AdjSat);
                this.AdjustmentValueChanged(AdjHue);
                this.AdjustmentValueChanged(AdjTemp);
            }

            this._lightSvc?.TurnOffAsync(entityId);
            this.MarkCommandSent(entityId);
            return true;
        }










        // 🔧 return bools here:
        public override Boolean Load()
        {
            PluginLog.Info("[LightsDynamicFolder] Load() START");
            PluginLog.Debug(() => $"[LightsDynamicFolder] Folder.Name = {this.Name}, CommandName = {this.CommandName}, AdjustmentName = {this.AdjustmentName}");

            try
            {
                // Initialize dependencies now that Plugin is available
                PluginLog.Debug(() => $"[LightsDynamicFolder] Load() - this.Plugin is null: {this.Plugin == null}");

                if (this.Plugin is HomeAssistantPlugin haPlugin)
                {
                    PluginLog.Debug(() => $"[LightsDynamicFolder] Load() - this.Plugin type: {this.Plugin.GetType().Name}");

                    // Initialize dependency injection - use the shared HaClient from Plugin
                    PluginLog.Info("[LightsDynamicFolder] Load() - Initializing dependencies");

                    this._ha = new HaClientAdapter(haPlugin.HaClient);
                    this._dataService = new HomeAssistantDataService(this._ha);
                    this._dataParser = new HomeAssistantDataParser(this._capSvc);

                    // Use the singleton LightStateManager from the main plugin to preserve state across folder exits/entries
                    this._lightStateManager = haPlugin.LightStateManager;
                    var existingCount = this._lightStateManager.GetTrackedEntityIds().Count();
                    PluginLog.Info(() => $"[LightsDynamicFolder] Using singleton LightStateManager with {existingCount} existing tracked entities");

                    this._registryService = new RegistryService();

                    // Initialize light control service with debounce settings
                    const Int32 BrightnessDebounceMs = SendDebounceMs;
                    const Int32 HueSatDebounceMs = SendDebounceMs;
                    const Int32 TempDebounceMs = SendDebounceMs;

                    this._lightSvc = new LightControlService(
                        this._ha,
                        BrightnessDebounceMs,
                        HueSatDebounceMs,
                        TempDebounceMs
                    );

                    // Initialize adjustment command factory
                    var adjustmentContext = new AdjustmentCommandContext(
                        this._lightStateManager,
                        this._lightSvc,
                        this._lookModeByEntity,
                        this.MarkCommandSent,
                        this.AdjustmentValueChanged,
                        this.GetCaps
                    );
                    this._adjustmentCommandFactory = new AdjustmentCommandFactory(adjustmentContext);

                    PluginLog.Info("[LightsDynamicFolder] Load() - All dependencies initialized successfully");
                }
                else
                {
                    PluginLog.Error(() => $"[LightsDynamicFolder] Load() - Plugin is not HomeAssistantPlugin, actual type: {this.Plugin?.GetType()?.Name ?? "null"}");
                    return false;
                }

                HealthBus.HealthChanged += this.OnHealthChanged;

                PluginLog.Info("[LightsDynamicFolder] Load() completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[LightsDynamicFolder] Load() failed with exception");
                return false;
            }
        }

        public override Boolean Unload()
        {
            PluginLog.Info("DynamicFolder.Unload()");

            if (this._lightStateManager != null)
            {
                var trackedCount = this._lightStateManager.GetTrackedEntityIds().Count();
                PluginLog.Info(() => $"[LightsDynamicFolder] Unloading - LightStateManager retains {trackedCount} tracked entities (singleton preserved)");
            }

            // New debounced sender
            this._lightSvc?.Dispose();
            this._eventsCts?.Cancel();
            this._events.SafeCloseAsync();

            HealthBus.HealthChanged -= this.OnHealthChanged;
            return true;
        }

        public override Boolean Activate()
        {
            PluginLog.Info("DynamicFolder.Activate() -> authenticate");
            var ret = this.AuthenticateSync();
            this.EncoderActionNamesChanged();
            return ret; // now returns bool (see below)
        }

        public override Boolean Deactivate()
        {
            PluginLog.Info("DynamicFolder.Deactivate() -> close WS");

            if (this._lightStateManager != null)
            {
                var trackedCount = this._lightStateManager.GetTrackedEntityIds().Count();
                PluginLog.Info(() => $"[LightsDynamicFolder] Deactivating - LightStateManager retains {trackedCount} tracked entities (singleton preserved)");
            }

            this._cts?.Cancel();
            this._ha?.SafeCloseAsync().GetAwaiter().GetResult();
            this._eventsCts?.Cancel();
            _ = this._events.SafeCloseAsync();
            //this.Plugin.OnPluginStatusChanged(PluginStatus.Warning, "Folder closed.", null);
            return true;
        }

        private void OnHealthChanged(Object? sender, EventArgs e)
        {
            try
            {
                this.ButtonActionNamesChanged();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to refresh status tile");
            }
        }


        // now returns bool so Activate() can bubble success up
        private Boolean AuthenticateSync()
        {
            this._cts?.Cancel();
            this._cts = new CancellationTokenSource();

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var baseUrl) ||
                String.IsNullOrWhiteSpace(baseUrl))
            {
                PluginLog.Warning("Missing ha.baseUrl setting");
                HealthBus.Error("Missing Base URL");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Set Home Assistant Base URL in plugin settings.");
                return false;
            }

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var token) ||
                String.IsNullOrWhiteSpace(token))
            {
                PluginLog.Warning("Missing ha.token setting");
                HealthBus.Error("Missing Token");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Set Home Assistant Long-Lived Token in plugin settings.");
                return false;
            }

            try
            {
                var (ok, msg) = this._ha != null
                    ? this._ha.ConnectAndAuthenticateAsync(baseUrl, token, TimeSpan.FromSeconds(AuthTimeoutSeconds), this._cts.Token)
                        .GetAwaiter().GetResult()
                    : (false, "HaClient not initialized");

                if (ok)
                {
                    HealthBus.Ok("Auth OK");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Normal, "Connected to Home Assistant.");



                    try
                    {
                        this._eventsCts?.Cancel();
                        this._eventsCts = new CancellationTokenSource();
                        this._events.BrightnessChanged -= this.OnHaBrightnessChanged; // avoid dup
                        this._events.BrightnessChanged += this.OnHaBrightnessChanged;

                        this._events.ColorTempChanged -= this.OnHaColorTempChanged;
                        this._events.ColorTempChanged += this.OnHaColorTempChanged;

                        this._events.HsColorChanged -= this.OnHaHsColorChanged;
                        this._events.HsColorChanged += this.OnHaHsColorChanged;

                        this._events.RgbColorChanged -= this.OnHaRgbColorChanged;
                        this._events.RgbColorChanged += this.OnHaRgbColorChanged;

                        this._events.XyColorChanged -= this.OnHaXyColorChanged;
                        this._events.XyColorChanged += this.OnHaXyColorChanged;
                        PluginLog.Verbose("[WS] connecting event stream…");


                        _ = this._events.ConnectAndSubscribeAsync(baseUrl, token, this._eventsCts.Token); // fire-and-forget
                        PluginLog.Info("[events] subscribed to state_changed");
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning(ex, "[events] subscribe failed");
                    }


                    // NEW: fetch and log the current lights + light services
                    var okFetch = this.FetchLightsAndServices();
                    if (!okFetch)
                    {
                        PluginLog.Warning("FetchLightsAndServices encountered issues (see logs).");
                    }

                    this._ha?.EnsureConnectedAsync(TimeSpan.FromSeconds(AuthTimeoutSeconds), this._cts.Token).GetAwaiter().GetResult();

                    this._level = ViewLevel.Root;
                    this._currentAreaId = null;
                    this._currentEntityId = null;
                    this._inDeviceView = false;


                    this.ButtonActionNamesChanged();
                    this.EncoderActionNamesChanged();
                    return true;

                }

                HealthBus.Error(msg ?? "Auth failed");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, msg);
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "AuthenticateSync failed");
                HealthBus.Error("Auth error");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Auth error. See plugin logs.");
                return false;
            }
        }
        private Boolean FetchLightsAndServices()
        {
            try
            {
                if (this._dataService == null || this._dataParser == null || this._registryService == null || this._lightStateManager == null)
                {
                    PluginLog.Error("[LightsDynamicFolder] FetchLightsAndServices: Required services are not initialized");
                    return false;
                }

                // Use the new self-contained InitOrUpdateAsync method for light state initialization
                var (lightSuccess, lightError) = this._lightStateManager.InitOrUpdateAsync(this._dataService, this._dataParser, this._cts.Token).GetAwaiter().GetResult();
                if (!lightSuccess)
                {
                    PluginLog.Error($"[LightsDynamicFolder] Light state initialization failed: {lightError}");
                    HealthBus.Error("Light init failed");
                    return false;
                }

                // Fetch services data (unique to DynamicFolder - not handled by InitOrUpdateAsync)
                var (okServices, servicesJson, errServices) = this._dataService.FetchServicesAsync(this._cts.Token).GetAwaiter().GetResult();
                if (!okServices)
                {
                    PluginLog.Error(() => $"Failed to fetch services: {errServices}");
                    return false;
                }

                // Validate servicesJson is not null even if fetch succeeded
                if (String.IsNullOrEmpty(servicesJson))
                {
                    PluginLog.Error("FetchServicesAsync succeeded but returned null or empty JSON data");
                    HealthBus.Error("Invalid services data");
                    return false;
                }

                // We need to re-fetch some data that InitOrUpdateAsync already fetched, but we need it for additional processing
                var (okStates, statesJson, errStates) = this._dataService.FetchStatesAsync(this._cts.Token).GetAwaiter().GetResult();
                var (okEnt, entJson, errEnt) = this._dataService.FetchEntityRegistryAsync(this._cts.Token).GetAwaiter().GetResult();
                var (okDev, devJson, errDev) = this._dataService.FetchDeviceRegistryAsync(this._cts.Token).GetAwaiter().GetResult();
                var (okArea, areaJson, errArea) = this._dataService.FetchAreaRegistryAsync(this._cts.Token).GetAwaiter().GetResult();

                if (!okStates || String.IsNullOrEmpty(statesJson))
                {
                    PluginLog.Error($"[LightsDynamicFolder] Failed to re-fetch states for additional processing: {errStates}");
                    // Don't fail here since light initialization succeeded - just log warning
                    PluginLog.Warning("[LightsDynamicFolder] Continuing without additional state processing");
                }
                else
                {
                    // Validate required JSON data using the parser (unique to DynamicFolder)
                    if (!this._dataParser.ValidateJsonData(statesJson, servicesJson))
                    {
                        PluginLog.Warning("[LightsDynamicFolder] JSON data validation failed - continuing without validation");
                    }

                    // Parse registry data and update registry service (unique to DynamicFolder)
                    var registryData = this._dataParser.ParseRegistries(devJson, entJson, areaJson);
                    this._registryService.UpdateRegistries(registryData);

                    // Parse light states for internal cache updates (unique to DynamicFolder)
                    var lights = this._dataParser.ParseLightStates(statesJson, registryData);
                    
                    // Update internal caches for compatibility with existing code (unique to DynamicFolder)
                    // TODO: Eventually remove these and use services directly
                    this.UpdateInternalCachesFromServices(lights, registryData);
                }

                // Process services using the parser (unique to DynamicFolder)
                this._dataParser.ProcessServices(servicesJson);

                HealthBus.Ok("Fetched lights/services");
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "FetchLightsAndServices failed");
                HealthBus.Error("Fetch failed");
                return false;
            }
        }

        /// <summary>
        /// Updates internal caches from services - only maintains non-state data for UI purposes
        /// State management is now handled entirely by LightStateManager
        /// </summary>
        private void UpdateInternalCachesFromServices(List<LightData> lights, ParsedRegistryData registryData)
        {
            // Clear existing UI data (state is managed by LightStateManager)
            this._lightsByEntity.Clear();
            this._entityToAreaId.Clear();
            this._areaIdToName.Clear();

            PluginLog.Debug(() => $"[UpdateInternalCachesFromServices] Clearing UI caches, LightStateManager handles all state data");

            // Update from registry data
            foreach (var (areaId, areaName) in registryData.AreaIdToName)
            {
                this._areaIdToName[areaId] = areaName;
            }

            // Update from light data - only UI-related data, not state
            foreach (var light in lights)
            {
                var li = new LightItem(light.EntityId, light.FriendlyName, light.State,
                                       light.DeviceId ?? "", light.DeviceName, light.Manufacturer, light.Model);
                this._lightsByEntity[light.EntityId] = li;
                this._entityToAreaId[light.EntityId] = light.AreaId;
            }

            PluginLog.Info(() => $"[UpdateInternalCachesFromServices] Updated {lights.Count} lights, {registryData.AreaIdToName.Count} areas - state managed by LightStateManager");
        }



        private void OnHaBrightnessChanged(String entityId, Int32? bri)
        {
            // Only lights
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var priorIsOn = this._lightStateManager?.IsLightOn(entityId) ?? false;
            var priorCachedB = this._lightStateManager?.GetHsbValues(entityId).B ?? BrightnessOff;
            var wasIgnored = this.ShouldIgnoreFrame(entityId, "brightness");

            PluginLog.Verbose(() => $"[OnHaBrightnessChanged] {entityId}: receivedBri={bri}, priorIsOn={priorIsOn}, priorCachedB={priorCachedB}, wasIgnored={wasIgnored}");

            if (wasIgnored)
            {
                return;
            }

            // Update ON/OFF state from brightness signal using LightStateManager:
            if (bri.HasValue)
            {
                if (bri.Value <= BrightnessOff)
                {
                    // OFF → don't change cached B, just mark state off
                    this._lightStateManager?.UpdateLightState(entityId, false);
                    PluginLog.Verbose(() => $"[OnHaBrightnessChanged] {entityId}: Updated to OFF state (bri={bri.Value})");
                }
                else
                {
                    // ON → update cached B and mark on
                    var clampedBri = HSBHelper.Clamp(bri.Value, BrightnessOff, MaxBrightness);
                    this._lightStateManager?.UpdateLightState(entityId, true, clampedBri);
                    PluginLog.Verbose(() => $"[OnHaBrightnessChanged] {entityId}: Updated to ON state (bri={clampedBri})");
                }
            }

            // Repaint: show 0 if OFF, else cached B
            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                this.AdjustmentValueChanged(AdjBri);
            }

            // Also repaint the device tile icon if visible in the current view
            this.CommandImageChanged(this.CreateCommandName($"{PfxDevice}{entityId}"));
        }


        private void OnHaColorTempChanged(String entityId, Int32? mired, Int32? kelvin, Int32? minM, Int32? maxM)
        {
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (this.ShouldIgnoreFrame(entityId, "color_temp"))
            {
                return;
            }

            // Update via LightStateManager
            this._lightStateManager?.UpdateColorTemp(entityId, mired, kelvin, minM, maxM);

            // If current device, refresh dial value/image
            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                this.AdjustmentValueChanged(AdjTemp);
            }
        }

        private void OnHaHsColorChanged(String entityId, Double? h, Double? s)
        {
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (this.ShouldIgnoreFrame(entityId, "hs_color"))
            {
                return;
            }

            PluginLog.Verbose(() => $"[OnHaHsColorChanged] eid={entityId} h={h?.ToString("F1")} s={s?.ToString("F1")}");

            // Update HS via LightStateManager
            this._lightStateManager?.UpdateHsColor(entityId, h, s);

            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                // 🔸 brightness-style: refresh all related wheels
                this.AdjustmentValueChanged(AdjHue);
                this.AdjustmentValueChanged(AdjSat);

            }
        }

        // Small thresholds to avoid UI churn on tiny float changes (already defined above)

        private void OnHaRgbColorChanged(String entityId, Int32? r, Int32? g, Int32? b)
        {
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!r.HasValue || !g.HasValue || !b.HasValue)
            {
                return;
            }

            if (this.ShouldIgnoreFrame(entityId, "rgb_color"))
            {
                return;
            }

            PluginLog.Verbose(() => $"[OnHaRgbColorChanged] eid={entityId} rgb=[{r},{g},{b}]");

            var (h, s) = HSBHelper.RgbToHs(r.Value, g.Value, b.Value);
            h = HSBHelper.Wrap360(h);
            s = HSBHelper.Clamp(s, MinSaturation, MaxSaturation);

            var cur = this._lightStateManager?.GetHsbValues(entityId) ?? (DefaultHue, DefaultSaturation, MidBrightness);
            var changed = Math.Abs(cur.H - h) >= HueEps || Math.Abs(cur.S - s) >= SatEps;

            if (!changed)
            {
                return;
            }

            // Update via LightStateManager
            this._lightStateManager?.UpdateHsColor(entityId, h, s);

            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                this.AdjustmentValueChanged(AdjHue);
                this.AdjustmentValueChanged(AdjSat);
                // brightness unchanged here
            }
        }

        private void OnHaXyColorChanged(String entityId, Double? x, Double? y, Int32? bri)
        {
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Bind to non-nullable locals or bail out
            if (x is not Double xv || y is not Double yv)
            {
                return;
            }

            if (this.ShouldIgnoreFrame(entityId, "xy_color"))
            {
                return;
            }

            PluginLog.Verbose(() => $"[OnHaXyColorChanged] eid={entityId} xy=[{xv.ToString("F4")},{yv.ToString("F4")}] bri={bri}");

            // Pick a luminance for XY->RGB: prefer event bri, else cached, else mid
            var cur = this._lightStateManager?.GetHsbValues(entityId) ?? (DefaultHue, DefaultSaturation, MidBrightness);
            var baseB = cur.B;
            var usedB = HSBHelper.Clamp(bri ?? baseB, BrightnessOff, MaxBrightness);

            var (R, G, B) = ColorConv.XyBriToRgb(xv, yv, usedB);
            var (h, s) = HSBHelper.RgbToHs(R, G, B);
            h = HSBHelper.Wrap360(h);
            s = HSBHelper.Clamp(s, MinSaturation, MaxSaturation);

            var hsChanged = Math.Abs(cur.H - h) >= HueEps || Math.Abs(cur.S - s) >= SatEps;
            var briChanged = bri.HasValue && usedB != cur.B;

            if (!hsChanged && !briChanged)
            {
                return;
            }

            // Update via LightStateManager
            if (hsChanged)
            {
                this._lightStateManager?.UpdateHsColor(entityId, h, s);
            }
            if (briChanged)
            {
                this._lightStateManager?.SetCachedBrightness(entityId, usedB);
            }

            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                if (hsChanged)
                { this.AdjustmentValueChanged(AdjHue); this.AdjustmentValueChanged(AdjSat); }
                if (briChanged)
                {
                    this.AdjustmentValueChanged(AdjBri);
                }
            }
        }



        private void MarkCommandSent(String entityId) => this._lastCmdAt[entityId] = DateTime.UtcNow;

        private Boolean ShouldIgnoreFrame(String entityId, String? reasonForLog = null)
        {
            if (this._lastCmdAt.TryGetValue(entityId, out var t))
            {
                var elapsed = DateTime.UtcNow - t;
                if (elapsed <= EchoSuppressWindow)
                {
                    PluginLog.Verbose(() => $"[echo] SUPPRESSED frame for {entityId} ({reasonForLog}) - elapsed={elapsed.TotalSeconds:F1}s of {EchoSuppressWindow.TotalSeconds}s window");
                    return true;
                }
                // past the window → forget it
                this._lastCmdAt.Remove(entityId);
                PluginLog.Verbose(() => $"[echo] Window expired for {entityId}, removing from suppression list");
            }
            return false;
        }




    }
}