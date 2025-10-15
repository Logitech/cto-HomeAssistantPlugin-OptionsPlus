namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;



    using Loupedeck; // ensure this is present

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




        private enum LookMode { Hs, Temp } // which color mode to show in the adjustment tile
                                           // What the user adjusted last per light (drives preview mode)
        private readonly Dictionary<String, LookMode> _lookModeByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        private const String CmdStatus = "status";
        private const String CmdRetry = "retry";
        private const String CmdBack = "back"; // our own back
        private const String CmdArea = "area";

        private readonly HaWebSocketClient _client = new();

        private CancellationTokenSource _cts = new();

        private readonly Dictionary<String, LightItem> _lightsByEntity = new();

        private readonly Dictionary<String, (Double H, Double S, Int32 B)> _hsbByEntity
            = new Dictionary<String, (Double H, Double S, Int32 B)>(StringComparer.OrdinalIgnoreCase);

        // ON/OFF state cache per entity (true = on, false = off)
        private readonly Dictionary<String, Boolean> _isOnByEntity =
            new(StringComparer.OrdinalIgnoreCase);


        private readonly Dictionary<String, LightCaps> _capsByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly CapabilityService _capSvc = new();


        private LightCaps GetCaps(String eid) =>
            this._capsByEntity.TryGetValue(eid, out var c)
                ? c
                : new LightCaps(true, false, false, false); // safe default: on/off + brightness




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
        private const String UnassignedAreaName = "(No area)";

        private String? _currentEntityId = null;

        // action parameter prefixes
        private const String PfxDevice = "device:"; // device:<entity_id>
        private const String PfxActOn = "act:on:"; // act:on:<entity_id>
        private const String PfxActOff = "act:off:"; // act:off:<entity_id>

        // --- WHEEL: constants & state
        private const String AdjBri = "adj:bri";     // brightness wheel
        private Int32 _wheelCounter = 0;                 // just for display/log when not in device view
        private const Int32 WheelStepPercent = 1;        // 1% per tick

        // ---- COLOR TEMP state (mirrors brightness pattern) ----
        private const String AdjTemp = "adj:ha-temp";   // wheel id
        private const Int32 TempStepMireds = 2;        // step per tick (≈smooth)
        private const Int32 MaxMiredsPerEvent = 60;     // cap coalesced burst
        private const Int32 DefaultMinMireds = 153;     // ~6500K
        private const Int32 DefaultMaxMireds = 500;     // ~2000K
        private const Int32 DefaultWarmMired = 370;     // ~2700K (UI fallback)

        // ===== HUE control (rotation-only) =====
        private const String AdjHue = "adj:ha-hue";   // wheel id

        private const Int32 HueStepDegPerTick = 1;      // 1° per tick feels smooth
        private const Int32 MaxHueDegPerEvent = 30;     // cap coalesced bursts

        // ===== SATURATION control =====
        private const String AdjSat = "adj:ha-sat";

        private const Int32 SatStepPctPerTick = 1;   // feels smooth
        private const Int32 MaxSatPctPerEvent = 15;  // cap burst coalesce



        // Per-entity cache: (Min, Max, Current) in Mireds
        private readonly Dictionary<String, (Int32 Min, Int32 Max, Int32 Cur)> _tempMiredByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        // Tune these if you want
        private const Int32 SendDebounceMs = 10; // how long to wait after the last tick before sending
        private const Int32 ReconcileIdleMs = 500; // idle pause before doing a single get_states as truth
        private const Int32 MaxPctPerEvent = 10;  // cap huge coalesced diffs to keep UI sane

        private readonly HaEventListener _events = new();
        private CancellationTokenSource _eventsCts = new();





        private readonly LightControlService _lightSvc;
        private readonly IHaClient _ha; // adapter over HaWebSocketClient


        // --- Echo suppression: ignore HA frames shortly after we sent a command ---
        private readonly Dictionary<String, DateTime> _lastCmdAt =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan EchoSuppressWindow = TimeSpan.FromSeconds(3);





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
                    var pct = (Int32)Math.Round(effB * 100.0 / 255.0);
                    return $"{pct}%";
                }

                // Root view: tick counter for diagnostics
                return this._wheelCounter.ToString();
            }
            if (actionParameter == AdjSat)
            {
                return this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb)
                    ? $"{(Int32)Math.Round(HSBHelper.Clamp(hsb.S, 0, 100))}%"
                    : "—%";
            }


            if (actionParameter == AdjHue)
            {
                return this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb)
                    ? $"{(Int32)Math.Round(HSBHelper.Wrap360(hsb.H))}°"
                    : "—°";
            }

            // Color Temperature wheel
            if (actionParameter == AdjTemp)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    if (this._tempMiredByEntity.TryGetValue(this._currentEntityId, out var t))
                    {
                        var k = ColorTemp.MiredToKelvin(t.Cur);
                        return $"{k}K";
                    }
                    return "— K"; // no cache yet → neutral placeholder
                }

                // Root view: hint the per-tick step size
                return $"±{TempStepMireds} mired";
            }

            return base.GetAdjustmentValue(actionParameter);
        }


        // --- sRGB <-> linear helpers (IEC 61966-2-1) ---
        private static Double SrgbToLinear01(Double c)
            => (c <= 0.04045) ? (c / 12.92) : Math.Pow((c + 0.055) / 1.055, 2.4);

        private static Double LinearToSrgb01(Double c)
        {
            c = Math.Max(0.0, Math.Min(1.0, c));
            return (c <= 0.0031308) ? (12.92 * c) : (1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055);
        }

        // --- Kelvin (blackbody) -> sRGB, using Tanner Helland / Neil Bartlett coeffs ---
        private static (Int32 R, Int32 G, Int32 B) KelvinToSrgb(Int32 kelvin)
        {
            // Clamp to a sensible household lamp range to avoid cartoonish extremes
            var K = HSBHelper.Clamp(kelvin, 1800, 6500) / 100.0; // Temp in hundreds of K
            Double r, g, b;

            if (K <= 66.0)
            {
                r = 255.0;
                g = 99.4708025861 * Math.Log(K) - 161.1195681661;
                b = (K <= 19.0) ? 0.0 : 138.5177312231 * Math.Log(K - 10.0) - 305.0447927307;
            }
            else
            {
                r = 329.698727446 * Math.Pow(K - 60.0, -0.1332047592);
                g = 288.1221695283 * Math.Pow(K - 60.0, -0.0755148492);
                b = 255.0;
            }

            var R = HSBHelper.Clamp((Int32)Math.Round(r), 0, 255);
            var G = HSBHelper.Clamp((Int32)Math.Round(g), 0, 255);
            var B = HSBHelper.Clamp((Int32)Math.Round(b), 0, 255);
            return (R, G, B);
        }

        // Scale an sRGB color by brightness in *linear* light, then encode back to sRGB
        private static (Int32 R, Int32 G, Int32 B) ApplyBrightnessLinear((Int32 R, Int32 G, Int32 B) srgb, Int32 effB)
        {
            var l = HSBHelper.Clamp(effB, 0, 255) / 255.0;     // 0..1
            if (l <= 0.0)
            {
                return (0, 0, 0);
            }

            var lr = SrgbToLinear01(srgb.R / 255.0) * l;
            var lg = SrgbToLinear01(srgb.G / 255.0) * l;
            var lb = SrgbToLinear01(srgb.B / 255.0) * l;

            var R = HSBHelper.Clamp((Int32)Math.Round(LinearToSrgb01(lr) * 255.0), 0, 255);
            var G = HSBHelper.Clamp((Int32)Math.Round(LinearToSrgb01(lg) * 255.0), 0, 255);
            var B = HSBHelper.Clamp((Int32)Math.Round(LinearToSrgb01(lb) * 255.0), 0, 255);
            return (R, G, B);
        }


        // Simulate how the light *actually* looks, honoring last-look mode (HS vs Temp),
        // using blackbody CCT for Temp and gamma-correct dimming for both modes.
        private (Int32 R, Int32 G, Int32 B) GetSimulatedLightRgbForCurrentDevice()
        {
            if (!this._inDeviceView || String.IsNullOrEmpty(this._currentEntityId))
            {
                return (64, 64, 64);
            }

            var eid = this._currentEntityId;

            // Effective brightness (0 if off)
            var effB = this.GetEffectiveBrightnessForDisplay(eid); // 0..255
            if (effB <= 0)
            {
                return (0, 0, 0);
            }

            var prefer = this._lookModeByEntity.TryGetValue(eid, out var pref) ? pref : LookMode.Hs;

            // --- Preferred: HS look ---
            (Int32 R, Int32 G, Int32 B) RenderFromHs()
            {
                if (!this._hsbByEntity.TryGetValue(eid, out var hsb))
                {
                    return (-1, -1, -1);
                }

                // Get full-brightness sRGB from your HSV/HSB helper, then dim in linear space
                var (sr, sg, sb) = HSBHelper.HsbToRgb(
                    HSBHelper.Wrap360(hsb.H),
                    HSBHelper.Clamp(Math.Max(0, hsb.S), 0, 100),
                    100.0 // full value; we'll apply brightness correctly afterwards
                );

                // Optional: tiny desaturation at very low brightness for human-perception feel
                // (keeps extreme colors from looking too "inky" when almost off)
                // double l = effB / 255.0;
                // if (l < 0.10) { sr = (int)(sr * (0.9 + l)); sg = (int)(sg * (0.9 + l)); sb = (int)(sb * (0.9 + l)); }

                return ApplyBrightnessLinear((sr, sg, sb), effB);
            }

            // --- Preferred: Color Temp look ---
            (Int32 R, Int32 G, Int32 B) RenderFromTemp()
            {
                if (!this.TryGetCachedTempMired(eid, out var t))
                {
                    return (-1, -1, -1);
                }

                var k = ColorTemp.MiredToKelvin(t.Cur);
                var srgb = KelvinToSrgb(k);                // blackbody approximate in sRGB
                return ApplyBrightnessLinear(srgb, effB);  // dim in linear light, back to sRGB
            }

            (Int32 R, Int32 G, Int32 B) rgb;

            rgb = (prefer == LookMode.Hs) ? RenderFromHs() : RenderFromTemp();
            if (rgb.R >= 0)
            {
                return rgb;
            }

            rgb = (prefer == LookMode.Hs) ? RenderFromTemp() : RenderFromHs();
            if (rgb.R >= 0)
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
                var bri = 128;
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    bri = this.GetEffectiveBrightnessForDisplay(this._currentEntityId);
                }

                var pct = (Int32)Math.Round(bri * 100.0 / 255.0);

                Int32 r, g, b;
                if (bri <= 0)
                { r = g = b = 0; }
                else
                { r = Math.Min(30 + pct * 2, 255); g = Math.Min(30 + pct, 220); b = 30; }

                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Brightness), "☀", padPct: 10, font: 58);
            }

            if (actionParameter == AdjSat)
            {
                var (r, g, b) = this.GetSimulatedLightRgbForCurrentDevice();
                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Saturation), "S", padPct: 8, font: 56);
            }

            if (actionParameter == AdjTemp)
            {
                var (r, g, b) = this.GetSimulatedLightRgbForCurrentDevice();
                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Temperature), "⟷", padPct: 10, font: 58);
            }

            if (actionParameter == AdjHue)
            {
                var (r, g, b) = this.GetSimulatedLightRgbForCurrentDevice();
                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Hue), "H", padPct: 8, font: 56);
            }

            return base.GetAdjustmentImage(actionParameter, imageSize);
        }





        // --- WHEEL: rotation handler (like your CounterAdjustment.ApplyAdjustment)
        public override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (actionParameter == AdjBri && diff != 0)
            {


                try
                {
                    if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                    {
                        var entityId = this._currentEntityId;

                        // current brightness from cache (fallback to mid)
                        var curB = this._hsbByEntity.TryGetValue(entityId, out var hsb)
                            ? hsb.B
                            : 128;

                        // compute target absolutely (± WheelStepPercent per tick), with cap
                        var stepPct = diff * WheelStepPercent;
                        stepPct = Math.Sign(stepPct) * Math.Min(Math.Abs(stepPct), MaxPctPerEvent);
                        var deltaB = (Int32)Math.Round(255.0 * stepPct / 100.0);
                        var targetB = HSBHelper.Clamp(curB + deltaB, 0, 255);

                        // optimistic UI: update cache immediately → live value/image
                        this.SetCachedBrightness(entityId, targetB);
                        this.AdjustmentValueChanged(actionParameter);
                        this.AdjustmentValueChanged(AdjSat);
                        this.AdjustmentValueChanged(AdjHue);
                        this.AdjustmentValueChanged(AdjTemp); // temp tile also reflects effB


                        this.MarkCommandSent(entityId);

                        this._lightSvc.SetBrightness(entityId, targetB);

                    }
                    else
                    {
                        // root view: your counter behavior (if you keep it)
                        this._wheelCounter += diff;
                        this.AdjustmentValueChanged(actionParameter);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[wheel] ApplyAdjustment exception");
                    HealthBus.Error("Wheel error");
                    this.AdjustmentValueChanged(actionParameter);
                }
            }

            if (actionParameter == AdjSat && diff != 0)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    if (!this.GetCaps(this._currentEntityId).ColorHs)
                    {
                        return;
                    }

                    var eid = this._currentEntityId;

                    this._lookModeByEntity[this._currentEntityId] = LookMode.Hs;

                    // Current HS from cache (fallbacks)
                    if (!this._hsbByEntity.TryGetValue(eid, out var hsb))
                    {
                        hsb = (0, 100, 128);
                    }

                    // Compute step with cap and clamp 0..100
                    var step = diff * SatStepPctPerTick;
                    step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxSatPctPerEvent);
                    var newS = HSBHelper.Clamp(hsb.S + step, 0, 100);

                    // Optimistic UI
                    this._hsbByEntity[eid] = (hsb.H, newS, hsb.B);
                    this.AdjustmentValueChanged(AdjSat);
                    this.AdjustmentValueChanged(AdjHue);
                    this.AdjustmentValueChanged(AdjTemp);


                    var curH = this._hsbByEntity.TryGetValue(eid, out var hsb3) ? hsb3.H : 0;
                    this.MarkCommandSent(eid);
                    this._lightSvc.SetHueSat(eid, curH, newS);


                }
            }
            if (actionParameter == AdjHue && diff != 0)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    if (!this.GetCaps(this._currentEntityId).ColorHs)
                    {
                        return;
                    }

                    var eid = this._currentEntityId;
                    this._lookModeByEntity[this._currentEntityId] = LookMode.Hs;

                    // Current HS from cache (fallbacks)
                    if (!this._hsbByEntity.TryGetValue(eid, out var hsb))
                    {
                        hsb = (0, 100, 128); // default to vivid color, mid brightness
                    }

                    // Compute step with cap; wrap 0..360
                    var step = diff * HueStepDegPerTick;
                    step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxHueDegPerEvent);
                    var newH = HSBHelper.Wrap360(hsb.H + step);

                    // Optimistic UI
                    this._hsbByEntity[eid] = (newH, hsb.S, hsb.B);
                    this.AdjustmentValueChanged(AdjHue);
                    this.AdjustmentValueChanged(AdjSat);
                    this.AdjustmentValueChanged(AdjTemp); // temp tile also reflects effB

                    var curS = this._hsbByEntity.TryGetValue(eid, out var hsb2) ? hsb2.S : 100;
                    this.MarkCommandSent(eid);
                    this._lightSvc.SetHueSat(eid, newH, curS);

                }

            }


            if (actionParameter == AdjTemp && diff != 0)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    if (!this.GetCaps(this._currentEntityId).ColorTemp)
                    {
                        return;
                    }

                    var eid = this._currentEntityId;
                    this._lookModeByEntity[this._currentEntityId] = LookMode.Temp;

                    var (minM, maxM, curM) = this._tempMiredByEntity.TryGetValue(eid, out var t)
                        ? t
                        : (DefaultMinMireds, DefaultMaxMireds, DefaultWarmMired);

                    var step = diff * TempStepMireds;
                    step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxMiredsPerEvent);

                    var targetM = HSBHelper.Clamp(curM + step, minM, maxM);

                    // Optimistic UI
                    this.SetCachedTempMired(eid, null, null, targetM);
                    this.AdjustmentValueChanged(AdjTemp);
                    this.AdjustmentValueChanged(AdjHue);
                    this.AdjustmentValueChanged(AdjSat);
                    this.MarkCommandSent(eid);
                    this._lightSvc.SetTempMired(eid, targetM);
                }
            }



            return;
        }









        public HomeAssistantLightsDynamicFolder()
        {
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

            // Wrap the raw client so the service can be unit-tested / mocked later
            this._ha = new HaClientAdapter(this._client);

            // If you want separate debounce timings per channel, split these constants.
            const Int32 BrightnessDebounceMs = SendDebounceMs;
            const Int32 HueSatDebounceMs = SendDebounceMs;
            const Int32 TempDebounceMs = SendDebounceMs;

            this._lightSvc = new LightControlService(
                this._ha,
                BrightnessDebounceMs,
                HueSatDebounceMs,
                TempDebounceMs
            );



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
            // If we know it’s OFF, show 0; otherwise show cached B
            return this._isOnByEntity.TryGetValue(entityId, out var on) && !on
                ? 0
                : this._hsbByEntity.TryGetValue(entityId, out var hsb) ? hsb.B : 0;
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
                    TilePainter.Background(bb, ok ? okImg : issueImg, ok ? new BitmapColor(0, 160, 60) : new BitmapColor(200, 30, 30));
                    bb.DrawText(ok ? "ONLINE" : "ISSUE", fontSize: 22, color: new BitmapColor(255, 255, 255));
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

            PluginLog.Info($"RunCommand: {actionParameter}");


            if (actionParameter == CmdBack)
            {
                if (this._level == ViewLevel.Device)
                {
                    if (!String.IsNullOrEmpty(this._currentEntityId))
                    {
                        this._lightSvc.CancelPending(this._currentEntityId);
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
                return;
            }

            if (actionParameter == CmdRetry)
            {
                this.AuthenticateSync();
                return;
            }

            if (actionParameter == CmdStatus)
            {
                var ok = HealthBus.State == HealthState.Ok;
                this.Plugin.OnPluginStatusChanged(ok ? PluginStatus.Normal : PluginStatus.Error,
                    ok ? "Home Assistant is connected." : HealthBus.LastMessage);
                return;
            }

            if (actionParameter.StartsWith(CmdArea, StringComparison.OrdinalIgnoreCase))
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
                    PluginLog.Info($"ENTER area view: {areaId}");
                }
                return;
            }


            // Enter device view
            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                PluginLog.Info($"Entering Device view");
                var entityId = actionParameter.Substring(PfxDevice.Length);
                if (this._lightsByEntity.ContainsKey(entityId))
                {


                    this._inDeviceView = true;
                    this._level = ViewLevel.Device;
                    this._currentEntityId = entityId;
                    this._wheelCounter = 0; // avoids showing previous ticks anywhere

                    // Brightness cache always OK
                    if (!this._hsbByEntity.ContainsKey(entityId))
                    {
                        this._hsbByEntity[entityId] = (0, 0, 0);
                    }

                    // Only keep/seed temp cache if device supports it
                    var caps = this.GetCaps(entityId);
                    if (!caps.ColorTemp)
                    {
                        this._tempMiredByEntity.Remove(entityId);
                    }
                    else if (!this._tempMiredByEntity.ContainsKey(entityId))
                    {
                        this._tempMiredByEntity[entityId] = (DefaultMinMireds, DefaultMaxMireds, DefaultWarmMired);
                    }


                    this.ButtonActionNamesChanged();       // swap to device actions

                    // 🔸 brightness-style UI refresh: force all wheels to redraw immediately
                    this.AdjustmentValueChanged(AdjBri);
                    this.AdjustmentValueChanged(AdjTemp);
                    this.AdjustmentValueChanged(AdjHue);
                    this.AdjustmentValueChanged(AdjSat);

                    PluginLog.Info($"ENTER device view: {entityId}  level={this._level} inDevice={this._inDeviceView}");

                }
                return;
            }


            // Actions
            if (actionParameter.StartsWith(PfxActOn, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActOn.Length);

                // Optimistic: mark ON immediately (UI becomes responsive)
                this._isOnByEntity[entityId] = true;
                this.CommandImageChanged(this.CreateCommandName($"{PfxDevice}{entityId}"));
                if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    this.AdjustmentValueChanged(AdjBri);
                    this.AdjustmentValueChanged(AdjSat);
                    this.AdjustmentValueChanged(AdjHue);
                    this.AdjustmentValueChanged(AdjTemp);
                }

                JsonElement? data = null;
                var caps = this.GetCaps(entityId);
                if (caps.Brightness && this._hsbByEntity.TryGetValue(entityId, out var hsb))
                {
                    var bri = HSBHelper.Clamp(Math.Max(1, hsb.B), 1, 255);
                    data = JsonSerializer.SerializeToElement(new { brightness = bri });
                }
                this.MarkCommandSent(entityId);
                _ = this._lightSvc.TurnOnAsync(entityId, data);
                return;
            }
            if (actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActOff.Length);

                // Optimistic: mark OFF (don’t touch cached B)
                this._isOnByEntity[entityId] = false;
                this.CommandImageChanged(this.CreateCommandName($"{PfxDevice}{entityId}"));
                if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    this.AdjustmentValueChanged(AdjBri);
                    this.AdjustmentValueChanged(AdjSat);
                    this.AdjustmentValueChanged(AdjHue);
                    this.AdjustmentValueChanged(AdjTemp);
                }

                this._lightSvc.TurnOffAsync(entityId);
                this.MarkCommandSent(entityId);
                return;
            }

        }










        // 🔧 return bools here:
        public override Boolean Load()
        {
            PluginLog.Info("DynamicFolder.Load()");
            PluginLog.Info($"Folder.Name = {this.Name}, CommandName = {CommandName}, AdjustmentName = {AdjustmentName}");

            HealthBus.HealthChanged += this.OnHealthChanged;



            return true;
        }

        public override Boolean Unload()
        {
            PluginLog.Info("DynamicFolder.Unload()");



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
            this._cts?.Cancel();
            this._client.SafeCloseAsync().GetAwaiter().GetResult();
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
                var (ok, msg) = this._client
                    .ConnectAndAuthenticateAsync(baseUrl, token, TimeSpan.FromSeconds(60), this._cts.Token)
                    .GetAwaiter().GetResult();

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

                    this._client.SendPingAsync(this._cts.Token).GetAwaiter().GetResult();

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
                // 1) get_states
                var (okStates, statesJson, errStates) = this._client.RequestAsync("get_states", this._cts.Token)
                                                               .GetAwaiter().GetResult();
                if (!okStates)
                {
                    PluginLog.Warning($"get_states failed: {errStates}");
                    HealthBus.Error("get_states failed");
                    return false;
                }

                // 2) get_services
                var (okServices, servicesJson, errServices) = this._client.RequestAsync("get_services", this._cts.Token)
                                                                     .GetAwaiter().GetResult();
                if (!okServices)
                {
                    PluginLog.Warning($"get_services failed: {errServices}");
                    HealthBus.Error("get_services failed");
                    return false;
                }

                // 3) entity registry
                var (okEnt, entJson, errEnt) = this._client.RequestAsync("config/entity_registry/list", this._cts.Token)
                                                      .GetAwaiter().GetResult();
                if (!okEnt)
                {
                    PluginLog.Warning($"entity_registry/list failed: {errEnt}");
                    // Not fatal for basic operation, but helpful for device names
                }

                // 4) device registry
                var (okDev, devJson, errDev) = this._client.RequestAsync("config/device_registry/list", this._cts.Token)
                                                      .GetAwaiter().GetResult();
                if (!okDev)
                {
                    PluginLog.Warning($"device_registry/list failed: {errDev}");
                }

                var (okArea, areaJson, errArea) = this._client
            .RequestAsync("config/area_registry/list", this._cts.Token)
            .GetAwaiter().GetResult();
                if (!okArea)
                {
                    PluginLog.Warning($"area_registry/list failed: {errArea}");
                }

                // ---- Parse results ----
                // states: array of { entity_id, state, attributes{friendly_name,...}, ...}
                using var statesDoc = JsonDocument.Parse(statesJson);
                // services: object { domain: { service: { fields, target, response } } }
                using var servicesDoc = JsonDocument.Parse(servicesJson);


                JsonElement entArray = default, devArray = default, areaArray = default;
                if (okEnt)
                {
                    entArray = JsonDocument.Parse(entJson).RootElement;
                }

                if (okDev)
                {
                    devArray = JsonDocument.Parse(devJson).RootElement;
                }
                if (okArea)
                {
                    areaArray = JsonDocument.Parse(areaJson).RootElement;
                }

                // Build device lookup by device_id AND device->area_id  <-- UPDATED
                var deviceById = new Dictionary<String, (String name, String mf, String model)>(StringComparer.OrdinalIgnoreCase);
                var deviceAreaById = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

                if (okDev && devArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dev in devArray.EnumerateArray())
                    {
                        var id = dev.GetPropertyOrDefault("id");
                        var name = dev.GetPropertyOrDefault("name_by_user") ?? dev.GetPropertyOrDefault("name") ?? "";
                        var mf = dev.GetPropertyOrDefault("manufacturer") ?? "";
                        var model = dev.GetPropertyOrDefault("model") ?? "";
                        var area = dev.GetPropertyOrDefault("area_id"); // may be null

                        if (!String.IsNullOrEmpty(id))
                        {
                            deviceById[id] = (name, mf, model);
                            if (!String.IsNullOrEmpty(area))
                            {
                                deviceAreaById[id] = area;
                            }
                        }
                    }
                }

                // Build entity->device_id AND entity->area_id (direct from entity registry)  <-- UPDATED
                var entityDevice = new Dictionary<String, (String deviceId, String originalName)>(StringComparer.OrdinalIgnoreCase);
                var entityArea = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

                if (okEnt && entArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ent in entArray.EnumerateArray())
                    {
                        var entityId = ent.GetPropertyOrDefault("entity_id");
                        if (String.IsNullOrEmpty(entityId))
                        {
                            continue;
                        }

                        var deviceId = ent.GetPropertyOrDefault("device_id") ?? "";
                        var oname = ent.GetPropertyOrDefault("original_name") ?? "";
                        var areaId = ent.GetPropertyOrDefault("area_id"); // may be null

                        entityDevice[entityId] = (deviceId, oname);
                        if (!String.IsNullOrEmpty(areaId))
                        {
                            entityArea[entityId] = areaId;
                        }
                    }
                }

                // Areas: area_id -> name  <-- NEW
                this._areaIdToName.Clear();
                if (okArea && areaArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ar in areaArray.EnumerateArray())
                    {
                        // HA uses "area_id" as the stable id; older payloads may also include "id"
                        var id = ar.GetPropertyOrDefault("area_id") ?? ar.GetPropertyOrDefault("id");
                        var name = ar.GetPropertyOrDefault("name") ?? id ?? "";
                        if (!String.IsNullOrEmpty(id))
                        {
                            this._areaIdToName[id] = name;
                        }
                    }
                }

                // Filter lights from states and assemble LightItem
                this._lightsByEntity.Clear();
                this._hsbByEntity.Clear();
                this._entityToAreaId.Clear();

                foreach (var st in statesDoc.RootElement.EnumerateArray())
                {
                    var entityId = st.GetPropertyOrDefault("entity_id");
                    if (String.IsNullOrEmpty(entityId) || !entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var state = st.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var attrs = st.TryGetProperty("attributes", out var a) ? a : default;
                    var isOn = String.Equals(state, "on", StringComparison.OrdinalIgnoreCase);
                    this._isOnByEntity[entityId] = isOn;

                    var friendly = (attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("friendly_name", out var fn))
                                   ? fn.GetString() ?? entityId
                                   : entityId;

                    String? deviceId = null, deviceName = "", mf = "", model = "";
                    // --- Capabilities (centralized) ---
                    var caps = this._capSvc.ForLight(attrs);
                    this._capsByEntity[entityId] = caps;

                    PluginLog.Info($"[Caps] {entityId} caps: onoff={caps.OnOff} bri={caps.Brightness} ctemp={caps.ColorTemp} color={caps.ColorHs}");



                    if (entityDevice.TryGetValue(entityId, out var map) && !String.IsNullOrEmpty(map.deviceId))
                    {
                        deviceId = map.deviceId;
                        if (deviceById.TryGetValue(deviceId, out var d))
                        {
                            deviceName = d.name;
                            mf = d.mf;
                            model = d.model;
                        }
                    }

                    // --- Area resolution (entity area wins; else device area; else Unassigned)  <-- NEW
                    String? areaId = null;
                    if (entityArea.TryGetValue(entityId, out var ea))
                    {
                        areaId = ea;
                    }
                    else if (!String.IsNullOrEmpty(deviceId) && deviceAreaById.TryGetValue(deviceId, out var da))
                    {
                        areaId = da;
                    }

                    if (String.IsNullOrEmpty(areaId))
                    {
                        areaId = UnassignedAreaId;
                    }

                    this._entityToAreaId[entityId] = areaId;

                    // --- Brightness: seed without clobbering last non-zero on OFF ---
                    var bri = 0;

                    JsonElement brEl = default;   // <-- initialize
                    var hasAttrBri = false;

                    if (attrs.ValueKind == JsonValueKind.Object &&
                        attrs.TryGetProperty("brightness", out brEl) &&
                        brEl.ValueKind == JsonValueKind.Number)
                    {
                        hasAttrBri = true;
                    }

                    if (hasAttrBri)
                    {
                        bri = HSBHelper.Clamp(brEl.GetInt32(), 0, 255);
                    }
                    else if (!isOn) // OFF and no brightness attribute: keep last-known if any
                    {
                        bri = this._hsbByEntity.TryGetValue(entityId, out var oldBri) ? oldBri.B : 0;
                    }
                    else
                    {
                        // ON but no brightness attribute → reasonable fallback
                        bri = 128;
                    }


                    // Optional HS seed (doesn’t matter for brightness UI, but nice to keep)
                    Double h = 0, sat = 0;
                    Int32 minM = DefaultMinMireds, maxM = DefaultMaxMireds, curM = DefaultWarmMired;
                    if (attrs.ValueKind == JsonValueKind.Object)
                    {

                        if (attrs.TryGetProperty("min_mireds", out var v1) && v1.ValueKind == JsonValueKind.Number)
                        {
                            minM = v1.GetInt32();
                        }

                        if (attrs.TryGetProperty("max_mireds", out var v2) && v2.ValueKind == JsonValueKind.Number)
                        {
                            maxM = v2.GetInt32();
                        }

                        if (attrs.TryGetProperty("color_temp", out var v3) && v3.ValueKind == JsonValueKind.Number)
                        {
                            curM = HSBHelper.Clamp(v3.GetInt32(), minM, maxM);
                        }
                        else if (attrs.TryGetProperty("color_temp_kelvin", out var v4) && v4.ValueKind == JsonValueKind.Number)
                        {
                            curM = HSBHelper.Clamp(ColorTemp.KelvinToMired(v4.GetInt32()), minM, maxM);
                        }
                        else if (String.Equals(state, "off", StringComparison.OrdinalIgnoreCase))
                        {
                            curM = DefaultWarmMired;
                        }

                        if (attrs.TryGetProperty("hs_color", out var hs) &&
                            hs.ValueKind == JsonValueKind.Array && hs.GetArrayLength() >= 2 &&
                            hs[0].ValueKind == JsonValueKind.Number && hs[1].ValueKind == JsonValueKind.Number)
                        {
                            h = HSBHelper.Wrap360(hs[0].GetDouble());
                            sat = HSBHelper.Clamp(hs[1].GetDouble(), 0, 100);
                        }
                        else if (attrs.TryGetProperty("rgb_color", out var rgb) &&
                                 rgb.ValueKind == JsonValueKind.Array && rgb.GetArrayLength() >= 3 &&
                                 rgb[0].ValueKind == JsonValueKind.Number &&
                                 rgb[1].ValueKind == JsonValueKind.Number &&
                                 rgb[2].ValueKind == JsonValueKind.Number)
                        {
                            var (hh, ss) = HSBHelper.RgbToHs(rgb[0].GetInt32(), rgb[1].GetInt32(), rgb[2].GetInt32());
                            h = HSBHelper.Wrap360(hh);
                            sat = HSBHelper.Clamp(ss, 0, 100);
                        }
                    }
                    this._hsbByEntity[entityId] = (h, sat, bri); // 👈 ALWAYS set B now
                                                                 //_tempMiredByEntity[entityId] = (minM, maxM, curM);
                                                                 // Only keep a temp cache if the light supports color temperature
                    if (caps.ColorTemp)
                    {
                        this._tempMiredByEntity[entityId] = (minM, maxM, curM);
                    }
                    else
                    {
                        this._tempMiredByEntity.Remove(entityId);
                    }

                    var li = new LightItem(entityId, friendly, state, deviceId ?? "", deviceName, mf, model);
                    this._lightsByEntity[entityId] = li;

                    PluginLog.Info($"[Light] {entityId} | name='{friendly}' | state={state} | dev='{deviceName}' mf='{mf}' model='{model}' bri={bri} tempMired={curM} range=[{minM},{maxM}] area='{(this._areaIdToName.TryGetValue(areaId, out var an) ? an : areaId)}'");
                }

                // Ensure a bucket exists for unassigned if any light landed there  <-- NEW
                if (this._entityToAreaId.ContainsValue(UnassignedAreaId))
                {
                    this._areaIdToName[UnassignedAreaId] = UnassignedAreaName;
                }

                if (servicesDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    servicesDoc.RootElement.TryGetProperty("light", out var lightDomain) &&
                    lightDomain.ValueKind == JsonValueKind.Object)
                {
                    foreach (var svc in lightDomain.EnumerateObject())
                    {
                        var svcName = svc.Name;             // e.g., turn_on, turn_off, toggle
                        var svcDef = svc.Value;            // contains fields/target/response
                        // Log a compact summary of fields
                        var fields = "";
                        if (svcDef.ValueKind == JsonValueKind.Object && svcDef.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
                        {
                            var names = new List<String>();
                            foreach (var fld in f.EnumerateObject())
                            {
                                names.Add(fld.Name);
                            }

                            fields = String.Join(", ", names);
                        }
                        PluginLog.Info($"[Service light.{svcName}] fields=[{fields}] target={(svcDef.TryGetProperty("target", out var t) ? "yes" : "no")}");
                    }
                }
                else
                {
                    PluginLog.Warning("No 'light' domain in get_services result.");
                }

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

        private void SetCachedBrightness(String entityId, Int32 bri)
        {
            PluginLog.Verbose($"[SetCachedBrightness] eid={entityId} bri={bri}");
            this._hsbByEntity[entityId] = this._hsbByEntity.TryGetValue(entityId, out var hsb)
                ? ((Double H, Double S, Int32 B))(hsb.H, hsb.S, HSBHelper.Clamp(bri, 0, 255))
                : ((Double H, Double S, Int32 B))(0, 0, HSBHelper.Clamp(bri, 0, 255));
        }


        private void OnHaBrightnessChanged(String entityId, Int32? bri)
        {
            // Only lights
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (this.ShouldIgnoreFrame(entityId, "brightness"))
            {
                return;
            }

            // Update ON/OFF state from brightness signal:
            if (bri.HasValue)
            {
                if (bri.Value <= 0)
                {
                    // OFF → don't change cached B, just mark state off
                    this._isOnByEntity[entityId] = false;
                }
                else
                {
                    // ON → update cached B and mark on
                    this._isOnByEntity[entityId] = true;
                    this.SetCachedBrightness(entityId, HSBHelper.Clamp(bri.Value, 0, 255));
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

            // Figure out current mired
            var cur = this._tempMiredByEntity.TryGetValue(entityId, out var t) ? t.Cur : DefaultWarmMired;
            if (mired.HasValue)
            {
                cur = mired.Value;
            }
            else if (kelvin.HasValue)
            {
                cur = ColorTemp.KelvinToMired(kelvin.Value);
            }

            // Update cache (carry forward bounds unless provided)
            this.SetCachedTempMired(entityId, minM, maxM, cur);

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

            PluginLog.Verbose($"[OnHaHsColorChanged] eid={entityId} h={h?.ToString("F1")} s={s?.ToString("F1")}");


            // Update HS in cache
            if (this._hsbByEntity.TryGetValue(entityId, out var hsb))
            {
                var H = h.HasValue ? HSBHelper.Wrap360(h.Value) : hsb.H;
                var S = s.HasValue ? HSBHelper.Clamp(s.Value, 0, 100) : hsb.S;
                this._hsbByEntity[entityId] = (H, S, hsb.B);
            }
            else
            {
                this._hsbByEntity[entityId] = (HSBHelper.Wrap360(h ?? 0), HSBHelper.Clamp(s ?? 100, 0, 100), 128);
            }

            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                // 🔸 brightness-style: refresh all related wheels
                this.AdjustmentValueChanged(AdjHue);
                this.AdjustmentValueChanged(AdjSat);

            }
        }

        // Small thresholds to avoid UI churn on tiny float changes
        private const Double HueEps = 0.5;     // degrees
        private const Double SatEps = 0.5;     // percent

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

            PluginLog.Verbose($"[OnHaRgbColorChanged] eid={entityId} rgb=[{r},{g},{b}]");


            var (h, s) = HSBHelper.RgbToHs(r.Value, g.Value, b.Value);
            h = HSBHelper.Wrap360(h);
            s = HSBHelper.Clamp(s, 0, 100);

            var cur = this._hsbByEntity.TryGetValue(entityId, out var old) ? old : (0, 100.0, 128);
            var changed = Math.Abs(cur.H - h) >= HueEps || Math.Abs(cur.S - s) >= SatEps;

            if (!changed)
            {
                return;
            }

            this._hsbByEntity[entityId] = (h, s, cur.B);

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

            PluginLog.Verbose($"[OnHaXyColorChanged] eid={entityId} xy=[{xv.ToString("F4")},{yv.ToString("F4")}] bri={bri}");

    // Pick a luminance for XY->RGB: prefer event bri, else cached, else mid
    var baseB = this._hsbByEntity.TryGetValue(entityId, out var old) ? old.B : 128;
    var usedB = HSBHelper.Clamp(bri ?? baseB, 0, 255);

    var (R, G, B) = ColorConv.XyBriToRgb(xv, yv, usedB);
    var (h, s) = HSBHelper.RgbToHs(R, G, B);
    h = HSBHelper.Wrap360(h);
    s = HSBHelper.Clamp(s, 0, 100);

    var cur = this._hsbByEntity.TryGetValue(entityId, out var curHsb) ? curHsb : (0, 100.0, 128);
    var hsChanged  = Math.Abs(cur.H - h) >= HueEps || Math.Abs(cur.S - s) >= SatEps;
    var briChanged = bri.HasValue && usedB != cur.B;

    if (!hsChanged && !briChanged)
            {
                return;
            }

            this._hsbByEntity[entityId] = (h, s, briChanged ? usedB : cur.B);

    if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
    {
        if (hsChanged) { this.AdjustmentValueChanged(AdjHue); this.AdjustmentValueChanged(AdjSat); }
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
                if ((DateTime.UtcNow - t) <= EchoSuppressWindow)
                {
                    if (!String.IsNullOrEmpty(reasonForLog))
                    {
                        PluginLog.Verbose($"[echo] Suppressing frame for {entityId} ({reasonForLog})");
                    }

                    return true;
                }
                // past the window → forget it
                this._lastCmdAt.Remove(entityId);
            }
            return false;
        }




        private Boolean TryGetCachedTempMired(String entityId, out (Int32 Min, Int32 Max, Int32 Cur) t)
            => this._tempMiredByEntity.TryGetValue(entityId, out t);

        private void SetCachedTempMired(String entityId, Int32? minM, Int32? maxM, Int32 curMired)
        {
            var existing = this._tempMiredByEntity.TryGetValue(entityId, out var temp) ? temp : (Min: DefaultMinMireds, Max: DefaultMaxMireds, Cur: DefaultWarmMired);
            var min = minM ?? existing.Min;
            var max = maxM ?? existing.Max;
            var cur = HSBHelper.Clamp(curMired, min, max);
            this._tempMiredByEntity[entityId] = (min, max, cur);
        }
    }
}