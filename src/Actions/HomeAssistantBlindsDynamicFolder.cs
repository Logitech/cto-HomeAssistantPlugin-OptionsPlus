namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;

    using Loupedeck;

    public class HomeAssistantBlindsDynamicFolder : PluginDynamicFolder
    {
        private record CoverItem(
            String EntityId,
            String FriendlyName,
            String State,
            String DeviceId,
            String DeviceName,
            String Manufacturer,
            String Model,
            String DeviceClass
        );

        private readonly IconService _icons;

        // Navigation constants
        private const String CmdStatus = "status";
        private const String CmdRetry = "retry";
        private const String CmdBack = "back";
        private const String CmdArea = "area";

        private readonly HaWebSocketClient _client = new();
        private CancellationTokenSource _cts;

        private readonly Dictionary<String, CoverItem> _coversByEntity = new();
        private readonly Dictionary<String, (Int32 Position, Int32 Tilt)> _coverStateByEntity = new();

        // State cache per entity 
        private readonly Dictionary<String, String> _stateByEntity = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<String, CoverCaps> _capsByEntity = new(StringComparer.OrdinalIgnoreCase);
        private readonly CapabilityService _capSvc = new();

        private CoverCaps GetCaps(String eid) =>
            this._capsByEntity.TryGetValue(eid, out var c) ? c : new CoverCaps(true, false, false, "");

        // Navigation levels
        private enum ViewLevel { Root, Area, Device }
        private ViewLevel _level = ViewLevel.Root;
        private Boolean _inDeviceView = false;

        private String _currentAreaId = null;
        private String _currentEntityId = null;

        // Area data
        private readonly Dictionary<String, String> _areaIdToName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, String> _entityToAreaId = new(StringComparer.OrdinalIgnoreCase);

        // Synthetic "no area" bucket
        private const String UnassignedAreaId = "!unassigned";
        private const String UnassignedAreaName = "(No area)";

        // Action parameter prefixes
        private const String PfxDevice = "device:";
        private const String PfxActOpen = "act:open:";
        private const String PfxActClose = "act:close:";
        private const String PfxActStop = "act:stop:";

        // Wheel controls
        private const String AdjPosition = "adj:position";
        private const String AdjTilt = "adj:tilt";
        private Int32 _wheelCounter = 0;
        private const Int32 WheelStepPercent = 1;

        // Debounce settings
        private const Int32 SendDebounceMs = 10;
        private const Int32 ReconcileIdleMs = 500;
        private const Int32 MaxPctPerEvent = 10;

        private readonly HaEventListener _events = new();
        private CancellationTokenSource _eventsCts;

        private readonly CoverControlService _coverSvc;
        private readonly IHaClient _ha;

        // Echo suppression: ignore HA frames shortly after we sent a command
        private readonly Dictionary<String, DateTime> _lastCmdAt = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan EchoSuppressWindow = TimeSpan.FromSeconds(3);

        public HomeAssistantBlindsDynamicFolder()
        {
            this.DisplayName = "All Blind Controls";
            this.GroupName = "Covers";

            this._icons = new IconService(new Dictionary<String, String>
            {
                // Use existing icons that make sense for covers
                { IconId.Cover,       "light_bulb_icon.svg" },      // Generic cover icon
                { IconId.Blind,       "light_bulb_icon.svg" },      // Blind specific
                { IconId.Curtain,     "light_bulb_icon.svg" },      // Curtain specific
                { IconId.Shade,       "light_bulb_icon.svg" },      // Shade specific
                { IconId.Shutter,     "light_bulb_icon.svg" },      // Shutter specific
                { IconId.Awning,      "light_bulb_icon.svg" },      // Awning specific
                { IconId.Garage,      "light_bulb_icon.svg" },      // Garage door
                { IconId.Gate,        "light_bulb_icon.svg" },      // Gate
                { IconId.Door,        "light_bulb_icon.svg" },      // Door
                { IconId.Window,      "light_bulb_icon.svg" },      // Window
                { IconId.Back,        "back_icon.svg" },
                { IconId.CoverOpen,   "light_on_icon.svg" },        // Open state
                { IconId.CoverClosed, "light_off_icon.svg" },       // Closed state
                { IconId.Position,    "brightness_icon.svg" },      // Position control
                { IconId.Tilt,        "saturation_icon.svg" },      // Tilt control
                { IconId.Stop,        "issue_status_icon.svg" },    // Stop button
                { IconId.Retry,       "reload_icon.svg" },
                { IconId.Issue,       "issue_status_icon.svg" },
                { IconId.Online,      "online_status_icon.png" },
                { IconId.Area,        "area_icon.svg" },
            });

            // Wrap the raw client
            this._ha = new HaClientAdapter(this._client);

            const Int32 PositionDebounceMs = SendDebounceMs;
            const Int32 TiltDebounceMs = SendDebounceMs;

            this._coverSvc = new CoverControlService(this._ha, PositionDebounceMs, TiltDebounceMs);
        }

        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) =>
            PluginDynamicFolderNavigation.None;

        // --- WHEEL: Display names for adjustment controls ---
        public override String GetAdjustmentDisplayName(String actionParameter, PluginImageSize _)
        {
            return actionParameter == AdjPosition
                ? this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId) ? "Position" : "Test Wheel"
                : actionParameter == AdjTilt
                ? "Tilt"
                : base.GetAdjustmentDisplayName(actionParameter, _);
        }

        // --- WHEEL: Small value shown next to the dial ---
        public override String GetAdjustmentValue(String actionParameter)
        {
            if (actionParameter == AdjPosition)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var pos = this.GetEffectivePosition(this._currentEntityId);
                    return $"{pos}%";
                }
                return this._wheelCounter.ToString();
            }

            if (actionParameter == AdjTilt)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    var tilt = this.GetEffectiveTilt(this._currentEntityId);
                    return $"{tilt}%";
                }
                return "—%";
            }

            return base.GetAdjustmentValue(actionParameter);
        }

        public override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == AdjPosition)
            {
                var pos = 50;
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    pos = this.GetEffectivePosition(this._currentEntityId);
                }

                Int32 r, g, b;
                if (pos <= 0)
                { r = g = b = 40; }
                else
                { r = Math.Min(30 + pos * 2, 255); g = Math.Min(60 + pos, 220); b = 80; }

                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Position), "◐", padPct: 10, font: 58);
            }

            if (actionParameter == AdjTilt)
            {
                var tilt = this._inDeviceView ? this.GetEffectiveTilt(this._currentEntityId) : 50;
                
                Int32 r = Math.Min(80 + tilt, 200);
                Int32 g = Math.Min(100 + tilt, 180);
                Int32 b = 120;
                
                using var bb = new BitmapBuilder(imageSize);
                bb.Clear(new BitmapColor(r, g, b));
                return TilePainter.IconOrGlyph(bb, this._icons.Get(IconId.Tilt), "⟷", padPct: 8, font: 56);
            }

            return null;
        }

        // --- WHEEL: Rotation handler ---
        public override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (actionParameter == AdjPosition && diff != 0)
            {
                try
                {
                    if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                    {
                        var entityId = this._currentEntityId;
                        var curPos = this.GetEffectivePosition(entityId);

                        var stepPct = diff * WheelStepPercent;
                        stepPct = Math.Sign(stepPct) * Math.Min(Math.Abs(stepPct), MaxPctPerEvent);
                        var targetPos = HSBHelper.Clamp(curPos + stepPct, 0, 100);

                        // Optimistic UI
                        this.SetCachedPosition(entityId, targetPos);
                        this.AdjustmentValueChanged(actionParameter);

                        this.MarkCommandSent(entityId);
                        this._coverSvc.SetPosition(entityId, targetPos);
                    }
                    else
                    {
                        this._wheelCounter += diff;
                        this.AdjustmentValueChanged(actionParameter);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[wheel] ApplyAdjustment position exception");
                    HealthBus.Error("Position wheel error");
                    this.AdjustmentValueChanged(actionParameter);
                }
            }

            if (actionParameter == AdjTilt && diff != 0)
            {
                if (this._inDeviceView && !String.IsNullOrEmpty(this._currentEntityId))
                {
                    if (!this.GetCaps(this._currentEntityId).Tilt)
                    {
                        return;
                    }

                    var entityId = this._currentEntityId;
                    var curTilt = this.GetEffectiveTilt(entityId);

                    var stepPct = diff * WheelStepPercent;
                    stepPct = Math.Sign(stepPct) * Math.Min(Math.Abs(stepPct), MaxPctPerEvent);
                    var targetTilt = HSBHelper.Clamp(curTilt + stepPct, 0, 100);

                    // Optimistic UI
                    this.SetCachedTilt(entityId, targetTilt);
                    this.AdjustmentValueChanged(actionParameter);

                    this.MarkCommandSent(entityId);
                    this._coverSvc.SetTilt(entityId, targetTilt);
                }
            }
        }

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _)
        {
            // Always show Back + Status
            yield return this.CreateCommandName(CmdBack);
            yield return this.CreateCommandName(CmdStatus);

            if (this._level == ViewLevel.Device && !String.IsNullOrEmpty(this._currentEntityId))
            {
                var caps = this.GetCaps(this._currentEntityId);

                // Show appropriate controls based on device capabilities
                if (caps.Basic)
                {
                    // Basic controls (open/close/stop)
                    yield return this.CreateCommandName($"{PfxActOpen}{this._currentEntityId}");
                    yield return this.CreateCommandName($"{PfxActClose}{this._currentEntityId}");
                    yield return this.CreateCommandName($"{PfxActStop}{this._currentEntityId}");
                }
                
                // Show tilt controls if supported (can be in addition to basic controls)
                if (caps.Tilt && !caps.Basic)
                {
                    // Tilt-only controls (for devices that only support tilt)
                    yield return this.CreateCommandName($"act:open_tilt:{this._currentEntityId}");
                    yield return this.CreateCommandName($"act:close_tilt:{this._currentEntityId}");
                    yield return this.CreateCommandName($"act:stop_tilt:{this._currentEntityId}");
                }
                else if (caps.Tilt && caps.Basic)
                {
                    // Additional tilt controls for devices that support both basic and tilt
                    yield return this.CreateCommandName($"act:open_tilt:{this._currentEntityId}");
                    yield return this.CreateCommandName($"act:close_tilt:{this._currentEntityId}");
                    yield return this.CreateCommandName($"act:stop_tilt:{this._currentEntityId}");
                }

                // Position control
                if (caps.Position)
                {
                    yield return this.CreateAdjustmentName(AdjPosition);
                }

                // Tilt control
                if (caps.Tilt)
                {
                    yield return this.CreateAdjustmentName(AdjTilt);
                }

                yield break;
            }

            if (this._level == ViewLevel.Area && !String.IsNullOrEmpty(this._currentAreaId))
            {
                // Covers for current area
                foreach (var kv in this._coversByEntity)
                {
                    if (this._entityToAreaId.TryGetValue(kv.Key, out var aid) && 
                        String.Equals(aid, this._currentAreaId, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return this.CreateCommandName($"{PfxDevice}{kv.Key}");
                    }
                }
                yield break;
            }

            // ROOT: list areas that actually have covers
            var areaIds = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (var eid in this._coversByEntity.Keys)
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
            if (actionParameter == CmdBack) return "Back";
            if (actionParameter == CmdStatus) return String.Empty;
            if (actionParameter == CmdRetry) return "Retry";

            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxDevice.Length);
                return this._coversByEntity.TryGetValue(entityId, out var ci) ? ci.FriendlyName : entityId;
            }

            if (actionParameter.StartsWith(PfxActOpen, StringComparison.OrdinalIgnoreCase)) return "Open";
            if (actionParameter.StartsWith(PfxActClose, StringComparison.OrdinalIgnoreCase)) return "Close";
            if (actionParameter.StartsWith(PfxActStop, StringComparison.OrdinalIgnoreCase)) return "Stop";
            
            // Tilt-specific actions
            if (actionParameter.StartsWith("act:open_tilt:", StringComparison.OrdinalIgnoreCase)) return "Open Tilt";
            if (actionParameter.StartsWith("act:close_tilt:", StringComparison.OrdinalIgnoreCase)) return "Close Tilt";
            if (actionParameter.StartsWith("act:stop_tilt:", StringComparison.OrdinalIgnoreCase)) return "Stop Tilt";

            if (actionParameter.StartsWith(CmdArea, StringComparison.OrdinalIgnoreCase))
            {
                var areaId = actionParameter.Substring(CmdArea.Length);
                return this._areaIdToName.TryGetValue(areaId, out var name) ? name : areaId;
            }

            return null;
        }

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == CmdBack) return this._icons.Get(IconId.Back);
            if (actionParameter == CmdRetry) return this._icons.Get(IconId.Retry);

            if (actionParameter == CmdStatus)
            {
                var ok = HealthBus.State == HealthState.Ok;
                using (var bb = new BitmapBuilder(imageSize))
                {
                    var okImg = this._icons.Get(IconId.Online);
                    var issueImg = this._icons.Get(IconId.Issue);
                    TilePainter.Background(bb, ok ? okImg : issueImg, 
                        ok ? new BitmapColor(0, 160, 60) : new BitmapColor(200, 30, 30));
                    bb.DrawText(ok ? "ONLINE" : "ISSUE", fontSize: 22, color: new BitmapColor(255, 255, 255));
                    return bb.ToImage();
                }
            }

            if (actionParameter.StartsWith(PfxDevice, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxDevice.Length);
                if (this._coversByEntity.TryGetValue(entityId, out var ci))
                {
                    var caps = this.GetCaps(entityId);
                    var iconId = caps.GetIconId();
                    return this._icons.Get(iconId) ?? this._icons.Get(IconId.Cover);
                }
                return this._icons.Get(IconId.Cover);
            }

            if (actionParameter.StartsWith(CmdArea, StringComparison.OrdinalIgnoreCase))
                return this._icons.Get(IconId.Area);

            if (actionParameter.StartsWith(PfxActOpen, StringComparison.OrdinalIgnoreCase))
                return this._icons.Get(IconId.CoverOpen);
            if (actionParameter.StartsWith(PfxActClose, StringComparison.OrdinalIgnoreCase))
                return this._icons.Get(IconId.CoverClosed);
            if (actionParameter.StartsWith(PfxActStop, StringComparison.OrdinalIgnoreCase))
                return this._icons.Get(IconId.Stop);
                
            // Tilt-specific action icons
            if (actionParameter.StartsWith("act:open_tilt:", StringComparison.OrdinalIgnoreCase))
                return this._icons.Get(IconId.CoverOpen);
            if (actionParameter.StartsWith("act:close_tilt:", StringComparison.OrdinalIgnoreCase))
                return this._icons.Get(IconId.CoverClosed);
            if (actionParameter.StartsWith("act:stop_tilt:", StringComparison.OrdinalIgnoreCase))
                return this._icons.Get(IconId.Stop);

            return null;
        }

        public override void RunCommand(String actionParameter)
        {
            PluginLog.Info($"RunCommand: {actionParameter}");

            if (actionParameter == CmdBack)
            {
                if (this._level == ViewLevel.Device)
                {
                    this._coverSvc.CancelPending(this._currentEntityId);
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
                if (this._areaIdToName.ContainsKey(areaId) || 
                    String.Equals(areaId, UnassignedAreaId, StringComparison.OrdinalIgnoreCase))
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
                var entityId = actionParameter.Substring(PfxDevice.Length);
                if (this._coversByEntity.ContainsKey(entityId))
                {
                    this._inDeviceView = true;
                    this._level = ViewLevel.Device;
                    this._currentEntityId = entityId;
                    this._wheelCounter = 0;

                    // Initialize state cache if needed
                    if (!this._coverStateByEntity.ContainsKey(entityId))
                    {
                        this._coverStateByEntity[entityId] = (50, 50); // Default middle position
                    }

                    this.ButtonActionNamesChanged();
                    this.AdjustmentValueChanged(AdjPosition);
                    this.AdjustmentValueChanged(AdjTilt);

                    PluginLog.Info($"ENTER device view: {entityId}");
                }
                return;
            }

            // Cover actions - basic controls
            if (actionParameter.StartsWith(PfxActOpen, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActOpen.Length);
                this.MarkCommandSent(entityId);
                _ = this._coverSvc.OpenAsync(entityId);
                return;
            }

            if (actionParameter.StartsWith(PfxActClose, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActClose.Length);
                this.MarkCommandSent(entityId);
                _ = this._coverSvc.CloseAsync(entityId);
                return;
            }

            if (actionParameter.StartsWith(PfxActStop, StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring(PfxActStop.Length);
                this.MarkCommandSent(entityId);
                _ = this._coverSvc.StopAsync(entityId);
                return;
            }
            
            // Cover actions - tilt controls
            if (actionParameter.StartsWith("act:open_tilt:", StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring("act:open_tilt:".Length);
                this.MarkCommandSent(entityId);
                _ = this._coverSvc.OpenTiltAsync(entityId);
                return;
            }

            if (actionParameter.StartsWith("act:close_tilt:", StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring("act:close_tilt:".Length);
                this.MarkCommandSent(entityId);
                _ = this._coverSvc.CloseTiltAsync(entityId);
                return;
            }

            if (actionParameter.StartsWith("act:stop_tilt:", StringComparison.OrdinalIgnoreCase))
            {
                var entityId = actionParameter.Substring("act:stop_tilt:".Length);
                this.MarkCommandSent(entityId);
                _ = this._coverSvc.StopTiltAsync(entityId);
                return;
            }
        }

        // Helper methods
        private Int32 GetEffectivePosition(String entityId)
        {
            return this._coverStateByEntity.TryGetValue(entityId, out var state) ? state.Position : 50;
        }

        private Int32 GetEffectiveTilt(String entityId)
        {
            return this._coverStateByEntity.TryGetValue(entityId, out var state) ? state.Tilt : 50;
        }

        private void SetCachedPosition(String entityId, Int32 position)
        {
            var current = this._coverStateByEntity.TryGetValue(entityId, out var state) ? state : (Position: 50, Tilt: 50);
            this._coverStateByEntity[entityId] = (HSBHelper.Clamp(position, 0, 100), current.Tilt);
        }

        private void SetCachedTilt(String entityId, Int32 tilt)
        {
            var current = this._coverStateByEntity.TryGetValue(entityId, out var state) ? state : (Position: 50, Tilt: 50);
            this._coverStateByEntity[entityId] = (current.Position, HSBHelper.Clamp(tilt, 0, 100));
        }

        private void MarkCommandSent(String entityId) => this._lastCmdAt[entityId] = DateTime.UtcNow;

        private Boolean ShouldIgnoreFrame(String entityId, String reasonForLog = null)
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
                this._lastCmdAt.Remove(entityId);
            }
            return false;
        }

        // Lifecycle methods (simplified - full implementation would follow lights pattern)
        public override Boolean Load()
        {
            PluginLog.Info("BlindsDynamicFolder.Load()");
            HealthBus.HealthChanged += this.OnHealthChanged;
            return true;
        }

        public override Boolean Unload()
        {
            PluginLog.Info("BlindsDynamicFolder.Unload()");
            this._coverSvc?.Dispose();
            this._eventsCts?.Cancel();
            this._events.SafeCloseAsync();
            HealthBus.HealthChanged -= this.OnHealthChanged;
            return true;
        }

        public override Boolean Activate()
        {
            PluginLog.Info("BlindsDynamicFolder.Activate() -> authenticate");
            var ret = this.AuthenticateSync();
            this.EncoderActionNamesChanged();
            return ret;
        }

        public override Boolean Deactivate()
        {
            PluginLog.Info("BlindsDynamicFolder.Deactivate() -> close WS");
            this._cts?.Cancel();
            this._client.SafeCloseAsync().GetAwaiter().GetResult();
            this._eventsCts?.Cancel();
            _ = this._events.SafeCloseAsync();
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

        // Simplified authentication - in full implementation, would follow lights pattern exactly
        private Boolean AuthenticateSync()
        {
            this._cts?.Cancel();
            this._cts = new CancellationTokenSource();

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var baseUrl) ||
                String.IsNullOrWhiteSpace(baseUrl))
            {
                PluginLog.Warning("Missing ha.baseUrl setting");
                HealthBus.Error("Missing Base URL");
                return false;
            }

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var token) ||
                String.IsNullOrWhiteSpace(token))
            {
                PluginLog.Warning("Missing ha.token setting");
                HealthBus.Error("Missing Token");
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
                    this.SetupEventListeners(baseUrl, token);
                    this.FetchCoversAndServices();
                    this._level = ViewLevel.Root;
                    this._currentAreaId = null;
                    this._currentEntityId = null;
                    this._inDeviceView = false;
                    this.ButtonActionNamesChanged();
                    this.EncoderActionNamesChanged();
                    return true;
                }

                HealthBus.Error(msg ?? "Auth failed");
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "AuthenticateSync failed");
                HealthBus.Error("Auth error");
                return false;
            }
        }

        private void SetupEventListeners(String baseUrl, String token)
        {
            try
            {
                this._eventsCts?.Cancel();
                this._eventsCts = new CancellationTokenSource();
                
                this._events.CoverPositionChanged -= this.OnCoverPositionChanged;
                this._events.CoverPositionChanged += this.OnCoverPositionChanged;
                
                this._events.CoverTiltChanged -= this.OnCoverTiltChanged;
                this._events.CoverTiltChanged += this.OnCoverTiltChanged;
                
                this._events.CoverStateChanged -= this.OnCoverStateChanged;
                this._events.CoverStateChanged += this.OnCoverStateChanged;

                _ = this._events.ConnectAndSubscribeAsync(baseUrl, token, this._eventsCts.Token);
                PluginLog.Info("[events] subscribed to cover state_changed");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[events] subscribe failed");
            }
        }

        private void OnCoverPositionChanged(String entityId, Int32? position)
        {
            if (!entityId.StartsWith("cover.", StringComparison.OrdinalIgnoreCase)) return;
            if (this.ShouldIgnoreFrame(entityId, "position")) return;

            if (position.HasValue)
            {
                this.SetCachedPosition(entityId, position.Value);
                
                if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    this.AdjustmentValueChanged(AdjPosition);
                }
            }
        }

        private void OnCoverTiltChanged(String entityId, Int32? tilt)
        {
            if (!entityId.StartsWith("cover.", StringComparison.OrdinalIgnoreCase)) return;
            if (this.ShouldIgnoreFrame(entityId, "tilt")) return;

            if (tilt.HasValue)
            {
                this.SetCachedTilt(entityId, tilt.Value);
                
                if (this._inDeviceView && String.Equals(this._currentEntityId, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    this.AdjustmentValueChanged(AdjTilt);
                }
            }
        }

        private void OnCoverStateChanged(String entityId, String state)
        {
            if (!entityId.StartsWith("cover.", StringComparison.OrdinalIgnoreCase)) return;
            
            this._stateByEntity[entityId] = state ?? "";
            
            // Repaint device tile if visible
            this.CommandImageChanged(this.CreateCommandName($"{PfxDevice}{entityId}"));
        }

        // Simplified fetch - in full implementation would follow lights pattern exactly
        private Boolean FetchCoversAndServices()
        {
            try
            {
                var (okStates, statesJson, errStates) = this._client.RequestAsync("get_states", this._cts.Token)
                                                               .GetAwaiter().GetResult();
                if (!okStates)
                {
                    PluginLog.Warning($"get_states failed: {errStates}");
                    return false;
                }

                // Parse and populate covers (simplified)
                using var statesDoc = JsonDocument.Parse(statesJson);
                this._coversByEntity.Clear();
                this._coverStateByEntity.Clear();

                foreach (var st in statesDoc.RootElement.EnumerateArray())
                {
                    var entityId = st.GetPropertyOrDefault("entity_id");
                    if (String.IsNullOrEmpty(entityId) || 
                        !entityId.StartsWith("cover.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var state = st.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var attrs = st.TryGetProperty("attributes", out var a) ? a : default;
                    
                    var friendly = (attrs.ValueKind == JsonValueKind.Object && 
                                   attrs.TryGetProperty("friendly_name", out var fn))
                                   ? fn.GetString() ?? entityId : entityId;

                    var caps = this._capSvc.ForCover(attrs);
                    this._capsByEntity[entityId] = caps;

                    // Extract position and tilt
                    var position = 50; // Default
                    var tilt = 50;     // Default

                    if (attrs.ValueKind == JsonValueKind.Object)
                    {
                        if (attrs.TryGetProperty("current_position", out var pos) && 
                            pos.ValueKind == JsonValueKind.Number)
                        {
                            position = HSBHelper.Clamp(pos.GetInt32(), 0, 100);
                        }

                        if (attrs.TryGetProperty("current_tilt_position", out var tiltPos) && 
                            tiltPos.ValueKind == JsonValueKind.Number)
                        {
                            tilt = HSBHelper.Clamp(tiltPos.GetInt32(), 0, 100);
                        }
                    }

                    this._coverStateByEntity[entityId] = (position, tilt);
                    this._stateByEntity[entityId] = state;

                    var coverItem = new CoverItem(entityId, friendly, state, "", "", "", "", caps.DeviceClass);
                    this._coversByEntity[entityId] = coverItem;

                    // For demo, put all covers in unassigned area
                    this._entityToAreaId[entityId] = UnassignedAreaId;

                    PluginLog.Info($"[Cover] {entityId} | name='{friendly}' | state={state} | pos={position}% | tilt={tilt}% | caps={caps}");
                }

                // Ensure unassigned area exists
                if (this._entityToAreaId.ContainsValue(UnassignedAreaId))
                {
                    this._areaIdToName[UnassignedAreaId] = UnassignedAreaName;
                }

                HealthBus.Ok("Fetched covers");
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "FetchCoversAndServices failed");
                HealthBus.Error("Fetch failed");
                return false;
            }
        }
    }
}