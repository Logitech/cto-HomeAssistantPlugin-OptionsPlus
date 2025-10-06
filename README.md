# Loupedeck × Home Assistant Plugin

Control your Home Assistant lights (and soon, any entity) from your Creative Console with a fast, optimistic UI, debounced actions, and capability-aware controls. Open the **All Light Controls** dynamic folder to browse **Areas → Lights → Commands** and use the dial for brightness, color temperature, hue, and saturation.

> **Status**: pre-release. For now there is only support for lights. OSS-ready and actively refactoring for general entities. The plugin was only tested on the Creative Console.

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
   * If no error appears after short time click save.
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

* **Control All Lights (Action) with Area-First Navigation**
  One action to browse **Areas → Lights → Commands**: pick an area, select a light, then use per-device controls.

* **Capability-Aware Controls**
  Only shows controls a device actually supports (on/off, brightness, color temperature, hue, saturation).

* **Optimistic UI + Debounced Sends**
  Dials update instantly while changes are coalesced to reduce Home Assistant traffic and avoid jitter.

* **Resilient WebSocket Integration**
  Authenticated request channel plus an event listener to keep state fresh.


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


---

## Architecture

```
src/
  HomeAssistantPlugin/                # Loupedeck plugin (UI-only)
    Actions/
      ConfigureHomeAssistantAction.cs
      RunScriptAction.cs
      HomeAssistantLightsDynamicFolder.cs   # now Areas → Lights → Commands
    Services/
      IconService.cs                  # one-time icon load and cache
      DebouncedSender.cs
      LightControlService.cs
      CapabilityService.cs            # capability inference
      ActionParam.cs                  # action parameter codec
      Hs.cs
      IHaClient.cs
    Models/
      LightCaps.cs
    Util/
      ColorTemp.cs
      JsonExt.cs
      TilePainter.cs                  # center/pad/glyph helpers
    Helpers/
      HSBHelper.cs
      HaWebSocketClient.cs
      HaEventListener.cs
      HealthBus.cs                    # simple health propagation
      HaEventListener.cs
      PluginLog.cs
      PluginResources.cs
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

* See Logi Actions SDK: [text](https://logitech.github.io/actions-sdk-docs)

### Style & Analyzers

* Nullable enabled recommended:

  ```xml
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  ```
* Run `dotnet format` before committing.

---

## Testing (TODO)

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
