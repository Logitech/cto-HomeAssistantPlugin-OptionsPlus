namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;

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

        
        private BitmapImage _bulbIconImg;





        private const String CmdStatus = "status";
        private const String CmdRetry = "retry";
        private const String CmdBack = "back"; // our own back

        private readonly HaWebSocketClient _client = new();

        private CancellationTokenSource _cts;

        private Dictionary<String, LightItem> _lightsByEntity = new();
        private Dictionary<String, JsonElement> _lightServices = new(); // serviceName -> fields/target/response
                                                                        // HSB cache per entity (Hue 0–360, Sat 0–100, Bri 0–255)
        private readonly Dictionary<String, (Double H, Double S, Int32 B)> _hsbByEntity
            = new Dictionary<String, (Double H, Double S, Int32 B)>(StringComparer.OrdinalIgnoreCase);

        // view state
        private Boolean _inDeviceView = false;

        private String _currentEntityId = null;

        // action parameter prefixes
        private const String PfxDevice = "device:"; // device:<entity_id>
        private const String PfxActOn = "act:on:"; // act:on:<entity_id>
        private const String PfxActOff = "act:off:"; // act:off:<entity_id>

        // --- WHEEL: constants & state
        private const String AdjWheel = "adj:wheel";     // a single wheel entry
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
        private const string AdjHue = "adj:ha-hue";   // wheel id

        private const int HueStepDegPerTick = 1;      // 1° per tick feels smooth
        private const int MaxHueDegPerEvent = 30;     // cap coalesced bursts

        // ===== SATURATION control =====
        private const string AdjSat = "adj:ha-sat";

        private const int SatStepPctPerTick = 1;   // feels smooth
        private const int MaxSatPctPerEvent = 15;  // cap burst coalesce

        // Target saturation (per-entity) to support debounced sending
        private readonly Dictionary<string, double> _sTargetPct =
            new(StringComparer.OrdinalIgnoreCase);


        // Debounce + targets
        private readonly Dictionary<string, double> _hTargetDeg = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (double H, double S)> _hsLastSent = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, System.Timers.Timer> _sendHueTimers = new(StringComparer.OrdinalIgnoreCase);



        // Per-entity cache: (Min, Max, Current) in Mireds
        private readonly Dictionary<String, (Int32 Min, Int32 Max, Int32 Cur)> _tempMiredByEntity =
            new(StringComparer.OrdinalIgnoreCase);


        // Debounce/send machinery
        private readonly Dictionary<String, Int32> _tempTargetMired = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, Int32> _tempLastSentMired = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, System.Timers.Timer> _sendTempTimers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, System.Timers.Timer> _reconcileTempTimers =
            new(StringComparer.OrdinalIgnoreCase);

        // Conversions
        private static Int32 MiredToKelvin(Int32 m) => (Int32)Math.Round(1_000_000.0 / Math.Max(1, m));
        private static Int32 KelvinToMired(Int32 k) => (Int32)Math.Round(1_000_000.0 / Math.Max(1, k));
        private static Int32 Clamp(Int32 v, Int32 lo, Int32 hi) => Math.Min(Math.Max(v, lo), hi);



        // Debounced, target-based brightness sending
        private readonly Dictionary<String, Int32> _briTarget = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, Int32> _briLastSent = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, System.Timers.Timer> _sendTimers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, System.Timers.Timer> _reconcileTimers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Object _sendGate = new Object();

        // Tune these if you want
        private const Int32 SendDebounceMs = 100; // how long to wait after the last tick before sending
        private const Int32 ReconcileIdleMs = 500; // idle pause before doing a single get_states as truth
        private const Int32 MaxPctPerEvent = 10;  // cap huge coalesced diffs to keep UI sane

        private readonly HaEventListener _events = new();










        // --- WHEEL: label shown next to the dial
        public override String GetAdjustmentDisplayName(String actionParameter, PluginImageSize _)
        {
            if (actionParameter == AdjWheel)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    return "Brightness";
                }

                return "Test Wheel";
            }

            if (actionParameter == AdjTemp)
                return (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId)) ? "Color Temp" : "Color Temp";
            if (actionParameter == AdjHue)
                return "Hue";
            if (actionParameter == AdjSat)
                return "Saturation";


            return base.GetAdjustmentDisplayName(actionParameter, _);
        }



        // --- WHEEL: small value shown next to the dial
        public override String GetAdjustmentValue(String actionParameter)
        {
            // Brightness wheel
            if (actionParameter == AdjWheel)
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
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb))
                {
                    return $"{(Int32)Math.Round(HSBHelper.Clamp(hsb.S, 0, 100))}%";
                }
                return "—%";
            }


            if (actionParameter == AdjHue)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb))
                {
                    return $"{(Int32)Math.Round(HSBHelper.Wrap360(hsb.H))}°";
                }
                return "—°";
            }

            // Color Temperature wheel
            if (actionParameter == AdjTemp)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    if (this._tempMiredByEntity.TryGetValue(this._currentEntityId, out var t))
                    {
                        var k = MiredToKelvin(t.Cur);
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
            if (actionParameter == AdjWheel)
            {

                Int32 bri = 128;

                if (this._inDeviceView &&
            !String.IsNullOrEmpty(this._currentEntityId) &&
            this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsbLocal))
                {
                    bri = hsbLocal.B;
                }

                var pct = (Int32)Math.Round(bri * 100.0 / 255.0);

                // Background tint: warmer/brighter as brightness increases
                Int32 r = 30 + (pct * 2);
                if (r > 255)
                    r = 255;
                Int32 g = 30 + (pct);
                if (g > 220)
                    g = 220;
                Int32 b = 30;

                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                bb.DrawText("☀", fontSize: 58, color: new BitmapColor(255, 235, 140));
                return bb.ToImage();
            }

            if (actionParameter == AdjSat)
            {
                double H = 0, S = 100;
                int B = 128;
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb))
                {
                    H = hsb.H;
                    S = Math.Max(0, hsb.S);
                    B = hsb.B;
                }

                var (r, g, b) = HSBHelper.HsbToRgb(HSBHelper.Wrap360(H), S, 100.0 * B / 255.0);
                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                bb.DrawText("S", fontSize: 56, color: new BitmapColor(255, 255, 255));
                return bb.ToImage();

            }
            if (actionParameter == AdjTemp)
            {
                // Render a tiny gradient hint based on warm/cool
                Int32 k = 3000;
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) &&
                    TryGetCachedTempMired(this._currentEntityId, out var t))
                    k = MiredToKelvin(t.Cur);

                // Map 2000K..6500K to a color-ish background (just a hint)
                var warmness = Math.Clamp((6500 - k) / 45, 0, 100); // arbitrary scale
                var r = 35 + warmness * 2;
                var g = 35 + (100 - warmness);
                var b = 35 + (100 - warmness) / 2;

                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                bb.DrawText("⟷", fontSize: 58, color: new BitmapColor(255, 240, 180));
                return bb.ToImage();

            }

            if (actionParameter == AdjHue)
            {
                double H = 0, S = 100;
                int B = 128;

                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) &&
                    this._hsbByEntity.TryGetValue(this._currentEntityId, out var hsb))
                {
                    H = hsb.H;
                    S = Math.Max(40, hsb.S); // ensure some saturation for preview
                    B = hsb.B;
                }

                var (r, g, b) = HSBHelper.HsbToRgb(HSBHelper.Wrap360(H), S, 100.0 * B / 255.0);

                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                bb.DrawText("H", fontSize: 56, color: new BitmapColor(255, 255, 255));
                return bb.ToImage();
            }


            return null;
        }




        // --- WHEEL: rotation handler (like your CounterAdjustment.ApplyAdjustment)
        public override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (actionParameter == AdjWheel && diff != 0)
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
                        SetCachedBrightness(entityId, targetB);
                        _briTarget[entityId] = targetB;
                        this.AdjustmentValueChanged(actionParameter);

                        // schedule one send for the latest target (debounced)
                        ScheduleSend(entityId, SendDebounceMs);


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
                    var eid = this._currentEntityId;

                    // Current HS from cache (fallbacks)
                    if (!this._hsbByEntity.TryGetValue(eid, out var hsb))
                        hsb = (0, 100, 128);

                    // Compute step with cap and clamp 0..100
                    var step = diff * SatStepPctPerTick;
                    step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxSatPctPerEvent);
                    var newS = HSBHelper.Clamp(hsb.S + step, 0, 100);

                    // Optimistic UI
                    this._hsbByEntity[eid] = (hsb.H, newS, hsb.B);
                    _sTargetPct[eid] = newS;
                    this.AdjustmentValueChanged(AdjSat);
                    this.AdjustmentValueChanged(AdjHue);
                    

                    // Debounced send — reuse the same sender as Hue
                    ScheduleHueSend(eid, SendDebounceMs); // <- intentionally reusing Hue's scheduling/sender

                }
            }
            if (actionParameter == AdjHue && diff != 0)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var eid = this._currentEntityId;

                    // Current HS from cache (fallbacks)
                    if (!this._hsbByEntity.TryGetValue(eid, out var hsb))
                        hsb = (0, 100, 128); // default to vivid color, mid brightness

                    // Compute step with cap; wrap 0..360
                    var step = diff * HueStepDegPerTick;
                    step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxHueDegPerEvent);
                    var newH = HSBHelper.Wrap360(hsb.H + step);

                    // Optimistic UI
                    this._hsbByEntity[eid] = (newH, hsb.S, hsb.B);
                    _hTargetDeg[eid] = newH;
                    this.AdjustmentValueChanged(AdjHue);
                    this.AdjustmentValueChanged(AdjSat);

                    // Debounced send
                    ScheduleHueSend(eid, SendDebounceMs);
                }

            }


            if (actionParameter == AdjTemp && diff != 0)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var eid = this._currentEntityId;

                    var (minM, maxM, curM) = _tempMiredByEntity.TryGetValue(eid, out var t)
                        ? t
                        : (DefaultMinMireds, DefaultMaxMireds, DefaultWarmMired);

                    var step = diff * TempStepMireds;
                    step = Math.Sign(step) * Math.Min(Math.Abs(step), MaxMiredsPerEvent);

                    var targetM = Clamp(curM + step, minM, maxM);

                    // Optimistic UI
                    SetCachedTempMired(eid, null, null, targetM);
                    _tempTargetMired[eid] = targetM;
                    this.AdjustmentValueChanged(AdjTemp);

                    // Debounced send
                    ScheduleTempSend(eid, SendDebounceMs);
                }
            }



            return;
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

                _bulbIconImg = PluginResources.ReadImage("light_bulb_icon.png");
                if (_bulbIconImg == null)
                    PluginLog.Error("[HA] Embedded icon not found: light_bulb_icon.png");
                else
                    PluginLog.Info("[HA] Embedded icon loaded OK");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[HA] ctor: failed to read embedded icon — continuing without it");
            }
        }

        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) =>
            PluginDynamicFolderNavigation.None;


        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _)
        {
            yield return this.CreateCommandName(CmdBack);   // system Back
            yield return this.CreateCommandName(CmdStatus);           // keep Status always

            if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
            {
                // Device view: show actions for the selected entity
                yield return this.CreateCommandName($"{PfxActOn}{this._currentEntityId}");
                yield return this.CreateCommandName($"{PfxActOff}{this._currentEntityId}");
                yield return this.CreateAdjustmentName(AdjWheel);
                yield return this.CreateAdjustmentName(AdjTemp);
                yield return this.CreateAdjustmentName(AdjHue);
                yield return this.CreateAdjustmentName(AdjSat);


            }
            else
            {
                // Root view: one button per light entity
                foreach (var kv in this._lightsByEntity)
                {
                    yield return this.CreateCommandName($"{PfxDevice}{kv.Key}");
                }

                // Keep Retry last
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

            if (actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase))
            {
                return "Off";
            }

            return null;
        }

        // Paint the tile: green when OK, red on error
        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {




            if (actionParameter == CmdBack)
            {
                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(30, 30, 30));
                bb.DrawText("←", fontSize: 65, color: new BitmapColor(255, 255, 200));
                return bb.ToImage();
            }

            // STATUS (unchanged)
            if (actionParameter == CmdStatus)
            {
                var ok = HealthBus.State == HealthState.Ok;
                var bb = new BitmapBuilder(imageSize);
                bb.Clear(ok ? new BitmapColor(0, 160, 60) : new BitmapColor(200, 30, 30));
                bb.DrawText(ok ? "ONLINE" : "ISSUE");
                return bb.ToImage();
            }

            // DEVICE tiles (light bulbs)
            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                return _bulbIconImg;
            }

            // ACTION tiles
            if (actionParameter.StartsWith(PfxActOn, StringComparison.OrdinalIgnoreCase))
            {
                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(0, 110, 255));
                bb.DrawText("ON");
                return bb.ToImage();
            }
            if (actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase))
            {
                var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(80, 80, 80));
                bb.DrawText("OFF");
                return bb.ToImage();
            }

            // Retry: no custom image
            return null;
        }





        public override void RunCommand(String actionParameter)
        {

            PluginLog.Info($"RunCommand: {actionParameter}");


            if (actionParameter == AdjWheel)
            {
                try
                {
                    if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        var (ok, err) = this._client.CallServiceAsync("light", "toggle", this._currentEntityId, null, cts.Token)
                                                    .GetAwaiter().GetResult();
                        if (ok)
                        {
                            PluginLog.Info($"[wheel] toggle -> {_currentEntityId} OK");
                        }
                        else
                        {
                            PluginLog.Warning($"[wheel] toggle FAILED: {err}");
                        }
                    }
                    else
                    {
                        this._wheelCounter = 0;
                        this.AdjustmentValueChanged(AdjWheel);
                        PluginLog.Info("[wheel] counter reset");
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[wheel] RunCommand toggle/reset exception");
                }
            }


            if (actionParameter == CmdBack)
{
    if (this._inDeviceView)
    {
        // 🔸 brightness-style cleanup for the current entity
        CancelEntityTimers(this._currentEntityId);
        ClearEntityTargets(this._currentEntityId);

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
                var entityId = actionParameter.Substring(PfxDevice.Length);
                if (this._lightsByEntity.ContainsKey(entityId))
                {
                    // stop any pending debounced sends for the previous device
                    var prev = this._currentEntityId;
                    CancelEntityTimers(prev);

                    // 🔸 brightness-style fix: clear ALL targets/last-sent for the previous entity
                    ClearEntityTargets(prev);

                    this._inDeviceView = true;
                    this._currentEntityId = entityId;
                    this._wheelCounter = 0; // avoids showing previous ticks anywhere

                    // ensure we have a cache entry for the new entity (like brightness did)
                    if (!this._hsbByEntity.ContainsKey(entityId))
                        this._hsbByEntity[entityId] = (0, 0, 0);
                    if (!this._tempMiredByEntity.ContainsKey(entityId))
                        this._tempMiredByEntity[entityId] = (DefaultMinMireds, DefaultMaxMireds, DefaultWarmMired);

                    this.ButtonActionNamesChanged();       // swap to device actions

                    // 🔸 brightness-style UI refresh: force all wheels to redraw immediately
                    this.AdjustmentValueChanged(AdjWheel);
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
                this.CallLightService(entityId, "turn_on");
                return;
            }
            if (actionParameter.StartsWith(PfxActOff, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActOff.Length);
                this.CallLightService(entityId, "turn_off");
                return;
            }
        }




        private void CallLightService(String entityId, String service)
        {
            try
            {
                using var ctsCall = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                // If you added EnsureConnectedAsync, keep it:
                var ensured = this._client.EnsureConnectedAsync(TimeSpan.FromSeconds(8), ctsCall.Token)
                                     .GetAwaiter().GetResult();
                if (!ensured)
                {
                    PluginLog.Warning("Connection to Home Assistant lost.");
                    HealthBus.Error("Connection lost");
                    //this.ButtonActionNamesChanged();     double redraw
                    return;
                }

                var (ok, err) = this._client.CallServiceAsync("light", service, entityId, null, ctsCall.Token)
                                       .GetAwaiter().GetResult();

                if (ok)
                {
                    PluginLog.Info($"[call_service] light.{service} -> {entityId} OK");
                    HealthBus.Ok($"light.{service}");
                    //this.ButtonActionNamesChanged();          double redraw
                }
                else
                {
                    var msg = err?.ToLowerInvariant() switch
                    {
                        "timeout" => "Service timeout",
                        "connection lost" => "Connection lost",
                        _ when String.IsNullOrEmpty(err) => "Service failed",
                        _ => err
                    };
                    PluginLog.Warning($"[call_service] light.{service} -> {entityId} faileeeeed: {msg}");
                    HealthBus.Error(msg);
                    //this.ButtonActionNamesChanged();          double redraw
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[call_service] light.{service} exception");
                HealthBus.Error("Service error");
                //this.ButtonActionNamesChanged();               double redraw
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
            lock (_sendGate)
            {
                foreach (var t in _sendTimers.Values)
                    try
                    { t.Stop(); t.Dispose(); }
                    catch { }
            }

            HealthBus.HealthChanged -= this.OnHealthChanged;
            return true;
        }

        public override Boolean Activate()
        {
            PluginLog.Info("DynamicFolder.Activate() -> authenticate");
            bool ret = AuthenticateSync();
            this.EncoderActionNamesChanged();
            return ret; // now returns bool (see below)
        }

        public override Boolean Deactivate()
        {
            PluginLog.Info("DynamicFolder.Deactivate() -> close WS");
            this._cts?.Cancel();
            this._client.SafeCloseAsync().GetAwaiter().GetResult();
            _ = _events.SafeCloseAsync();
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
                    .ConnectAndAuthenticateAsync(baseUrl, token, TimeSpan.FromSeconds(8), this._cts.Token)
                    .GetAwaiter().GetResult();

                if (ok)
                {
                    HealthBus.Ok("Auth OK");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Normal, "Connected to Home Assistant.", null);

                    try
                    {
                        var ctsEv = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        _events.BrightnessChanged -= OnHaBrightnessChanged; // avoid dup
                        _events.BrightnessChanged += OnHaBrightnessChanged;

                        _events.ColorTempChanged -= this.OnHaColorTempChanged;
                        _events.ColorTempChanged += this.OnHaColorTempChanged;

                        _events.HsColorChanged -= this.OnHaHsColorChanged;
                        _events.HsColorChanged += this.OnHaHsColorChanged;

                        _ = _events.ConnectAndSubscribeAsync(baseUrl, token, ctsEv.Token); // fire-and-forget
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
                        continue;

                    var state = st.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var attrs = st.TryGetProperty("attributes", out var a) ? a : default;
                    var friendly = (attrs.ValueKind == JsonValueKind.Object && attrs.TryGetProperty("friendly_name", out var fn))
                                   ? fn.GetString() ?? entityId
                                   : entityId;

                    String deviceId = null, deviceName = "", mf = "", model = "";

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
                    Int32 bri = 0;
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
                            minM = v1.GetInt32();
                        if (attrs.TryGetProperty("max_mireds", out var v2) && v2.ValueKind == JsonValueKind.Number)
                            maxM = v2.GetInt32();

                        if (attrs.TryGetProperty("color_temp", out var v3) && v3.ValueKind == JsonValueKind.Number)
                            curM = Clamp(v3.GetInt32(), minM, maxM);
                        else if (attrs.TryGetProperty("color_temp_kelvin", out var v4) && v4.ValueKind == JsonValueKind.Number)
                            curM = Clamp(KelvinToMired(v4.GetInt32()), minM, maxM);
                        else if (String.Equals(state, "off", StringComparison.OrdinalIgnoreCase))
                            curM = DefaultWarmMired;

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
                    _tempMiredByEntity[entityId] = (minM, maxM, curM);

                    var li = new LightItem(entityId, friendly, state, deviceId ?? "", deviceName, mf, model);
                    this._lightsByEntity[entityId] = li;

                    PluginLog.Info($"[Light] {entityId} | name='{friendly}' | state={state} | dev='{deviceName}' mf='{mf}' model='{model}' bri={bri} tempMired={curM} range=[{minM},{maxM}]");
                }


                // Extract only LIGHT domain services and store
                this._lightServices.Clear();
                if (servicesDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    servicesDoc.RootElement.TryGetProperty("light", out var lightDomain) &&
                    lightDomain.ValueKind == JsonValueKind.Object)
                {
                    foreach (var svc in lightDomain.EnumerateObject())
                    {
                        var svcName = svc.Name;             // e.g., turn_on, turn_off, toggle
                        var svcDef = svc.Value;            // contains fields/target/response
                        this._lightServices[svcName] = svcDef;
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

        // --- WHEEL: tiny helper to build JsonElement payloads safely
        private static JsonElement ToJsonElement(Object obj)
        {
            using var doc = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(obj));
            return doc.RootElement.Clone();
        }


        private bool TryGetCachedBrightness(string entityId, out int bri)
        {
            if (this._hsbByEntity.TryGetValue(entityId, out var hsb))
            {
                bri = hsb.B;
                return true;
            }
            bri = 0;
            return false;
        }

        private void SetCachedBrightness(string entityId, int bri)
        {
            if (this._hsbByEntity.TryGetValue(entityId, out var hsb))
                this._hsbByEntity[entityId] = (hsb.H, hsb.S, HSBHelper.Clamp(bri, 0, 255));
            else
                this._hsbByEntity[entityId] = (0, 0, HSBHelper.Clamp(bri, 0, 255));
        }

        // Quick, synchronous read of a single entity’s brightness via WS get_states
        private int? GetBrightnessFromHa(string entityId)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var (ok, statesJson, err) = this._client.RequestAsync("get_states", cts.Token)
                                                        .GetAwaiter().GetResult();
                if (!ok || statesJson is null)
                {
                    PluginLog.Warning($"[wheel] get_states failed: {err}");
                    return null;
                }

                using var doc = System.Text.Json.JsonDocument.Parse(statesJson);
                foreach (var st in doc.RootElement.EnumerateArray())
                {
                    var id = st.GetPropertyOrDefault("entity_id");
                    if (!String.Equals(id, entityId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (st.TryGetProperty("attributes", out var attrs) &&
                        attrs.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        attrs.TryGetProperty("brightness", out var br) &&
                        br.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        var b = HSBHelper.Clamp(br.GetInt32(), 0, 255);
                        PluginLog.Info($"[wheel] HA reports brightness={b} for {entityId}");
                        return b;
                    }

                    // When off, HA often omits brightness entirely → treat as 0
                    var stateStr = st.TryGetProperty("state", out var s) ? s.GetString() : null;
                    if (String.Equals(stateStr, "off", StringComparison.OrdinalIgnoreCase))
                        return 0;

                    return null;
                }
                return null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[wheel] GetBrightnessFromHa exception");
                return null;
            }
        }

        private void ScheduleHueSend(String entityId, Int32 delayMs)
        {
            lock (_sendGate)
            {
                if (!_sendHueTimers.TryGetValue(entityId, out var t))
                {
                    t = new System.Timers.Timer { AutoReset = false };
                    t.Elapsed += (s, e) => SafeFireHueSend(entityId);
                    _sendHueTimers[entityId] = t;
                }
                t.Interval = delayMs;
                t.Stop();
                t.Start();
            }
        }

        private void SafeFireHueSend(String entityId)
        {
            try
            { SendLatestHueAsync(entityId).GetAwaiter().GetResult(); }
            catch (Exception ex) { PluginLog.Warning(ex, $"[hue] send timer for {entityId}"); }
        }

        private async Task SendLatestHueAsync(String entityId)
        {
            double H, S;

            lock (_sendGate)
            {
                if (!_hTargetDeg.TryGetValue(entityId, out H))
                {
                    // We still want to allow a “sat-only” change to send HS; if no hue target,
                    // take H from cache (or 0).
                    H = this._hsbByEntity.TryGetValue(entityId, out var hsbH) ? hsbH.H : 0;
                }

                // PREFER saturation target if set; else take from cache (or 100 if unknown)
                if (_sTargetPct.TryGetValue(entityId, out var st))
                    S = st;
                else if (this._hsbByEntity.TryGetValue(entityId, out var hsb))
                    S = hsb.S;
                else
                    S = 100;

                // Skip if negligible vs last-sent
                if (_hsLastSent.TryGetValue(entityId, out var last) &&
                    Math.Abs(last.H - H) < 0.5 && Math.Abs(last.S - S) < 0.5)
                    return;
            }

            if (!this._client.IsAuthenticated)
            {
                HealthBus.Error("Connection lost");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var data = System.Text.Json.JsonSerializer.SerializeToElement(new { hs_color = new object[] { H, S } });

            var (ok, err) = await this._client.CallServiceAsync("light", "turn_on", entityId, data, cts.Token);
            if (ok)
            {
                lock (_sendGate)
                { _hsLastSent[entityId] = (H, S); }
                PluginLog.Info($"[color/send] hs_color=[{H:F0},{S:F0}] -> {entityId} OK");
            }
            else
            {
                PluginLog.Warning($"[color/send] failed: {err}");
                HealthBus.Error(err ?? "Color change failed");
            }
        }



        private void ScheduleSend(String entityId, Int32 delayMs)
        {
            lock (_sendGate)
            {
                if (!_sendTimers.TryGetValue(entityId, out var t))
                {
                    t = new System.Timers.Timer { AutoReset = false };
                    t.Elapsed += (s, e) => SafeFireSend(entityId);
                    _sendTimers[entityId] = t;
                }
                t.Interval = delayMs;
                t.Stop();
                t.Start();
            }
        }

        private void ScheduleTempSend(String entityId, Int32 delayMs)
        {
            lock (_sendGate)
            {
                if (!_sendTempTimers.TryGetValue(entityId, out var t))
                {
                    t = new System.Timers.Timer { AutoReset = false };
                    t.Elapsed += (s, e) => SafeFireTempSend(entityId);
                    _sendTempTimers[entityId] = t;
                }
                t.Interval = delayMs;
                t.Stop();
                t.Start();
            }
        }

        private void SafeFireTempSend(String entityId)
        {
            try
            { SendLatestTempAsync(entityId).GetAwaiter().GetResult(); }
            catch (Exception ex) { PluginLog.Warning(ex, $"[temp] send timer for {entityId}"); }
        }

        private async System.Threading.Tasks.Task SendLatestTempAsync(String entityId)
        {
            Int32 targetM;
            lock (_sendGate)
            {
                if (!_tempTargetMired.TryGetValue(entityId, out targetM))
                    return;
                if (_tempLastSentMired.TryGetValue(entityId, out var last) && last == targetM)
                    return;
            }

            if (!this._client.IsAuthenticated)
            {
                HealthBus.Error("Connection lost");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var targetK = MiredToKelvin(targetM);
            var data = JsonSerializer.SerializeToElement(new { color_temp_kelvin = targetK });

            var (ok, err) = await this._client.CallServiceAsync("light", "turn_on", entityId, data, cts.Token);
            if (ok)
            {
                lock (_sendGate)
                { _tempLastSentMired[entityId] = targetM; }
                PluginLog.Info($"[temp/send] {targetK}K ({targetM} mired) -> {entityId} OK");
            }
            else
            {
                PluginLog.Warning($"[temp/send] failed: {err}");
                HealthBus.Error(err ?? "Temp change failed");
            }
        }


        private void SafeFireSend(String entityId)
        {
            try
            { SendLatestBrightnessAsync(entityId).GetAwaiter().GetResult(); }
            catch (Exception ex) { PluginLog.Warning(ex, $"[wheel] send timer for {entityId}"); }
        }

        private async System.Threading.Tasks.Task SendLatestBrightnessAsync(String entityId)
        {
            Int32 target;
            lock (_sendGate)
            {
                if (!_briTarget.TryGetValue(entityId, out target))
                    return;
                if (_briLastSent.TryGetValue(entityId, out var last) && last == target)
                    return; // nothing to send
            }

            if (!this._client.IsAuthenticated)
            {
                HealthBus.Error("Connection lost");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var data = System.Text.Json.JsonSerializer.SerializeToElement(new { brightness = target });

            var (ok, err) = await this._client.CallServiceAsync("light", "turn_on", entityId, data, cts.Token);
            if (ok)
            {
                lock (_sendGate)
                { _briLastSent[entityId] = target; }
                PluginLog.Info($"[wheel/send] brightness={target} -> {entityId} OK");
                // Optional: if you want buttery ramping, you can also include transition: 0 in service_data
            }
            else
            {
                PluginLog.Warning($"[wheel/send] failed: {err}");
                HealthBus.Error(err ?? "Brightness change failed");
            }
        }



        private void SafeFireReconcile(String entityId)
        {
            try
            {
                var val = GetBrightnessFromHa(entityId);
                if (val.HasValue)
                {
                    SetCachedBrightness(entityId, val.Value);
                    this.AdjustmentValueChanged(AdjWheel); // redraw value & image
                    PluginLog.Info($"[wheel/reconcile] HA={val.Value} for {entityId}");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[wheel] reconcile timer for {entityId}");
            }
        }

        private void OnHaBrightnessChanged(string entityId, int? bri)
        {
            // We only care about lights we know
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                return;

            // Update cache
            if (bri.HasValue)
                SetCachedBrightness(entityId, HSBHelper.Clamp(bri.Value, 0, 255));

            // If this is the active device, redraw the wheel value & image
            if (_inDeviceView && string.Equals(_currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
            {
                this.AdjustmentValueChanged(AdjWheel);
            }
        }

        private void OnHaColorTempChanged(string entityId, int? mired, int? kelvin, int? minM, int? maxM)
        {
            if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                return;

            // Figure out current mired
            Int32 cur = _tempMiredByEntity.TryGetValue(entityId, out var t) ? t.Cur : DefaultWarmMired;
            if (mired.HasValue)
                cur = mired.Value;
            else if (kelvin.HasValue)
                cur = KelvinToMired(kelvin.Value);

            // Update cache (carry forward bounds unless provided)
            SetCachedTempMired(entityId, minM, maxM, cur);

            // If current device, refresh dial value/image
            if (_inDeviceView && String.Equals(_currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                this.AdjustmentValueChanged(AdjTemp);
        }

        private void OnHaHsColorChanged(string entityId, double? h, double? s)
{
    if (!entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
        return;

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





        private void CancelEntityTimers(String entityId)
        {
            if (String.IsNullOrEmpty(entityId))
                return;
            lock (_sendGate)
            {
                if (_sendTimers.TryGetValue(entityId, out var t))
                { try { t.Stop(); } catch { } }
                if (_reconcileTimers != null && _reconcileTimers.TryGetValue(entityId, out var r))
                { try { r.Stop(); } catch { } }
                if (_sendHueTimers.TryGetValue(entityId, out var tH))
                { try { tH.Stop(); } catch { } }

                if (_sendTempTimers.TryGetValue(entityId, out var t2))
                { try { t2.Stop(); } catch { } }
                if (_reconcileTempTimers != null && _reconcileTempTimers.TryGetValue(entityId, out var r2))
                { try { r2.Stop(); } catch { } }
            }
        }



        private Boolean TryGetCachedTempMired(String entityId, out (Int32 Min, Int32 Max, Int32 Cur) t)
            => _tempMiredByEntity.TryGetValue(entityId, out t);

        private void SetCachedTempMired(String entityId, Int32? minM, Int32? maxM, Int32 curMired)
        {
            var min = minM ?? (_tempMiredByEntity.TryGetValue(entityId, out var old) ? old.Min : DefaultMinMireds);
            var max = maxM ?? (_tempMiredByEntity.TryGetValue(entityId, out var old2) ? old2.Max : DefaultMaxMireds);
            var cur = Clamp(curMired, min, max);
            _tempMiredByEntity[entityId] = (min, max, cur);
        }

        private void ClearEntityTargets(String entityId)
{
    if (String.IsNullOrEmpty(entityId)) return;
    lock (_sendGate)
    {
        // Brightness
        _briTarget?.Remove(entityId);
        _briLastSent?.Remove(entityId);

        // Temperature
        _tempTargetMired?.Remove(entityId);
        _tempLastSentMired?.Remove(entityId);

        // Hue/Saturation
        _hTargetDeg?.Remove(entityId);
        _sTargetPct?.Remove(entityId);
        _hsLastSent?.Remove(entityId);
    }
}








    }
}
