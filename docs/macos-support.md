# macOS / Apple Silicon support

The package enables macOS installation using the same managed plugin DLL as Windows. The target is **macOS 14+ on Apple Silicon with Logi Options+**, without Rosetta. Intel Macs and the legacy Loupedeck host are not certified.

**Status: release candidate, not hardware-certified.** Build/package support and an arm64 SDK rendering check do not prove that the installed Options+ service runs natively or that every device action works. Complete the acceptance record below before publishing a release or claiming native compatibility.

## End-user installation

Install current Logi Options+, connect a supported device, and configure Home Assistant and a long-lived access token. End users do not need a .NET SDK, developer tools, or a source checkout.

Download a release's `HomeAssistant.lplug4`, check its SHA256 against `SHA256SUMS`, and open it in Finder to install through Options+. Open the Home Assistant configuration action and enter the Home Assistant URL and token. Do not copy the plugin DLL into the Options+ application bundle.

The package retains `HomeAssistant` as its ID and uses `bin` for both OSes. It contains no host API, graphics libraries, or standalone runtime. Those are supplied by Logi Plugin Service. The macOS manifest field selects the OS, not the CPU architecture; incidental Intel compatibility is not a support claim.

## Developer setup

1. Install Options+ and the **arm64 .NET 8 SDK** matching `global.json` (8.0.427 or a compatible later patch). Verify `dotnet --info` reports `osx-arm64`.
2. From the repository root, run `dotnet tool restore`. This restores the local pinned LogiPluginTool 6.1.4.22672; it does not replace a globally installed tool.
3. Use a native .NET debugger (VS Code C# extension or Rider). VS Code includes service-name and process-picker attach configurations.

The plugin remains a framework-dependent `net8.0` class library. Do not publish it as an executable, use NativeAOT, or add an unconditional `osx-arm64` RID. The host supplies the runtime. The pinned tool is the .NET 8-compatible SDK fixture; newer LogiPluginTool releases may require a newer .NET runtime. Reassess the .NET 8 lifecycle and host runtime before changing this pin or the target framework.

Default host paths follow the Logitech SDK template:

| Purpose | macOS | Windows |
| --- | --- | --- |
| API folder | `/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/` | `%ProgramW6432%\Logi\LogiPluginService\` |
| Development links | `~/Library/Application Support/Logi/LogiPluginService/Plugins/` | `%LocalAppData%\Logi\LogiPluginService\Plugins\` |

Confirm these against the installed host version. Both properties accept absolute overrides, with or without a trailing separator:

```bash
dotnet build src/HomeAssistantPlugin.csproj -p:PluginApiDir="/path with spaces/API" -p:PluginDir="/path with spaces/Plugins"
```

Paths are native filesystem paths, not shell-escaped values. The Mac home directory is expanded by MSBuild. Linux has no automatic host/deployment paths.

### Build, debug, and clean

```bash
# Local developer build: create the link and request a best-effort reload.
dotnet build src/HomeAssistantPlugin.csproj

# Build without modifying or reloading the host.
dotnet build HomeAssistantPlugin.sln -c Release -p:DeployPlugin=false

# Test without deployment, using the installed host API.
dotnet test HomeAssistantPlugin.sln -p:DeployPlugin=false

# Remove build outputs and only this checkout/configuration's development link.
dotnet clean src/HomeAssistantPlugin.csproj
```

Debug and Release plugin roots are `bin/Debug/` and `bin/Release/`; each contains `bin/`, `metadata/`, and action symbols. Tests have separate output/intermediate directories. Direct-project builds and solution builds use the same paths. Solution builds disable deployment by default so the test and solution references share one plugin build instance; use a direct plugin-project build for automatic local deployment.

Use `-p:ReloadPlugin=false` to create a development link without opening the reload URL. `-p:DeployPlugin=false` disables both link creation/removal and reload. Clean leaves links pointing at another checkout or configuration intact. Installed plugin folders and settings are never deleted.

A development link takes precedence over a packaged installation. Before testing an installed package, clean the configuration that owns the link or remove that specific `.link` file after checking its contents.

### SDK-only builds and CI

```bash
dotnet tool restore
dotnet build HomeAssistantPlugin.sln -c Release -p:UseToolPluginApi=true -p:DeployPlugin=false
dotnet test HomeAssistantPlugin.sln -p:UseToolPluginApi=true -p:DeployPlugin=false
```

`UseToolPluginApi=true` downloads the exact official LogiPluginTool NuGet package through `PackageDownload` and resolves `PluginApi.dll` from its `tools/net8.0/any` directory. NuGet verifies package integrity during restore. Explicit `PluginApiDir` overrides remain authoritative. SDK-only builds disable deployment by default. Vendor binaries stay in the local NuGet cache and are not committed or included in plugin packages; use is subject to the [Logitech developer agreement](https://www.logitech.com/en-us/software/marketplace/developer-agreement.html).

The Windows x64 and macOS arm64 CI matrix restores the same SDK fixture, asserts native architecture, builds, runs build-integration tests and the full regression suite, checks formatting, and packs/verifies diagnostic artifacts. Native graphics testing runs separately on the arm64 Mac runner:

```bash
RUN_NATIVE_SMOKE=1 dotnet test tests/HomeAssistantPlugin.UnitTests -c Release \
  -p:UseToolPluginApi=true -p:DeployPlugin=false -p:EnableNativeSmokeTests=true \
  --filter "Category=Native"
```

This explicitly provisions the fixture's macOS native libraries and checks real SVG/PNG loading, fallback icons, and text rendering. Normal unit tests do not require native graphics, and the existing skipped graphics tests remain unchanged. A fixture rendering pass is not certification of the installed host.

For formatter invocations use environment properties, because `dotnet format` does not accept MSBuild `-p:` arguments:

```bash
UseToolPluginApi=true DeployPlugin=false dotnet format HomeAssistantPlugin.sln --verify-no-changes --no-restore
```

On PowerShell set `$env:UseToolPluginApi = "true"` and `$env:DeployPlugin = "false"` before running the formatter.

**Existing validation debt:** the original 1.2.0 source, compiled with only its Windows test API hint redirected to the official API, has 19 failing cases out of 1,132 tests in this Linux environment (1,094 pass, 19 skip). They concern null/fallback handling, extreme adjustment values, registry ordering, a color expectation, and debounce timing. Existing repository-wide formatting also needs attention. These are not silently skipped or treated as passing: CI uploads diagnostic packages/results but fails its final regression/formatting gate, blocking release automation until the baseline is resolved. New build-contract tests are a separate required check.

## Packaging and releases

```bash
dotnet tool restore
dotnet build src/HomeAssistantPlugin.csproj -c Release -t:PackPlugin \
  -p:UseToolPluginApi=true -p:DeployPlugin=false
```

`StagePlugin` first rejects enabled deployment, builds, and recreates only `artifacts/package/` from source package assets and the plugin's own runtime payload. `PackPlugin` creates `artifacts/HomeAssistant.lplug4` and verifies it with the pinned local tool. Stale build outputs, test assemblies, development links, host DLLs, and native graphics libraries are not taken from an arbitrary output-directory copy.

The tag/manual release workflow reruns both OS jobs against the selected tag and requires `vMAJOR.MINOR.PATCH` to match the packaged manifest. Identical payloads share `bin`; differing Windows/Mac binaries are isolated in `bin/win` and `bin/mac` with the corresponding manifest paths. Shared metadata/assets must match. The combined package is packed and verified again, never assembled by mixing native dependencies in one folder. A successful run creates a **draft only**, with a SHA256 checksum. Publishing requires a maintainer to review the hardware record; Marketplace submission is a separate manual vendor process.

## Physical host acceptance (required before publishing)

Use a physical Apple Silicon Mac, not Rosetta or an Intel runner. Test the macOS 14 baseline and a current host-supported macOS release. Record a Windows installation smoke test too.

Capture the host contract before testing:

```bash
sw_vers
uname -m
dotnet --info
bundle="/Applications/Utilities/LogiPluginService.app"
executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$bundle/Contents/Info.plist")
file "$bundle/Contents/MacOS/$executable"
lipo -archs "$bundle/Contents/MacOS/$executable"
find "$bundle/Contents" -name '*.runtimeconfig.json' -print
find "$bundle/Contents" \( -name 'libSkiaSharp.dylib' -o -name 'libHarfBuzzSharp.dylib' \) -exec lipo -archs {} \;
```

Confirm the executable and loaded graphics libraries include arm64, inspect the host runtime configuration for compatibility with the `net8.0` plugin, and use Activity Monitor's **Kind** column to verify the running Logi Plugin Service is **Apple**, not Intel. A universal binary on disk alone does not establish native execution. Record actual API/plugin/log paths and the service version; the manifest's existing minimum service version is not a statement of hardware certification.

Attach a completed record to the draft release:

| Check | Record required |
| --- | --- |
| Environment | Candidate commit/package SHA256, Mac chip, macOS version, Options+/service version, runtime, device, API/plugin/log paths, running service Kind |
| Fresh install | Install downloaded `.lplug4` with normal quarantine/Gatekeeper behavior; discovery and load without an SDK or checkout |
| Configuration | URL/token entry and persistence across reload/service restart; no unwanted application association |
| Device controls | MX Creative Console buttons/dials; Actions Ring basic actions when hardware is available |
| Features | Lights (brightness/temperature/hue/saturation), switches, covers/tilt, scripts, area/advanced actions, dynamic folders, external state updates |
| Rendering | SVG/PNG icons, labels, fallback glyphs, folder navigation, and rotary feedback |
| Network/lifecycle | LAN HTTP, trusted HTTPS, bad credentials, disconnect/reconnect, sleep/wake, host restart; local-network permissions where applicable |
| Upgrade/uninstall | Upgrade from the previous package preserves settings/bindings; no development link masks the installed package |
| Windows regression | Packaged install, discovery, configuration, core actions, rendering, upgrade |
| Result | Pass/fail per check, defects/log excerpts with tokens removed, tester sign-off |

Do not weaken TLS verification, globally disable Gatekeeper, or introduce app entitlements/signing workarounds without a reproduced host-specific requirement. If the installed service or its graphics backend is x64-only, native support is blocked upstream; do not relabel a Rosetta run as arm64 support.
