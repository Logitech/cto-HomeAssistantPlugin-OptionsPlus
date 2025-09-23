namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks; // for async lambda in DebouncedSender



    using Loupedeck; // ensure this is present

    public class HomeAssistantDynamicFolder : PluginDynamicFolder
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


        private readonly BitmapImage _bulbIconImg;
        private readonly BitmapImage _BackIconImg;
        private readonly BitmapImage _bulbOnImg;
        private readonly BitmapImage _bulbOffImg;
        private readonly BitmapImage _brightnessImg;
        private readonly BitmapImage _retryImg;
        private readonly BitmapImage _saturationImg;
        private readonly BitmapImage _issueStatusImg;
        private readonly BitmapImage _temperatureImg;
        private readonly BitmapImage _onlineStatusImg;
        private readonly BitmapImage _hueImg;





        private const String CmdStatus = "status";
        private const String CmdRetry = "retry";
        private const String CmdBack = "back"; // our own back

        private readonly HaWebSocketClient _client = new();

        private CancellationTokenSource _cts;

        private readonly Dictionary<String, LightItem> _lightsByEntity = new();

        private readonly Dictionary<String, (Double H, Double S, Int32 B)> _hsbByEntity
            = new Dictionary<String, (Double H, Double S, Int32 B)>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<String, LightCaps> _capsByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly CapabilityService _capSvc = new();


        private LightCaps GetCaps(String eid) =>
            this._capsByEntity.TryGetValue(eid, out var c)
                ? c
                : new LightCaps(true, true, false, false); // safe default: on/off + brightness




        // view state
        private Boolean _inDeviceView = false;

        private String _currentEntityId = null;

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





        private readonly LightControlService _lightSvc;
        private readonly IHaClient _ha; // adapter over HaWebSocketClient





        // --- WHEEL: label shown next to the dial
        public override String GetAdjustmentDisplayName(String actionParameter, PluginImageSize _)
        {
            if (actionParameter == AdjBri)
            {
                return this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) ? "Brightness" : "Test Wheel";
            }

            if (actionParameter == AdjTemp)
            {
                return "Color Temp";
            }

            if (actionParameter == AdjHue)
            {
                return "Hue";
            }

            return actionParameter == AdjSat ? "Saturation" : base.GetAdjustmentDisplayName(actionParameter, _);
        }



        // --- WHEEL: small value shown next to the dial
        public override String GetAdjustmentValue(String actionParameter)
        {
            // Brightness wheel
            if (actionParameter == AdjBri)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    if (this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb))
                    {
                        var pct = (Int32)Math.Round(hsb.B * 100.0 / 255.0);
                        return $"{pct}%";
                    }
                    return "— %"; // no cache yet → neutral placeholder
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



        public override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == AdjBri)
            {
                var bri = 128;

                if (this._inDeviceView &&
                    !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsbLocal))
                {
                    bri = hsbLocal.B;
                }

                var pct = (Int32)Math.Round(bri * 100.0 / 255.0);

                Int32 r, g, b;
                if (bri <= 0)
                {
                    // Make background completely black at 0 brightness
                    r = g = b = 0;
                }
                else
                {
                    // Same “warmer/brighter” mapping as before
                    r = Math.Min(30 + pct * 2, 255);
                    g = Math.Min(30 + pct, 220);
                    b = 30;
                }

                using (var bb = new BitmapBuilder(imageSize))
                {
                    bb.Clear(new BitmapColor(r, g, b));

                    if (this._brightnessImg != null)
                    {
                        var pad = (Int32)Math.Round(Math.Min(bb.Width, bb.Height) * 0.10); // 10% padding
                        var side = Math.Min(bb.Width, bb.Height) - pad * 2;
                        var x = (bb.Width - side) / 2;
                        var y = (bb.Height - side) / 2;

                        bb.DrawImage(this._brightnessImg, x, y, side, side);
                    }
                    else
                    {
                        bb.DrawText("☀", fontSize: 58, color: new BitmapColor(255, 235, 140));
                    }

                    return bb.ToImage();
                }
            }



            if (actionParameter == AdjSat)
            {
                Double H = 0, S = 100;
                var B = 128;

                if (this._inDeviceView &&
                    !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb))
                {
                    H = hsb.H;
                    S = Math.Max(0, hsb.S);
                    B = hsb.B;
                }

                var (r, g, b) = HSBHelper.HsbToRgb(HSBHelper.Wrap360(H), S, 100.0 * B / 255.0);

                using (var bb = new BitmapBuilder(imageSize))
                {
                    bb.Clear(new BitmapColor(r, g, b));

                    if (this._saturationImg != null)
                    {
                        // Centered icon with a little padding; scales to fit
                        var pad = (Int32)Math.Round(Math.Min(bb.Width, bb.Height) * 0.08);
                        var side = Math.Min(bb.Width, bb.Height) - pad * 2;
                        var x = (bb.Width - side) / 2;
                        var y = (bb.Height - side) / 2;

                        bb.DrawImage(this._saturationImg, x, y, side, side);
                    }
                    else
                    {
                        // Fallback glyph if icon not available
                        bb.DrawText("S", fontSize: 56, color: new BitmapColor(255, 255, 255));
                    }

                    return bb.ToImage();
                }
            }

            if (actionParameter == AdjTemp)
            {
                // Current temperature in Kelvin (default 3000K)
                var k = 3000;
                if (this._inDeviceView &&
                    !String.IsNullOrEmpty(this._currentEntityId) &&
                    this.TryGetCachedTempMired(this._currentEntityId, out var t))
                {
                    k = ColorTemp.MiredToKelvin(t.Cur);
                }

                // Clamp to a sane range (2000K..6500K) before mapping
                k = Math.Max(2000, Math.Min(6500, k));

                // Map 2000K..6500K to a warmness scale (0..100)
                // higher warmness => warmer background
                var warmness = HSBHelper.Clamp((6500 - k) / 45, 0, 100); // same idea as your code

                // Background tint based on warmness (keep your style)
                var r = Math.Min(35 + warmness * 2, 255);
                var g = Math.Min(35 + (100 - warmness), 255);
                var b = Math.Min(35 + (100 - warmness) / 2, 255);

                using (var bb = new BitmapBuilder(imageSize))
                {
                    bb.Clear(new BitmapColor(r, g, b));

                    if (this._temperatureImg != null)
                    {
                        // Center and scale icon with ~10% padding
                        var pad = (Int32)Math.Round(Math.Min(bb.Width, bb.Height) * 0.10);
                        var side = Math.Min(bb.Width, bb.Height) - pad * 2;
                        var x = (bb.Width - side) / 2;
                        var y = (bb.Height - side) / 2;

                        bb.DrawImage(this._temperatureImg, x, y, side, side);
                    }
                    else
                    {
                        // Fallback glyph if icon is missing
                        bb.DrawText("⟷", fontSize: 58, color: new BitmapColor(255, 240, 180));
                    }

                    return bb.ToImage();
                }
            }


            if (actionParameter == AdjHue)
            {
                Double H = 0, S = 100;
                var B = 128;

                if (this._inDeviceView &&
                    !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb))
                {
                    H = hsb.H;
                    S = Math.Max(40, hsb.S); // ensure some saturation for preview
                    B = hsb.B;
                }

                var (r, g, b) = HSBHelper.HsbToRgb(HSBHelper.Wrap360(H), S, 100.0 * B / 255.0);

                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));

                if (this._hueImg != null)
                {
                    // Draw icon centered with a little padding; scales to fit square
                    var pad = (Int32)Math.Round(Math.Min(bb.Width, bb.Height) * 0.08); // ~8% padding
                    var side = Math.Min(bb.Width, bb.Height) - pad * 2;
                    var x = (bb.Width - side) / 2;
                    var y = (bb.Height - side) / 2;
                    bb.DrawImage(this._hueImg, x, y, side, side);
                }
                else
                {
                    // Fallback glyph
                    bb.DrawText("H", fontSize: 56, color: new BitmapColor(255, 255, 255));
                }

                return bb.ToImage();
            }



            return null;
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

                    var curH = this._hsbByEntity.TryGetValue(eid, out var hsb3) ? hsb3.H : 0;
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

                    var curS = this._hsbByEntity.TryGetValue(eid, out var hsb2) ? hsb2.S : 100;
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

                    var (minM, maxM, curM) = this._tempMiredByEntity.TryGetValue(eid, out var t)
                        ? t
                        : (DefaultMinMireds, DefaultMaxMireds, DefaultWarmMired);

                    var step = diff * TempStepMireds;
                    step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxMiredsPerEvent);

                    var targetM = HSBHelper.Clamp(curM + step, minM, maxM);

                    // Optimistic UI
                    this.SetCachedTempMired(eid, null, null, targetM);
                    this.AdjustmentValueChanged(AdjTemp);
                    this._lightSvc.SetTempMired(eid, targetM);
                }
            }



            return;
        }







        private BitmapImage LoadIcon(String resourceName, out Boolean ok)
        {
            var img = PluginResources.ReadImage(resourceName);
            if (img == null)
            {
                PluginLog.Error($"[HA] Embedded icon not found: {resourceName}");
                ok = false;
            }
            else
            {
                PluginLog.Info($"[HA] Embedded icon loaded OK: {resourceName}");
                ok = true;
            }
            return img;
        }

        public HomeAssistantDynamicFolder()
        {
            this.DisplayName = "Home Assistant";
            this.GroupName = "Smart Home";

            try
            {
                //var names = string.Join(", ", typeof(HomeAssistantDynamicFolder).Assembly.GetManifestResourceNames());
                //PluginLog.Info("[HA RES] " + names);

                // Idempotent; safe even if the plugin already called it
                PluginResources.Init(typeof(HomeAssistantPlugin).Assembly);

                Boolean _;
                this._bulbIconImg      = this.LoadIcon("light_bulb_icon.png", out _);
                this._BackIconImg      = this.LoadIcon("back_icon.png", out _);
                this._bulbOnImg        = this.LoadIcon("light_on_icon.png", out _);
                this._bulbOffImg       = this.LoadIcon("light_off_icon.png", out _);
                this._brightnessImg    = this.LoadIcon("brightness_icon.png", out _);
                this._retryImg         = this.LoadIcon("reload_icon.png", out _);
                this._saturationImg    = this.LoadIcon("saturation_icon.png", out _);
                this._issueStatusImg   = this.LoadIcon("issue_status_icon.png", out _);
                this._temperatureImg   = this.LoadIcon("temperature_icon.png", out _);
                this._onlineStatusImg  = this.LoadIcon("online_status_icon.png", out _);
                this._hueImg           = this.LoadIcon("hue_icon.png", out _);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[HA] ctor: failed to read embedded icon — continuing without it");
            }

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
            yield return this.CreateCommandName(CmdBack);   // system Back
            yield return this.CreateCommandName(CmdStatus);           // keep Status always

            if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
            {
                var caps = this.GetCaps(this._currentEntityId);

                // Device actions
                yield return this.CreateCommandName($"{PfxActOn}{this._currentEntityId}");
                yield return this.CreateCommandName($"{PfxActOff}{this._currentEntityId}");

                // Only show controls the device actually supports
                if (caps.Brightness)
                {
                    yield return this.CreateAdjustmentName(AdjBri);
                }

                if (caps.ColorTemp)
                {
                    yield return this.CreateAdjustmentName(AdjTemp);
                }

                if (caps.ColorHs)
                {
                    yield return this.CreateAdjustmentName(AdjHue);
                    yield return this.CreateAdjustmentName(AdjSat);
                }
            }
            else
            {
                // Root view unchanged...
                foreach (var kv in this._lightsByEntity)
                {
                    yield return this.CreateCommandName($"{PfxDevice}{kv.Key}");
                }

                yield return this.CreateCommandName(CmdRetry);
            }

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

            return actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase) ? "Off" : null;
        }

        // Paint the tile: green when OK, red on error
        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {




            if (actionParameter == CmdBack)
            {
                return this._BackIconImg;
            }
            if (actionParameter == CmdRetry)
            {
                return this._retryImg;
            }

            // STATUS (unchanged)
            if (actionParameter == CmdStatus)
            {
                var ok = HealthBus.State == HealthState.Ok;

                using (var bb = new BitmapBuilder(imageSize))
                {
                    // Use the corresponding status image as background, or fallback to solid color
                    if (ok)
                    {
                        if (this._onlineStatusImg != null)
                        {
                            bb.SetBackgroundImage(this._onlineStatusImg);
                        }
                        else
                        {
                            bb.Clear(new BitmapColor(0, 160, 60)); // fallback green
                        }
                    }
                    else
                    {
                        if (this._issueStatusImg != null)
                        {
                            bb.SetBackgroundImage(this._issueStatusImg);
                        }
                        else
                        {
                            bb.Clear(new BitmapColor(200, 30, 30)); // fallback red
                        }
                    }

                    // Keep the label on top
                    bb.DrawText(ok ? "ONLINE" : "ISSUE", fontSize: 22, color: new BitmapColor(255, 255, 255));

                    return bb.ToImage();
                }
            }


            // DEVICE tiles (light bulbs)
            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                return this._bulbIconImg;
            }

            // ACTION tiles
            if (actionParameter.StartsWith(PfxActOn, StringComparison.OrdinalIgnoreCase))
            {
                return this._bulbOnImg;
            }
            if (actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase))
            {
                return this._bulbOffImg;
            }

            // Retry: no custom image
            return null;
        }





        public override void RunCommand(String actionParameter)
        {

            PluginLog.Info($"RunCommand: {actionParameter}");


            if (actionParameter == CmdBack)
            {
                if (this._inDeviceView)
                {
                    // 🔸 brightness-style cleanup for the current entity
                    this._lightSvc.CancelPending(this._currentEntityId);

                    this._lightSvc.CancelPending(this._currentEntityId);


                    PluginLog.Info("LEAVE device view -> root");
                    this._inDeviceView = false;
                    this._currentEntityId = null;

                    this.ButtonActionNamesChanged();
                    this.EncoderActionNamesChanged();
                }
                else
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


            // Enter device view
            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                PluginLog.Info($"Entering Device view");
                var entityId = actionParameter.Substring(PfxDevice.Length);
                if (this._lightsByEntity.ContainsKey(entityId))
                {
                    

                    this._inDeviceView = true;
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

                    PluginLog.Info($"ENTER device view: {entityId}");
                }
                return;
            }


            // Actions
            if (actionParameter.StartsWith(PfxActOn, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActOn.Length);
                this._lightSvc.TurnOnAsync(entityId);
                return;
            }
            if (actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActOff.Length);
                this._lightSvc.TurnOffAsync(entityId);
                return;
            }
        }










        // 🔧 return bools here:
        public override Boolean Load()
        {
            PluginLog.Info("DynamicFolder.Load()");
            HealthBus.HealthChanged += this.OnHealthChanged;



            return true;
        }

        public override Boolean Unload()
        {
            PluginLog.Info("DynamicFolder.Unload()");



            // New debounced sender
            this._lightSvc?.Dispose();

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
            _ = this._events.SafeCloseAsync();
            //this.Plugin.OnPluginStatusChanged(PluginStatus.Warning, "Folder closed.", null);
            return true;
        }

        private void OnHealthChanged(Object sender, EventArgs e)
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
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Set Home Assistant Base URL in plugin settings.", null);
                return false;
            }

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var token) ||
                String.IsNullOrWhiteSpace(token))
            {
                PluginLog.Warning("Missing ha.token setting");
                HealthBus.Error("Missing Token");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Set Home Assistant Long-Lived Token in plugin settings.", null);
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
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Normal, "Connected to Home Assistant.", null);

                    try
                    {
                        var ctsEv = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        this._events.BrightnessChanged -= this.OnHaBrightnessChanged; // avoid dup
                        this._events.BrightnessChanged += this.OnHaBrightnessChanged;

                        this._events.ColorTempChanged -= this.OnHaColorTempChanged;
                        this._events.ColorTempChanged += this.OnHaColorTempChanged;

                        this._events.HsColorChanged -= this.OnHaHsColorChanged;
                        this._events.HsColorChanged += this.OnHaHsColorChanged;

                        _ = this._events.ConnectAndSubscribeAsync(baseUrl, token, ctsEv.Token); // fire-and-forget
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
                    this.ButtonActionNamesChanged();
                    this.EncoderActionNamesChanged();
                    return true;

                }

                HealthBus.Error(msg ?? "Auth failed");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, msg, null);
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "AuthenticateSync failed");
                HealthBus.Error("Auth error");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Auth error. See plugin logs.", null);
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

                // ---- Parse results ----
                // states: array of { entity_id, state, attributes{friendly_name,...}, ...}
                using var statesDoc = JsonDocument.Parse(statesJson);
                // services: object { domain: { service: { fields, target, response } } }
                using var servicesDoc = JsonDocument.Parse(servicesJson);


                JsonElement entArray = default;
                JsonElement devArray = default;
                if (okEnt)
                {
                    entArray = JsonDocument.Parse(entJson).RootElement;
                }

                if (okDev)
                {
                    devArray = JsonDocument.Parse(devJson).RootElement;
                }

                // Build device lookup by device_id
                var deviceById = new Dictionary<String, (String name, String mf, String model)>(StringComparer.OrdinalIgnoreCase);
                if (okDev && devArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dev in devArray.EnumerateArray())
                    {
                        var id = dev.GetPropertyOrDefault("id");
                        var name = dev.GetPropertyOrDefault("name_by_user") ?? dev.GetPropertyOrDefault("name") ?? "";
                        var mf = dev.GetPropertyOrDefault("manufacturer") ?? "";
                        var model = dev.GetPropertyOrDefault("model") ?? "";
                        if (!String.IsNullOrEmpty(id))
                        {
                            deviceById[id] = (name, mf, model);
                        }
                    }
                }

                // Build entity->device_id map (and keep original names)
                var entityDevice = new Dictionary<String, (String deviceId, String originalName)>(StringComparer.OrdinalIgnoreCase);
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
                        entityDevice[entityId] = (deviceId, oname);
                    }
                }

                // Filter lights from states and assemble LightItem
                this._lightsByEntity.Clear();
                this._hsbByEntity.Clear();

                foreach (var st in statesDoc.RootElement.EnumerateArray())
                {
                    var entityId = st.GetPropertyOrDefault("entity_id");
                    if (String.IsNullOrEmpty(entityId) || !entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var state = st.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var attrs = st.TryGetProperty("attributes", out var a) ? a : default;
                    var friendly = (attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("friendly_name", out var fn))
                                   ? fn.GetString() ?? entityId
                                   : entityId;

                    String deviceId = null, deviceName = "", mf = "", model = "";
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

                    // --- Brightness: seed for ALL lights, not just color-capable ---
                    var bri = 0;
                    if (attrs.ValueKind == JsonValueKind.Object &&
                        attrs.TryGetProperty("brightness", out var br) &&
                        br.ValueKind == JsonValueKind.Number)
                    {
                        bri = HSBHelper.Clamp(br.GetInt32(), 0, 255);
                    }
                    else
                    {
                        // if HA says "off" often brightness is omitted → treat as 0
                        bri = String.Equals(state, "off", StringComparison.OrdinalIgnoreCase) ? 0 : 128;
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

                    PluginLog.Info($"[Light] {entityId} | name='{friendly}' | state={state} | dev='{deviceName}' mf='{mf}' model='{model}' bri={bri} tempMired={curM} range=[{minM},{maxM}]");
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
            if (this._hsbByEntity.TryGetValue(entityId, out var hsb))
            {
                this._hsbByEntity[entityId] = (hsb.H, hsb.S, HSBHelper.Clamp(bri, 0, 255));
            }
            else
            {
                this._hsbByEntity[entityId] = (0, 0, HSBHelper.Clamp(bri, 0, 255));
            }
        }


        private void OnHaBrightnessChanged(String entityId, Int32? bri)
        {
            // We only care about lights we know
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Update cache
            if (bri.HasValue)
            {
                this.SetCachedBrightness(entityId, HSBHelper.Clamp(bri.Value, 0, 255));
            }

            // If this is the active device, redraw the wheel value & image
            if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                this.AdjustmentValueChanged(AdjBri);
            }
        }

        private void OnHaColorTempChanged(String entityId, Int32? mired, Int32? kelvin, Int32? minM, Int32? maxM)
        {
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
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




        private Boolean TryGetCachedTempMired(String entityId, out (Int32 Min, Int32 Max, Int32 Cur) t)
            => this._tempMiredByEntity.TryGetValue(entityId, out t);

        private void SetCachedTempMired(String entityId, Int32? minM, Int32? maxM, Int32 curMired)
        {
            var min = minM ?? (this._tempMiredByEntity.TryGetValue(entityId, out var old) ? old.Min : DefaultMinMireds);
            var max = maxM ?? (this._tempMiredByEntity.TryGetValue(entityId, out var old2) ? old2.Max : DefaultMaxMireds);
            var cur = HSBHelper.Clamp(curMired, min, max);
            this._tempMiredByEntity[entityId] = (min, max, cur);
        }
    }
}
