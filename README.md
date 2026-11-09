# Options Plus × Home Assistant Plugin

Control your Home Assistant lights and switches from your Creative Console with a fast, optimistic UI, debounced actions, and capability-aware controls. Open the **All Light Controls** dynamic folder(only on creative console) to browse **Areas → Lights → Commands** and use the dial for brightness, color temperature, hue, and saturation.

> **Status**: Beta. Support for lights and switches implemented. OSS-ready and actively monitoring and fixing issues. The plugin was only tested on the Creative Console with Phillips Wiz lights.

## Need help?

[🧰 Troubleshooting guide](./TROUBLESHOOTING.md) ·
[🐞 Report a bug](https://github.com/Logitech/cto-HomeAssistantPlugin-OptionsPlus/issues/new?template=bug_report.yml) ·
[💡 Request a feature](https://github.com/Logitech/cto-HomeAssistantPlugin-OptionsPlus/issues/new?template=feature_request.yml) ·
[❓ Ask a question](https://github.com/Logitech/cto-HomeAssistantPlugin-OptionsPlus/discussions/new)



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
- [Testing](#testing)
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
4. Put any **actions** you want into your layout. See what they do in the explanation below
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

* Works with any HA light entity; no variables are used (it's a pure toggle).
* Requires HA **Base URL** and **Long-Lived Token** to be set (see *Configure Home Assistant*).

---

### Toggle Switches

Toggle one or more `switch.*` entities on/off via Home Assistant.

* **Press** → sends `switch.toggle` for the selected switches.

**To configure:**

1. Place **Toggle Switches** on your layout.
2. In the popup:

   * **Switches**: pick from your `switch.*` entities (the list auto-loads from HA state), or enter additional switch entity IDs in the field below (e.g., `switch.living_room_fan,switch.kitchen_outlet`)
   * If HA isn't configured/connected yet, you'll see a hint to open plugin settings.

**Notes**

* Works with any HA switch entity; no variables are used (it's a pure toggle).
* Supports multiple switches simultaneously.
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

### Area Toggle Switches

Area-based switch control that automatically discovers and controls all switches in a selected area.

* **Press** → toggles all switches in the selected area.

**To configure:**

1. Place **Area Toggle Switches** on your layout.
2. In the popup:
   
   * **Select Area**: Choose from dropdown showing "Area Name (X switches)" format
   * Area selection automatically includes all switch entities within that area

**Key Features:**

* **Automatic Switch Discovery**: Finds all switches in selected area automatically
* **Area-Centric Control**: No need to select individual switches - just pick the area
* **Simple Toggle Logic**: Clean on/off control for all switches in the area

**Best for**: Controlling entire areas/rooms where you want to toggle all switches together (outlets, fans, etc.).

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

### All Switch Controls (Areas → Switches → Commands)

Browse all switches and control them with simple on/off functionality. Only available for the Creative Console or equivalents.

1. Add **All Switch Controls** to your layout and press it to enter.

2. You'll see:

   * **Back** — navigates up one level (Device → Area → Root → closes).
   * **Status** — shows ONLINE/ISSUE; press when ISSUE to surface the error in Options+.
   * **Retry** — reconnects and reloads data from HA.
   * **Areas** — your HA Areas (plus **(No area)** if some switches aren't assigned).

3. **Pick an Area** → shows all **Switches** in that area.

4. **Pick a Switch** → shows **Commands** for that device:

   * **On / Off** buttons

     * Simple toggle control for the selected switch.

**To configure:**

Simply add the action to your layout - no additional configuration needed.

**Best for:** Quick access to all your switches organized by area, perfect for controlling outlets, fans, and other switch-based devices throughout your home.

**Notes**

* Navigation follows the same pattern as lights: **Back** steps through Device → Area → Root. From Root, Back closes the folder.
* All switches are automatically discovered from your Home Assistant configuration.

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

* **Toggle Single Switch (Action)**
  Simple toggle action for individual switch entities with basic on/off control.

* **Multi-Switch Control (Action)**
  Control multiple selected switches with simultaneous toggle functionality.

* **Area-Based Switch Control (Action)**
  Intelligent area control that automatically discovers all switches in a selected area.


---


## Requirements

* **Loupedeck** software + a compatible Loupedeck device (Creative Console, Live/Live S/CT, etc.) Some simple actions can be used with the actions ring.
* **Home Assistant** with WebSocket API enabled (standard).
* **Home Assistant Long-Lived Access Token** (Profile → Security).
* Windows 10/11, or macOS 14+ on Apple Silicon with Logi Options+.
* macOS support is a release candidate pending native host/hardware certification. See [macOS setup and acceptance requirements](docs/macos-support.md).
* To build from source only: the **.NET 8 SDK** matching `global.json` (arm64 on Mac). End users do not need the SDK.

---

## Install

### Marketplace

* Search for **“Home Assistant”** in the Loupedeck Marketplace and install.


---

### Manual (from source)

1. **Build the plugin (Release)**

```bash
dotnet tool restore
dotnet build src/HomeAssistantPlugin.csproj -c Release -t:StagePlugin -p:DeployPlugin=false
```

2. **Package to a `.lplug4`** using the Logi Plugin Tool

```bash
dotnet tool run logiplugintool pack ./artifacts/package/ ./artifacts/HomeAssistant.lplug4
```

(Optional) **Verify** the package:

```bash
dotnet tool run logiplugintool verify ./artifacts/HomeAssistant.lplug4
```

The `.lplug4` format is a zip-like package with metadata; it’s registered with Logi Plugin Service.

3. **Install**
   Double-click the generated `.lplug4` file. It will open in **Logi Options+** and guide you through installation.

> Notes:
>
> * Keep the package name readable, e.g. `HomeAssistant_1_0.lplug4`.
> * Ensure `metadata/LoupedeckPackage.yaml` is present and OS targets match your claims.


> Tip: Debug builds of the plugin project create a development link automatically. Use `-p:DeployPlugin=false` to avoid changing/reloading your local host. See [Mac and cross-platform development](docs/macos-support.md) and [Logi Actions SDK](https://logitech.github.io/actions-sdk-docs/getting-started/).

---


---

## Architecture

```
src/
  Actions/
    AreaToggleLightsAction.cs             # Area-based control with individual capability filtering
    AreaToggleSwitchesAction.cs           # Area-based switch control
    AdvancedToggleLightsAction.cs         # Multi-light control with capability intersection
    ConfigureHomeAssistantAction.cs
    HomeAssistantLightsDynamicFolder.cs   # Areas → Lights → Commands (refactored)
    HomeAssistantSwitchesDynamicFolder.cs # Areas → Switches → Commands
    RunScriptAction.cs
    ToggleLightAction.cs                  # Simple individual light toggle
    ToggleSwitchesAction.cs               # Multi-switch toggle control
  Services/
    # Core Services
    CapabilityService.cs              # capability inference
    DebouncedSender.cs               # request debouncing
    HomeAssistantDataParser.cs        # JSON parsing and validation
    HomeAssistantDataService.cs       # HA API data fetching
    HueSaturation.cs                 # color space utilities
    IconService.cs                    # icon loading and caching
    LightControlService.cs            # light control with debouncing
    LightStateManager.cs              # centralized state management
    RegistryService.cs                # device/entity/area registry
    SwitchControlService.cs           # switch control with debouncing
    SwitchStateManager.cs             # centralized switch state management
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
    SwitchCaps.cs                    # switch capability model
    SwitchData.cs                    # switch entity data
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
dotnet build src/HomeAssistantPlugin.csproj
```

For SDK-only builds/tests without an installed Logi host, use `-p:UseToolPluginApi=true -p:DeployPlugin=false`. See [cross-platform tooling and validation](docs/macos-support.md#sdk-only-builds-and-validation).

### Run / Debug

* See Logi Actions SDK: [text](https://logitech.github.io/actions-sdk-docs)

### Style & Analyzers

* Nullable enabled recommended:

  ```xml
  <Nullable>enable</Nullable>
  ```
* Run `dotnet format` before committing.

---

# 🧪 Testing

## Overview
This project includes a comprehensive testing suite following industry best practices with **1,378+ tests** covering all critical functionality. The test suite ensures reliability, performance, and maintainability of the Home Assistant integration.

## Test Coverage
- **Foundation & Utilities:** 95% coverage (mathematical functions, color conversions)
- **Command Pattern & Business Logic:** 100% coverage (core plugin functionality)
- **Services & Data Processing:** 85% coverage (external integration points)

## Test Structure
```
tests/HomeAssistantPlugin.UnitTests/
├── Helpers/                    # Utility function tests
│   ├── HSBHelperTests.cs      # Color space conversions
│   └── ColorConvTests.cs      # Color science algorithms
├── Models/                     # Data model tests
│   └── LightCapsTests.cs      # Capability detection logic
├── Services/                   # Service layer tests
│   ├── Commands/              # Command pattern implementations
│   ├── *ServiceTests.cs       # Business logic services
│   └── *FactoryTests.cs       # Factory pattern implementations
└── Util/                      # Mathematical utilities
    └── ColorTempTests.cs      # Temperature conversions
```

## Running Tests

**Execute all tests:**
```bash
cd tests/HomeAssistantPlugin.UnitTests
dotnet test
```

**Generate coverage report:**
```bash
dotnet test --collect:"XPlat Code Coverage"
```

**Run specific categories:**
```bash
dotnet test --filter "Category=Unit"        # Mathematical utilities
dotnet test --filter "Category=Command"     # Command pattern tests
dotnet test --filter "Category=Service"     # Service layer tests
```

## Testing Framework
- **xUnit 2.6.2** - Modern testing framework with excellent async support
- **NSubstitute 5.1.0** - Clean, readable mocking for interface-heavy architecture
- **FluentAssertions 6.12.0** - Expressive and readable test assertions
- **Coverlet** - Code coverage analysis and reporting

## Test Categories

### **Mathematical Precision (258 Tests)**
Validates color science algorithms critical for accurate light control:
- **HSB↔RGB conversions** with mathematical precision validation
- **Color temperature** Kelvin↔Mired conversions with round-trip accuracy
- **CIE 1931 XYZ color space** conversions following international standards
- **Boundary condition testing** for all mathematical operations

### **Command Pattern Validation (560 Tests)**
Ensures reliable command execution and service coordination:
- **All adjustment commands** (brightness, hue, saturation, temperature)
- **Factory pattern implementation** with dependency injection validation
- **Debounced operations** with thread-safety and timing accuracy
- **Service coordination** patterns with comprehensive mocking

### **Service Layer Integration (7 Service Test Files)**
Validates external integration points and data processing:
- **JSON parsing robustness** with real Home Assistant data samples
- **State management consistency** between UI and backend
- **WebSocket communication** handling and error recovery
- **Registry data processing** with device/entity/area mapping
- **Concurrent access patterns** and performance validation

## Quality Assurance

### **Production Readiness Features:**
- ✅ **Real-world data testing** with actual Home Assistant device configurations
- ✅ **Error resilience** for network failures and malformed data
- ✅ **Thread safety validation** for concurrent operations
- ✅ **Memory management** testing preventing leaks in long-running scenarios
- ✅ **Performance characteristics** validated under load conditions

### **Development Confidence:**
- ✅ **Refactoring safety** with comprehensive regression testing
- ✅ **Integration validation** ensuring proper Home Assistant API compatibility
- ✅ **Edge case coverage** for boundary conditions and error scenarios
- ✅ **Mathematical accuracy** ensuring precise color control operations

---

## Packaging & Release

* **Validation**: run build, test, format, and package verification manually on Windows x64 and macOS arm64; no GitHub Actions workflows are configured.
* **Releases**: package and publish manually after resolving validation failures and completing physical Mac/Windows acceptance; see [release requirements](docs/macos-support.md#packaging-and-releases).
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

* **Generalize beyond lights and switches** (fan, climate, cover, scene)
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
