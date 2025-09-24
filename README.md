# Loupedeck × Home Assistant Plugin

Control your Home Assistant lights (and soon, any entity) from your Loupedeck with a fast, optimistic UI, debounced actions, and capability-aware controls. Open the **Home Assistant** dynamic folder to browse **Areas → Lights → Commands** and use dials for brightness, color temperature, hue, and saturation.

> **Status**: pre-release. Marketplace submission in progress. OSS-ready and actively refactoring for general entities.

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

* **Area-first navigation**
  Root view lists all **Home Assistant Areas**. Pick an area → see **lights** in that area → pick a light for **commands/dials**.
* **Capability-aware controls**
  Only shows dials/actions a device actually supports (on/off, brightness, color temperature, hue/sat).
* **Optimistic UI + debounced sends**
  Dials update instantly, and actions are coalesced to minimize HA traffic.
* **Resilient WS integration**
  Separate authenticated WebSocket for requests + event stream listener to keep state fresh.
* **Typed, testable core**
  Clear separation between plugin UI and HA core (models, client, event stream, utilities).
* **Icon + tile helpers**
  Centralized `IconService` (one-time icon load) and `TilePainter` (DRY image centering & fallback glyphs).

---

## Demo

*(Screenshots/GIFs encouraged — drop them here later)*

* Root: Areas grid
* Area: Lights in “Office”
* Device: On/Off, Brightness dial, Temp/Hue/Sat dials

---

## Requirements

* **Loupedeck** software + a compatible Loupedeck device (Live/Live S/CT, etc.).
* **Home Assistant** with WebSocket API enabled (standard).
* **Home Assistant Long-Lived Access Token** (Profile → Security).
* **.NET SDK** 8.0 (recommended) to build from source.
* Windows 10/11 (for building and running the Loupedeck plugin locally).

---

## Install

### Marketplace (soon)

* Search for **“Home Assistant”** in the Loupedeck Marketplace and install.

### Manual (from source)

1. Clone this repo.
2. Build in **Release**:

   ```bash
   dotnet build -c Release
   ```
3. Deploy the built plugin according to Loupedeck’s plugin install instructions (path varies by version; typically via the Loupedeck UI’s **Developer / Load Plugin** or by placing output in the local Plugins folder).
4. Restart Loupedeck.

> Tip: If you’re developing, you can run in Debug and let Loupedeck discover your dev plugin folder.

---

## Quick Start

1. In Loupedeck, open the **Home Assistant** dynamic folder.
2. Press **Configure Home Assistant** action (or open the plugin’s settings):

   * **Base URL**: `ws://homeassistant.local:8123/` (or your `wss://` URL)
   * **Long-Lived Token**: paste from HA Profile
   * Click **Test connection**.
3. Return to the **Home Assistant** dynamic folder:

   * Select an **Area** → pick a **Light** → use **commands/dials**.

---

## Controls & Navigation

* **Root (Areas)**
  Shows all HA areas that contain at least one light (plus a synthetic **(No area)** bucket if needed).

  * Buttons: **Back**, **Status**, one button per **Area**, **Retry**.
* **Area (Lights)**
  Lists the lights within the selected area.

  * Buttons: **Back**, **Status**, one button per **Light**.
* **Device (Commands & Dials)**
  Per-light controls, depending on capabilities:

  * **On** / **Off** buttons
  * **Brightness** dial (0..255), **Color Temp** dial (Kelvin/mired), **Hue**, **Saturation** dials
  * Brightness on **On**: uses cached brightness (min 1) when available
* **Back** behavior
  Device → Area → Root → closes folder.

**Status tile** reflects connectivity; press it to see a status message.

---

## Configuration

### Plugin Settings

* **Base URL**: `ws://<host>:<port>/` or `wss://...` (the plugin resolves `/api/websocket` behind the scenes).
* **Token**: Home Assistant **Long-Lived Access Token**.
* **Test connection**: Attempts WS auth and shows status.

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
    Tiles/
      TilePainter.cs                  # center/pad/glyph helpers
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
