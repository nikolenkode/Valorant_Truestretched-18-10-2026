# NVIDIA Custom Resolution

**English** | [Русский](RREADME.md)

Creates a custom resolution in the NVIDIA driver and stretches it to the full screen, without clicking through NVIDIA Control Panel. Typical use: stretched resolutions for games, for example 1280x960 or 900x573 on a 16:9 monitor.

Two front ends share the same engine (`CustomResCore.cs`):

| File | What it is |
| --- | --- |
| `CustomRes.exe` | Graphical interface (C#, WinForms). Build it with `build.bat` from `CustomResCore.cs`, `CustomResGui.cs` and `app.manifest`. |
| `custom_res.bat` | Console version in a single file (a PowerShell script with the same C# engine inside). |

Only the **primary** display is changed. The other monitors keep their resolution, refresh rate and position.

## Why the picture used to get black bars on the sides

NVIDIA Control Panel creates a custom resolution with the **Automatic** timing as a *GPU-scaled* mode: the monitor keeps receiving its native timing and the GPU stretches the smaller source mode to it. If a custom resolution is created with a timing of its own (CVT and so on), the monitor receives exactly that resolution and decides how to scale it. Most monitors keep the aspect ratio and add black bars.

This tool does what Control Panel does: it asks the driver for the Automatic timing and forces the **Full-screen, GPU** scaling mode, so the stretching is done by the GPU and the monitor never sees the smaller resolution.

## What it does

1. Optionally disables the monitor devices ("Generic PnP Monitor") in Device Manager. Only the device entry is disabled, the physical monitor is not touched. This is the usual workaround for the driver refusing custom resolutions that the monitor's EDID does not list.
2. Creates the custom resolution: the driver is asked for the timing (`NvAPI_DISP_GetTiming`: Automatic first, then Current, CVT-RB, CVT, GTF) and the resolution is created with `NvAPI_DISP_TryCustomDisplay`, the same call as the **Test** button in Control Panel. A built-in CVT-RB timing is the last resort.
3. Stretches right away: the resolution is saved (`NvAPI_DISP_SaveCustomDisplay`) and the display configuration is applied (`NvAPI_DISP_SetDisplayConfig`) with the new source mode and the scaling mode **Force GPU - Full Screen**. The other monitors are moved so that they still touch the primary display on the same side. The configuration is validated before it is applied.
4. Checks the result and prints what the driver reports: the source size, the scaling mode and the signal that goes to the monitor.
5. Asks **once** whether the picture is fine. Without an answer within 15 seconds everything is rolled back: the original display configuration is restored and the new custom resolution is deleted.
6. Persists the configuration, offers to enable the monitors again (or does it according to the checkbox) and checks that the mode survived. If it was reset, it is applied again.

## How the result is checked

- **Input checks:** size and refresh limits. For a 1:1 mode (`/nostretch`) also the timing and a warning for widths that are not a multiple of 8.
- **Timing check:** a timing from the driver is compared with the request before it is used. The native timing is accepted only when it is larger than the requested mode.
- **Driver check:** NVIDIA must accept the timing and the display configuration. Every attempt is printed with its NVAPI error code.
- **Signal check:** the log says whether the monitor receives its native resolution (the GPU scales, no bars) or the small one (the monitor scales and may add bars).
- **Configuration check:** after applying, the configuration is read back and compared (sizes, positions, scaling).
- **Visual confirmation** with automatic rollback.
- **Persistence check:** the resolution is looked up in the driver's custom resolution list, and again after the monitors are enabled.

## Requirements

- Windows 10 or 11, 64-bit
- NVIDIA GPU with an installed driver, and the **primary** display connected to it
- Administrator rights (both programs ask for them)
- For the GUI build: nothing to install, `build.bat` uses the C# compiler that ships with Windows (.NET Framework 4)

## Usage

### GUI

1. Run `build.bat` once to get `CustomRes.exe`, then start it and approve the UAC prompt.
2. Pick a preset or enter width, height and refresh rate. The language (English / Русский) can be switched in the window.
3. Leave **Stretch to the full screen** on, then press **Create and apply**.
4. Check the picture: **Keep** (Enter) or **Revert** (Esc). Nothing pressed for 15 seconds means revert.
5. **Restore monitors** enables the monitors again if the window was closed in the middle of a run.

### Console

Double-click `custom_res.bat`, approve the UAC prompt, choose the language (Enter = English) and type a resolution such as `1280x960@240`. The refresh rate is asked separately if it is omitted. `x`, a Cyrillic `х`, `×` and `*` are all accepted as the separator.

### Parameters of `custom_res.bat`

| Parameter | Meaning |
| --- | --- |
| `/en`, `/ru` | Interface language, no question is asked |
| `/restore` | Only enable the monitors that a previous run disabled (can be combined with `/ru`) |
| `/nodisable` | Do not disable the monitors (for troubleshooting) |
| `/nostretch` | Do not stretch: create a 1:1 mode and apply it through Windows, the monitor decides how to scale it |

## Settings

| Where | Name | Default | Meaning |
| --- | --- | --- | --- |
| `custom_res.bat` | `$TestSeconds` | `15` | Seconds to wait for confirmation before the automatic rollback |
| `custom_res.bat` | `$Stretch` | `$true` | Stretch to the full screen (`/nostretch` turns it off) |
| `CustomResCore.cs`, class `Options` | `SaveOutputOnly` | `0` | `1` = save the resolution only for this port/connector |
| `CustomResCore.cs`, class `Options` | `SaveMonitorOnly` | `0` | `1` = save only for a monitor with the same EDID |

## Troubleshooting

- **Black bars on the sides.** Look at the line `Signal to the monitor:` in the log. If it shows the small resolution (1:1), the monitor is doing the scaling: set **Adjust desktop size and position** to **Full-screen** and **Perform scaling on** to **GPU** in NVIDIA Control Panel, and check the aspect/scaling option in the monitor's own menu. In a game, also tick **Override the scaling mode set by games and programs**.
- **`NVAPI_ERROR` for everything.** Run with `/nodisable` to see whether the disabled monitors are the cause, and include the printed attempt log when you report an issue.
- **Monitors stay disabled.** Run `custom_res.bat /restore` or press **Restore monitors** in the GUI. On errors and on Ctrl+C they are enabled again automatically.
- **The screen is black after a test.** Wait: the automatic rollback restores the previous mode.

## Notes and limitations

- The stretching needs the NVIDIA display configuration API. If the driver rejects the configuration, the mode is applied 1:1 and the log says so.
- A 1:1 timing has to fit the cable and the monitor; very high pixel clocks (over 600 MHz) give a warning.
- NVAPI function IDs are taken from the public NvAPIWrapper table because NVIDIA does not publish them. A very old or very new driver may not expose them.
- Whether the mode survives re-enabling the monitors can depend on the driver version. The tool checks and reports it. If it does not survive, leave the monitors disabled when asked.

## How it works

`CustomResCore.cs` contains the engine: the translations, the CVT calculation, the NVAPI and Windows display calls, and the whole flow. The flow talks to the system through an `IPlatform` interface, which is how the logic was tested without a GPU. `CustomResGui.cs` is the WinForms front end. `custom_res.bat` carries a copy of the engine after the `##PSSTART##` marker, compiles it with `Add-Type` and uses it for the console dialog.

## Status

Experimental. The NVIDIA display configuration part is new; if something is rejected, the log shows the exact step and error code.

## Disclaimer

Use at your own risk. Changing display timings and scaling can leave a display showing nothing until the mode is rolled back. Try it on a setup where losing the picture for a few seconds is fine.