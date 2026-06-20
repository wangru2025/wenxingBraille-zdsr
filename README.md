# Wenxing Braille ZDSR Add-in

ZDSR braille display add-in for Wenxing / CBP 40-cell braille displays.

This project implements the Wenxing device protocol directly through WinUSB. It does not include Sunshine Screen Reader files, StarLibDriver.dll, or proprietary driver binaries.

## Notice

This is an unofficial compatibility add-in. It is not affiliated with, endorsed by, or supported by the Wenxing / CBP device vendor, Sunshine Screen Reader, ZDSR, or NV Access.

The installer only installs this add-in. It does not include or distribute Sunshine Screen Reader files, `StarLibDriver.dll`, ZDSR binaries, NVDA binaries, or other proprietary components. Users must obtain ZDSR, the braille display, and any required drivers through lawful channels and comply with their respective licenses.

## Requirements

- ZDSR with `ZDSRBrailleDisplayAddin.dll` installed.
- .NET Framework 4.x runtime.
- Inno Setup 6 for building the installer.
- A Wenxing / CBP compatible braille display using the WinUSB interface.

## Build

Run:

```powershell
.\build.ps1
```

The add-in DLL is written to `dist\app\wenxingBraille.dll`.

To build the installer:

```powershell
.\build-installer.ps1
```

The setup package is written to `dist\wenxingBraille-zdsr-1.0.2-Setup.exe`.

## Installation

The installer copies the add-in to:

```text
{app}\addins\BrailleDisplay\wenxingBraille
```

By default `{app}` is `C:\Program Files (x86)\zdsr\zdsr`.

## License

MIT
