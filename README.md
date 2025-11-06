# Loupedeck × Home Assistant Plugin

Control your Home Assistant lights (and soon, any entity) from your Creative Console with a fast, optimistic UI, debounced actions, and capability-aware controls. Open the **All Light Controls** dynamic folder(only on creative console) to browse **Areas → Lights → Commands** and use the dial for brightness, color temperature, hue, and saturation.

> **Status**: Beta. For now there is only support for lights. OSS-ready and actively monitoring and fixing issues. The plugin was only tested on the Creative Console with Phillips Wiz lights.

## Need help?
Check the [Troubleshooting guide](./TROUBLESHOOTING.md) or open a [New bug report](../../issues/new?template=bug_report.yml).


---

## Table of Contents

- [Features](#features)
- [Quick Start](#quick-start)
- [Actions & How to Use Them](#actions--how-to-use-them)
  - [Home Assistant Permissions](#home-assistant-permissions)
- [Requirements](#requirements)
- [Install](#install)
  - [Marketplace (soon)](#marketplace-soon)
  - [Manual (from source)](#manual-from-source)
- [Architecture](#architecture)
- [Development](#development)
- [Testing](#testing-todo)
- [Packaging & Release](#packaging--release)
- [Troubleshooting](#troubleshooting)
- [Security & Privacy](#security--privacy)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)
- [Credits](#credits)
- [Licensing & Icon Attributions](#licensing--icon-attributions)
- [Appendix: Power-user Notes](#appendix-power-user-notes)


---

## Quick Start
1. Find the plugin in the Logi Marketplace in options+ and install the plugin

2. Create a **Long-Lived Token** and copy it (Home Assistant ->Your Profile -> Security -> Long-lived access tokens)


3. Drop the **Configure Home Assistant** action (located inside the HOME ASSISTANT folder) in a tile :

   * **Base Websocket URL**: `wss://homeassistant.local:8123/` if Home Assistant was setup using the default way (or your own custom URL starting with `wss://` or `ws://` otherwise). Prefer `wss://` for enhanced security.
   * **Long-Lived Token**: paste from HA Profile
   * Click **Test connection**.
   * If no error appears after click save.
4. Put any **actions** you want into your layout. See what they do in the explanation helow
5. (Optional) You may now delete the Configure Home Assistant action from your layout . The settings persist.

---

## Actions & How to Use Them

### HA: Run Script

Trigger or stop any `script.*` in Home Assistant.

* **Press once** → runs the selected script (optionally with variables).
* **Press again while running** → stops the script.

**To configure:**

1. Place **HA: Run Script** on your layout.
2. In the popup:

   * **Script**: pick from your `script.*` entities (the list auto-loads from HA).
   * **Variables (JSON)**: optional; e.g. `{"minutes":5,"who":"guest"}`.
   * **Prefer `script.toggle`**: leave **off** unless you know you need toggle semantics (toggle ignores variables).

---

### Toggle Light

Toggle a single `light.*` on/off via Home Assistant.

* **Press** → sends `light.toggle` for the selected light.

**To configure:**

1. Place **Toggle Light** on your layout.
2. In the popup:

   * **Light**: pick from your `light.*` entities (the list auto-loads from HA state).
   * If HA isn’t configured/connected yet, you’ll see a hint to open plugin settings.

**Notes**

* Works with any HA light entity; no variables are used (it’s a pure toggle).
* Requires HA **Base URL** and **Long-Lived Token** to be set (see *Configure Home Assistant*).

---

### Advanced Toggle Lights

Advanced multi-light control with comprehensive settings and capability-aware parameter management.

* **Press** → intelligently toggles selected lights with smart parameter handling:
  - **Lights ON with parameters** → turn OFF
  - **Lights OFF** → turn ON with your configured parameters (brightness, color temperature, hue, saturation, white levels)

**To configure:**

1. Place **Advanced Toggle Lights** on your layout.
2. In the popup:
   
   * **Select Lights**: Choose one light from the dropdown, or add additional lights by entering entity IDs in the field below (e.g., `light.living_room,light.kitchen`)
   * **Brightness**: Set desired brightness level (0-255)
   * **Color Temperature**: Set color temperature if supported by lights
   * **Hue & Saturation**: Set color values if supported by lights
   * **White Levels**: Configure white channel levels for RGBW lights (requires hue and saturation to also be set)

**Key Features:**

* **Smart Toggle Behavior**: Automatically determines whether to turn lights on or off based on current state
* **Debounced Service Calls**: Smooth operation with optimized Home Assistant API calls
* **Full Parameter Control**: Complete control over all supported light parameters

**Best for**: Controlling multiple lights of similar capabilities where you want consistent behavior across all selected lights.

---

### Area Toggle Lights

Area-based light control that automatically discovers and controls all lights in a selected area with individual capability optimization.

* **Press** → toggles all lights in the selected area with intelligent per-light parameter handling:
  - Each light gets the **maximum parameters it individually supports**
  - **Mixed capability areas** work seamlessly (RGB + brightness-only lights together)

**To configure:**

1. Place **Area Toggle Lights** on your layout.
2. In the popup:
   
   * **Select Area**: Choose from dropdown showing "Area Name (X lights)" format
   * **Brightness**: Set desired brightness level (0-255)
   * **Color Temperature**: Set color temperature for capable lights
   * **Hue & Saturation**: Set color values for capable lights
   * **White Levels**: Configure white channel levels for RGBW-capable lights (requires hue and saturation to also be set)

**Key Features:**

* **Individual Capability Filtering**: Each light gets maximum possible parameters it supports (not limited by other lights in area)
* **Automatic Light Discovery**: Finds all lights in selected area automatically
* **Mixed Capability Support**: Perfect for areas with different light types
* **Area-Centric Control**: No need to select individual lights - just pick the area

**Best for**: Controlling entire areas/rooms where lights have different capabilities, maximizing each light's potential.

---

### All Light Controls (Areas → Lights → Commands)

Browse all lights and control them with capability-aware dials. Only available for the Creative Console or equivalents.

1. Add **All Light Controls** to your layout and press it to enter.

2. You’ll see:

   * **Back** — navigates up one level (Device → Area → Root → closes).
   * **Status** — shows ONLINE/ISSUE; press when ISSUE to surface the error in Options+.
   * **Retry** — reconnects and reloads data from HA.
   * **Areas** — your HA Areas (plus **(No area)** if some lights aren’t assigned).

3. **Pick an Area** → shows all **Lights** in that area.

4. **Pick a Light** → shows **Commands & Dials** for that device:

   * **On / Off** buttons

     * On uses the last cached brightness, hue, sat when available.
   * **Brightness** (use dial): 0–255, optimistic UI with debounced sending.
   * **Color Temp** (use dial): warm ↔ cool, shown only if supported.
   * **Hue** (use dial): 0–360°, shown only if supported.
   * **Saturation** (use dial): 0–100%, shown only if supported.

**Notes**

* Controls are **capability-aware**—you only see what the light supports.
* UI is **optimistic** and sends updates **debounced** to keep HA traffic low.
* **Back** steps: Device → Area → Root. From Root, Back closes the folder.

---



### Home Assistant Permissions

The Long-Lived Token must allow:

* Reading state (`get_states`)
* Calling services (e.g., `light.turn_on`)
* Registry access (`config/*_registry/list`) to map devices and areas

> If your HA user has standard admin privileges, you’re all set.
---

## Features

* **Setup (Action)**
  A dedicated action to setup the connection with the **Home Assistant hub`**.

* **Run Home Assistant Scripts (Action)**
  A dedicated action to trigger any **Home Assistant `script.*`** (with optional variables) and stop/toggle when running.

* **Toggle Single Light (Action)**
  Simple toggle action for individual light entities with basic on/off control.

* **Advanced Multi-Light Control (Action)**
  Sophisticated action for controlling multiple selected lights with capability intersection approach - uses parameters that ALL selected lights support for consistent behavior.

* **Area-Based Light Control (Action)**
  Intelligent area control that automatically discovers all lights in a selected area and maximizes each light's individual capabilities with smart parameter filtering.

* **Control All Lights (Action) with Area-First Navigation**
  One action to browse **Areas → Lights → Commands**: pick an area, select a light, then use per-device controls.

* **Capability-Aware Controls**
  Only shows controls a device actually supports (on/off, brightness, color temperature, hue, saturation).

* **Smart Toggle Behavior**
  Intelligent toggle logic that turns lights OFF when they're ON with parameters, or turns them ON with configured parameters when OFF.

* **Dual Capability Approaches**
  - **Intersection Approach** (Advanced Toggle): Parameters limited to what ALL lights support
  - **Individual Filtering** (Area Toggle): Each light gets maximum parameters it individually supports

* **Optimistic UI + Debounced Sends**
  Dials update instantly while changes are coalesced to reduce Home Assistant traffic and avoid jitter.

* **Resilient WebSocket Integration**
  Authenticated request channel plus an event listener to keep state fresh.


---


## Requirements

* **Loupedeck** software + a compatible Loupedeck device (Creative Console, Live/Live S/CT, etc.) Some simple actions can be used with the actions ring.
* **Home Assistant** with WebSocket API enabled (standard).
* **Home Assistant Long-Lived Access Token** (Profile → Security).
* **.NET SDK** 8.0 (recommended) to build from source.
* Windows 10/11 (for building and running the Loupedeck plugin locally).

---

## Install

### Marketplace

* Search for **“Home Assistant”** in the Loupedeck Marketplace and install.


---

### Manual (from source)

1. **Build the plugin (Release)**

```bash
dotnet build -c Release
```

2. **Package to a `.lplug4`** using the Logi Plugin Tool

```bash
logiplugintool pack ./bin/Release/ ./HomeAssistant.lplug4
```

(Optional) **Verify** the package:

```bash
logiplugintool verify ./HomeAssistant.lplug4
```

The `.lplug4` format is a zip-like package with metadata; it’s registered with Logi Plugin Service.

3. **Install**
   Double-click the generated `.lplug4` file. It will open in **Logi Options+** and guide you through installation.

> Notes:
>
> * Keep the package name readable, e.g. `HomeAssistant_1_0.lplug4`.
> * Ensure `metadata/LoupedeckPackage.yaml` is present and OS targets match your claims.


> Tip: If you’re developing, you can run in Debug and let Loupedeck discover your dev plugin folder. See Logi Actions SDK: [text](https://logitech.github.io/actions-sdk-docs/Getting-started/)

---


---

## Architecture

```
src/
  Actions/
    ConfigureHomeAssistantAction.cs
    HomeAssistantLightsDynamicFolder.cs   # Areas → Lights → Commands (refactored)
    RunScriptAction.cs
    ToggleLightAction.cs                  # Simple individual light toggle
    AdvancedToggleLightsAction.cs         # Multi-light control with capability intersection
    AreaToggleLightsAction.cs             # Area-based control with individual capability filtering
  Services/
    # Core Services
    LightControlService.cs            # light control with debouncing
    LightStateManager.cs              # centralized state management
    HomeAssistantDataService.cs       # HA API data fetching
    HomeAssistantDataParser.cs        # JSON parsing and validation
    RegistryService.cs                # device/entity/area registry
    CapabilityService.cs              # capability inference
    IconService.cs                    # icon loading and caching
    DebouncedSender.cs               # request debouncing
    HueSaturation.cs                 # color space utilities
    # Command Pattern Architecture (NEW)
    AdjustmentCommandContext.cs       # shared command dependencies
    AdjustmentCommandFactory.cs       # command factory
    Commands/
      BrightnessAdjustmentCommand.cs  # brightness control logic
      HueAdjustmentCommand.cs        # hue control logic
      SaturationAdjustmentCommand.cs # saturation control logic
      TemperatureAdjustmentCommand.cs # color temperature logic
    Interfaces/
      IAdjustmentCommand.cs          # command interface
      IAdjustmentCommandFactory.cs   # factory interface
      ILightControlService.cs        # service contracts
      ILightStateManager.cs          # state management contract
      # ... (additional service interfaces)
  Models/
    LightCaps.cs                     # light capability model
    LightData.cs                     # light entity data
    ParsedRegistryData.cs            # registry data structures
  Util/
    ColorTemp.cs                     # color temperature conversions
    JsonExt.cs                       # JSON utilities
    TilePainter.cs                   # UI rendering helpers
  Helpers/
    ColorConv.cs                     # color space conversions
    HaEventListener.cs               # WebSocket event handling
    HaWebSocketClient.cs             # WebSocket client
    HealthBus.cs                     # health status propagation
    HSBHelper.cs                     # HSB color utilities
    PluginLog.cs                     # logging infrastructure
    PluginResources.cs               # resource management
```

**Key Architecture Principles**

* **Clean Architecture**: Clear separation between UI, business logic, and data layers
* **Command Pattern**: Adjustment operations use focused, testable command classes
* **Dependency Injection**: Services use interface-based dependency injection
* **Single Responsibility**: Each class has one clear purpose (post-refactoring)
* **State Management**: Centralized light state management with optimistic UI updates
* **Debounced Operations**: User actions are debounced to reduce Home Assistant traffic

---

## Development

### Build

```bash
dotnet build
```

### Run / Debug

* See Logi Actions SDK: [text](https://logitech.github.io/actions-sdk-docs)

### Style & Analyzers

* Nullable enabled recommended:

  ```xml
  <Nullable>enable</Nullable>
  ```
* Run `dotnet format` before committing.

---

## Testing (TODO)

Add tests under `HomeAssistant.Tests`:

**Core Business Logic**
* **Command Pattern**: Test all adjustment commands (`BrightnessAdjustmentCommand`, `HueAdjustmentCommand`, etc.)
* **Command Factory**: Test `AdjustmentCommandFactory` command creation and dependency injection
* **State Management**: Test `LightStateManager` state updates and caching

**Utilities & Helpers**
* **Color math**: `HSBHelper` conversions, Kelvin↔Mired round-trips
* **DebouncedSender**: last-write-wins, one send per burst
* **CapabilityService**: `LightCaps.FromAttributes` samples (ct-only, hs-only, onoff)

**Data Processing**
* **HomeAssistantDataParser**: JSON parsing and validation
* **RegistryService**: Device/entity/area mapping

**Integration Tests**
* **Method Decomposition**: Verify refactored mega-methods maintain functionality
* **UI State Consistency**: Test optimistic UI updates with backend state

Run:

```bash
dotnet test
```

**Test Coverage Goals**
* Command classes: 100% (focused, single-responsibility classes)
* State management: 90%+ (critical for UI consistency)
* Color utilities: 95%+ (mathematical functions)

---

## Packaging & Release

* **CI** (recommended): build, test, `dotnet format`, then produce plugin artifact.
* **Versioning**: semantic (`MAJOR.MINOR.PATCH`).
* **License file** and **attributions** required (see below).

---

## Troubleshooting

**“Auth failed” / “Timeout waiting for HA response”**

* Verify **Base URL** (must be reachable from your PC).
* If using HTTPS, ensure valid certs; try `wss://` and correct port (usually 8123).
* Regenerate the **Long-Lived Token** in HA and re-paste.

**No areas appear / Lights missing**

* Check HA entity/device/area registries: the plugin queries
  `config/entity_registry/list`, `config/device_registry/list`, `config/area_registry/list`.
* Lights outside any area land in **(No area)**.

**Controls not shown for a light**

* Capability inference hides unsupported dials. Confirm the light reports the right attributes (e.g., `supported_color_modes`).

**Brightness dial moves but light doesn’t change**

* Ensure the device supports brightness; check HA dashboard to confirm.
* Look for plugin logs about `call_service` errors (network or auth).

---

## Security & Privacy

* The plugin stores your **Base URL** and **Long-Lived Token** in Loupedeck’s plugin settings (local to your machine and stored encrypted).
* Always use `wss://` for enhanced security.
* Logs may include entity IDs and friendly names; avoid sharing logs publicly.

---

## Roadmap

* **Generalize beyond lights** (switch, fan, climate, cover, scene)
* **Other useful actions** beyond all the controls for a device (for ex toggle one light or a group)
* **Marketplace release**

---

## Contributing

Contributions welcome! Suggested ways to help:

* Open issues with reproducibility steps, device models, logs, or HA payload samples.
* “Good first issues”: tests for `HSBHelper`, `CapabilityService`, and `DebouncedSender`.
* PRs for additional domains eg. Blind controls.


---

## License

**MIT** — see [LICENSE](LICENSE).

---

## Credits

* Home Assistant team & community
* Logi Actions SDK

---

### Appendix: Power-user Notes

* **ActionParam codec** avoids stringly command parsing (`"area:<id>"`, `"device:<entity_id>"`, `"act:on:<entity_id>"`, etc.).
* **Brightness on “On”** sends last cached brightness (`>=1`) when available.
* **Area resolution precedence**: **entity area\_id** → **device area\_id** → **(No area)**.
* **Color temp**: the UI accepts Kelvin (or mired internally) and normalizes both ways for convenience.
