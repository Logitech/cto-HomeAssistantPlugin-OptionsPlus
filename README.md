# Loupedeck × Home Assistant Plugin

Control your Home Assistant lights (and soon, any entity) from your Creative Console with a fast, optimistic UI, debounced actions, and capability-aware controls. Open the **All Light Controls** dynamic folder to browse **Areas → Lights → Commands** and use the dial for brightness, color temperature, hue, and saturation.

> **Status**: pre-release. For now there is only support for lights. OSS-ready and actively refactoring for general entities. The plugin was only tested on the Creative Console.

---

## Table of Contents

* [Features](#features)
* [Demo](#demo)
* [Requirements](#requirements)
* [Install](#install)
* [Quick Start](#quick-start)
* [Controls & Navigation](#controls--navigation)
* [Configuration](#configuration)
* [Architecture](#architecture)
* [Development](#development)
* [Testing](#testing)
* [Packaging & Release](#packaging--release)
* [Troubleshooting](#troubleshooting)
* [Security & Privacy](#security--privacy)
* [Roadmap](#roadmap)
* [Contributing](#contributing)
* [License](#license)
* [Credits](#credits)

---

## Features

* **Setup (Action)**
  A dedicated action to setup the connection with the **Home Assistant hub`**.

* **Run Home Assistant Scripts (Action)**
  A dedicated action to trigger any **Home Assistant `script.*`** (with optional variables) and stop/toggle when running.

* **Control All Lights (Action) with Area-First Navigation**
  One action to browse **Areas → Lights → Commands**: pick an area, select a light, then use per-device controls.

* **Capability-Aware Controls**
  Only shows controls a device actually supports (on/off, brightness, color temperature, hue, saturation).

* **Optimistic UI + Debounced Sends**
  Dials update instantly while changes are coalesced to reduce Home Assistant traffic and avoid jitter.

* **Resilient WebSocket Integration**
  Authenticated request channel plus an event listener to keep state fresh.


---

## Demo

*(Screenshots/GIFs encouraged — drop them here later)*

* Root: Areas grid
* Area: Lights in “Office”
* Device: On/Off, Brightness dial, Temp/Hue/Sat dials

---

## Requirements

* **Loupedeck** software + a compatible Loupedeck device (Creative Console, Live/Live S/CT, etc.).
* **Home Assistant** with WebSocket API enabled (standard).
* **Home Assistant Long-Lived Access Token** (Profile → Security).
* **.NET SDK** 8.0 (recommended) to build from source.
* Windows 10/11 (for building and running the Loupedeck plugin locally).

---

## Install

### Marketplace (soon)

* Search for **“Home Assistant”** in the Loupedeck Marketplace and install.

Awesome—here’s the revised **Manual (from source)** section for your README, aligned with Logitech’s docs:

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

## Quick Start

1. In Loupedeck, open the **Home Assistant** dynamic folder.
2. Create a **Long-Lived Token** and copy it Home Assistant -> Profile -> Security -> Long-lived access tokens
2. Press **Configure Home Assistant** action (or open the plugin’s settings):

   * **Base URL**: `ws://homeassistant.local:8123/` (or your `wss://` URL)
   * **Long-Lived Token**: paste from HA Profile
   * Click **Test connection**.
   * If no error appears after short time click save.
3. Put any **actions** you want into your layout

---

## Actions & How to Use Them

### Configure Home Assistant (one-time setup)

Use this once to connect the plugin to your HA instance—then you can remove it from your layout (settings persist).

1. Drop **Configure Home Assistant** into any page.
2. Enter your **HA WebSocket URL** (e.g., `ws://homeassistant.local:8123/` or `wss://...`).
3. Paste a **Long-Lived Access Token** from your HA Profile.
4. Click **Test connection**. If no error appears after a short moment, you’re good.
5. Click **Save**. You may now delete the action from your layout; the plugin will remember your settings.

---

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

### All Light Controls (Areas → Lights → Commands)

Browse all lights and control them with capability-aware dials.

1. Add **All Light Controls** to your layout and press it to enter.

2. You’ll see:

   * **Back** — navigates up one level (Device → Area → Root → closes).
   * **Status** — shows ONLINE/ISSUE; press when ISSUE to surface the error in Options+.
   * **Retry** — reconnects and reloads data from HA.
   * **Areas** — your HA Areas (plus **(No area)** if some lights aren’t assigned).

3. **Pick an Area** → shows all **Lights** in that area.

4. **Pick a Light** → shows **Commands & Dials** for that device:

   * **On / Off** buttons

     * On uses the last cached brightness (min 1) when available.
   * **Brightness** (dial): 0–255, optimistic UI with debounced sending.
   * **Color Temp** (dial): warm ↔ cool, shown only if supported.
   * **Hue** (dial): 0–360°, shown only if supported.
   * **Saturation** (dial): 0–100%, shown only if supported.

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

## Architecture

```
src/
  HomeAssistantPlugin/                # Loupedeck plugin (UI-only)
    Actions/
      ConfigureHomeAssistantAction.cs
      RunScriptAction.cs
      HomeAssistantDynamicFolder.cs   # now Areas → Lights → Commands
    Services/
      IconService.cs                  # one-time icon load and cache
      DebouncedSender.cs
      LightControlService.cs
      CapabilityService.cs            # capability inference
      ActionParam.cs                  # action parameter codec
    Models/
      LightCaps.cs
    Util/
      ColorTemp.cs
      HSBHelper.cs
      JsonExt.cs
      TilePainter.cs                  # center/pad/glyph helpers
    Helpers/
      HaWebSocketClient.cs
      HaEventListener.cs
      HealthBus.cs                    # simple health propagation
  HomeAssistant.Core/                 # (future) pure HA integration layer
  HomeAssistant.Tests/                # unit tests for math/caps/debounce/etc.
```

**Key ideas**

* **UI layer** renders folders/tiles/dials and translates user input → service calls.
* **Core/services** own WebSocket, event stream, state inference, debouncing, JSON payloads.
* **CapabilityService** centralizes capability inference from HA attributes.
* **IconService/TilePainter** remove duplicated icon math and resource loading.

---

## Development

### Build

```bash
dotnet build
```

### Run / Debug

* Start Loupedeck.
* Load the plugin (dev mode) or point Loupedeck to your build output.

### Style & Analyzers

* Nullable enabled recommended:

  ```xml
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  ```
* Run `dotnet format` before committing.

---

## Testing

Add tests under `HomeAssistant.Tests`:

* **Color math**: `HSBHelper` conversions, Kelvin↔Mired round-trips
* **DebouncedSender**: last-write-wins, one send per burst
* **CapabilityService**: `LightCaps.FromAttributes` samples (ct-only, hs-only, onoff)
* **ActionParam**: encode/parse round-trip

Run:

```bash
dotnet test
```

---

## Packaging & Release

* **CI** (recommended): build, test, `dotnet format`, then produce plugin artifact.
* **Versioning**: semantic (`MAJOR.MINOR.PATCH`).
* **Changelog**: keep `CHANGELOG.md` up to date.
* **Marketplace**: include screenshots, description, permissions, and setup steps.
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

**High CPU or sluggish UI**

* Debounced sender is already coalescing; ensure you didn’t add additional timers.
* Check that you’re not spamming `ButtonActionNamesChanged()` in a loop.

---

## Security & Privacy

* The plugin stores your **Base URL** and **Long-Lived Token** in Loupedeck’s plugin settings (local to your machine).
* No telemetry.
* Logs may include entity IDs and friendly names; avoid sharing logs publicly.

---

## Roadmap

* **Generalize beyond lights** (switch, fan, climate, cover, scene)
* **State store** for all entities + reusable dial model
* **Reconnect & backoff** strategies exposed in logs
* **Localization** scaffolding
* **Marketplace release**

---

## Contributing

Contributions welcome! Suggested ways to help:

* Open issues with repro steps, device models, or HA payload samples.
* “Good first issues”: tests for `HSBHelper`, `CapabilityService`, and `DebouncedSender`.
* PRs for additional domains (start with a capability → dial definition).

Please see **CONTRIBUTING.md** (create if missing) and follow the code style above.

---

## License

**MIT** — see [LICENSE](LICENSE).

> **Icon attributions**: If you ship 3rd-party icons, include their licenses and credits in `NOTICE` or this section.

---

## Credits

* Home Assistant team & community
* Loupedeck SDK & developer community
* Contributors (add yourself in `AUTHORS`)

---

### Appendix: Power-user Notes

* **ActionParam codec** avoids stringly command parsing (`"area:<id>"`, `"device:<entity_id>"`, `"act:on:<entity_id>"`, etc.).
* **Brightness on “On”** sends last cached brightness (`>=1`) when available.
* **Area resolution precedence**: **entity area\_id** → **device area\_id** → **(No area)**.
* **Color temp**: the UI accepts Kelvin (or mired internally) and normalizes both ways for convenience.
