# Muter - Microphone Mute Overlay

Muter is a simple yet powerful Windows utility that provides a clean, unobtrusive overlay to show your current microphone status. Never again wonder if your mic is hot! Toggle your microphone with a customizable global hotkey, making it perfect for conference calls, streaming, or online gaming.

![muter](https://github.com/user-attachments/assets/28661ee3-783b-4603-96c3-6aba466711bb)

## Features

-   **Visual Overlay:** A minimalist overlay instantly shows whether your microphone is **ON**, **OFF**, or **disconnected**.
-   **Overlay Settings:** Choose the position (corners, center, or drag it anywhere), the size, how long it stays on screen (or keep it always visible), and the monitor (primary, a specific one, or the one under the mouse).
-   **Mini Indicator:** An optional small, always-visible status square (green = ON, red = OFF) that never gets in the way of your clicks.
-   **Global Hotkey:** Mute or unmute your microphone from any application with a customizable key combination. The default is `Ctrl + Shift + F9` (users of version 1.0 keep their hotkey).
-   **Device Selection:** Control the default devices, one specific microphone, or all microphones at once, with optional syncing so they always share the same state.
-   **System Tray Icon:** The app sits quietly in your system tray, reflecting the current mic status and providing quick access to settings.
-   **Lightweight:** Designed to use minimal system resources, so it won't impact your performance.
-   **Run on Startup:** Easily configure Muter to launch automatically when you start Windows.

## How to Use

No installation is required.

1.  Go to the **[Releases](https://github.com/Enesuygurs/Muter/releases)** page.
2.  Download the zip file (for example `Muter.v1.1.zip`) from the latest release.
3.  Run the application. An icon will appear in your system tray.
4.  Press the hotkey or left-click the tray icon to mute or unmute.
5.  Right-click the tray icon and choose Settings to change the microphones, the hotkey and the overlay.

### Tray menu and settings

Right-click the tray icon to switch between microphones (**Devices**) or to open **Settings**. Changes in the settings window apply immediately.

| Page | Settings |
|---|---|
| **General** | **Microphones**: **Default (System)** controls the default recording device *and* the default communications device (call apps often use the latter); **All Devices** controls every microphone; or pick a single one. **Keep in sync** keeps every controlled microphone in the same state, even when one is changed elsewhere (e.g. in Windows sound settings). **Mute hotkey**: press a new key combination and choose Save. Combinations that would block typing in other apps (such as `Shift + F`) are not accepted, and if another application already uses a combination, Muter tells you and keeps the old one. **Screen**: the monitor for the overlay and the mini indicator, and **Positions** (Move lets you drag both windows anywhere, Reset puts them back; a dragged window stays on the monitor it was dropped on). **Start with Windows**. |
| **Overlay** | Show overlay, position, size and display time. |
| **Mini indicator** | Show it, position and size. |

### What the states mean

Muter only shows **OFF** when it has read back every controlled microphone from Windows and all of them are muted.

-   **ON**: at least one controlled microphone is live.
-   **OFF**: every controlled microphone is verified muted.
-   **No Device**: the selected microphone is not connected.
-   **UNKNOWN**: a microphone could not be read, or the Windows audio system is not responding. Treat it as **ON**.

Changes made by other applications are picked up immediately, with a re-check every second as a safety net.

### Troubleshooting

Muter writes a log to `%LOCALAPPDATA%\Muter\muter.log` (state changes, hotkey problems, device errors). Only one copy of Muter can run at a time.

## Building

Requires Windows and the .NET SDK (the app targets .NET Framework 4.7.2, which is part of Windows 10/11).

```
dotnet build Muter.sln -c Release
dotnet test Muter.sln
```

Note: the tests in `Muter.Tests/Hardware` mute and unmute the real microphones of the computer they run on (the original states are restored afterwards), and some UI tests briefly show windows and move the mouse.

## Built With

This project was built using:
* **C#** with **.NET Framework**
* **Windows Forms** for the user interface
* The **Windows Core Audio API** (MMDevice) for microphone control

## Acknowledgements

* The icons used in this application are provided by **[Icons8](https://icons8.com)**.

## License

This project is licensed under the MIT License. See the [`LICENSE.txt`](LICENSE.txt) file for details.
