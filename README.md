# Temp Monitor

A tiny, no-install temperature monitor for Windows 10 and 11. It opens a window that shows live temperatures from every sensor it can find: CPU cores and package, GPU, NVMe/SATA drives, motherboard, and DDR5 memory where supported.

- **One file, ~1.7 MB.** No installer and no runtime to download. It uses .NET Framework 4.8, which is already part of Windows 10 (1903+) and Windows 11.
- **Live readings** every second, with Current / Min / Max per sensor, grouped by hardware.
- **Colour warnings:** rows turn orange at 70 °C and red at 85 °C.
- **°C or °F**, always-on-top mode, reset min/max, and copy readings to the clipboard.
- Remembers its window position and settings.

## Download

Get `TempMonitor.exe` from the [Releases](../../releases) page and run it. There's nothing to install; you can keep the file anywhere.

Each release also lists the file's SHA-256 checksum in `SHA256SUMS.txt`.

### First run

1. **Windows asks for administrator permission.** Click **Yes**. Reading CPU and motherboard sensors requires low-level hardware access, so every hardware monitor (HWiNFO, HWMonitor, LibreHardwareMonitor) needs admin rights.
2. **SmartScreen may warn about an unrecognized app** because the exe isn't code-signed. Click **More info → Run anyway**.
3. **CPU temperatures need the PawnIO driver.** If it isn't installed, a yellow bar offers to install it. Temp Monitor downloads the official PawnIO installer, checks it against a known checksum, and runs it. GPU and drive temperatures work without it.

## About PawnIO

[PawnIO](https://pawnio.eu) is a signed, open-source driver that lets programs read CPU and motherboard sensors safely. It replaces the old WinRing0 driver, which Windows Defender now flags as vulnerable. LibreHardwareMonitor and other monitoring tools use it too. You can uninstall it any time from **Settings → Apps**.

## Requirements

- Windows 10 version 1903 or newer, or Windows 11
- 64-bit (x64) PC
- Administrator rights

## Command line

Write a one-off snapshot of all sensors to a text file, which is useful when reporting a problem:

```bash
TempMonitor.exe --dump temps.txt
```

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer. It builds a .NET Framework 4.8 exe, so the .NET runtime is not needed to run it.

```bash
dotnet build -c Release
```

Output: `bin/Release/net48/TempMonitor.exe`

### Releases

Pushing a tag such as `v1.2.0` runs the GitHub Actions workflow in `.github/workflows/build.yml`. It builds the exe with that version number and publishes a GitHub Release with the exe and its checksum.

```bash
git tag v1.0.0
```

```bash
git push origin v1.0.0
```

## Project layout

| File | Purpose |
|---|---|
| `MainForm.cs` | Window, menus, PawnIO banner, display |
| `Sensors.cs` | Reads temperature sensors through LibreHardwareMonitorLib |
| `PawnIo.cs` | Detects PawnIO and downloads/verifies/runs its installer |
| `Settings.cs` | Saves preferences in `HKCU\Software\TempMonitor` |
| `Program.cs` | Entry point, error handling, `--dump` |
| `app.manifest` | Admin rights, DPI awareness, Windows 10/11 compatibility |

## License

MIT. See [LICENSE](LICENSE). Sensor access is provided by [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for all bundled components.
