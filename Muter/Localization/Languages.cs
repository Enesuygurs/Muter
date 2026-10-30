using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Muter.Localization
{
    // The available languages. Each translation is in its own Languages.<name>.cs file.
    internal static partial class Languages
    {
        // The language setting value that follows the Windows display language.
        public const string System = "";

        private static IReadOnlyList<Strings> all;

        public static IReadOnlyList<Strings> All => all ?? (all = new[]
        {
            English, Turkish, German, French, Spanish, Italian, PortugueseBrazil, Russian, Polish, Dutch, Ukrainian,
            Japanese, ChineseSimplified, Korean,
        });

        // The language for a setting value: an exact code, or for System (or an unknown code) the Windows display
        // language, falling back to English.
        public static Strings Resolve(string code, CultureInfo windowsLanguage = null)
        {
            if (!string.IsNullOrEmpty(code))
            {
                Strings chosen = All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
                if (chosen != null)
                    return chosen;
            }
            return ForCulture(windowsLanguage ?? CultureInfo.CurrentUICulture);
        }

        private static Strings ForCulture(CultureInfo culture)
        {
            for (CultureInfo c = culture; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
            {
                Strings exact = All.FirstOrDefault(l => string.Equals(l.Code, c.Name, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return exact;
            }
            // Same language, another region or script (e.g. pt-PT, zh-Hans).
            return All.FirstOrDefault(l => string.Equals(l.Code.Split('-')[0], culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                ?? English;
        }

        public static readonly Strings English = new Strings
        {
            Code = "en",
            NativeName = "English",

            AlreadyRunning = "Muter is already running. Look for its icon in the system tray.",
            StartupChangeFailed = "Could not change the startup setting:\n{0}",
            HotkeyNotActiveTitle = "Hotkey is not active",
            HotkeyNotActiveText = "{0} Open Settings from the tray icon to pick another.",

            MenuDevices = "Devices",
            MenuSettings = "Settings...",
            MenuExit = "Exit",
            DeviceDefault = "Default (System)",
            DeviceDefaultTip = "The default recording device and the default communications device.",
            DeviceAll = "All Devices",
            DeviceFallback = "Device",
            DeviceDisconnected = "{0} (Disconnected)",
            DevicesListFailed = "(Could not list the microphones)",
            DevicesNoneActive = "(No active devices found)",

            Microphone = "Microphone",
            AllMicrophones = "All Devices",
            StateOn = "ON",
            StateOff = "OFF",
            StateNoDevice = "No Device",
            StateUnknown = "UNKNOWN",
            TipPartlyLive = "ON ({0} of {1} live)",
            TipUnknown = "state unknown, assume ON",
            TipNotResponding = "audio system not responding, assume ON",
            DragToMove = "Drag to move",

            AnchorTopLeft = "Top left",
            AnchorTopCenter = "Top center",
            AnchorTopRight = "Top right",
            AnchorCenter = "Center",
            AnchorLowerCenter = "Lower center (default)",
            AnchorBottomLeft = "Bottom left",
            AnchorBottomCenter = "Bottom center",
            AnchorBottomRight = "Bottom right",
            AnchorCustom = "Custom (dragged)",
            SizeSmall = "Small",
            SizeMedium = "Medium",
            SizeLarge = "Large",
            OneSecond = "1 second",
            Seconds = "{0} seconds",
            AlwaysVisible = "Always visible",
            MonitorPrimary = "Primary monitor",
            MonitorUnderMouse = "Monitor under the mouse",
            DisplayNumber = "Display {0}",

            SettingsTitle = "Settings",
            SettingsWindowTitle = "Muter Settings",
            PageGeneral = "General",
            PageHotkey = "Hotkey",
            PageOverlay = "Overlay",
            PageMiniIndicator = "Mini indicator",
            SectionMicrophone = "Microphone",
            SectionStartup = "Startup",
            SectionScreen = "Screen",
            SectionLanguage = "Language",
            SectionHotkey = "Hotkey",
            SectionSound = "Sound",
            Microphones = "Microphones",
            MicrophonesHint = "Which microphones the hotkey mutes",
            KeepInSync = "Keep in sync",
            KeepInSyncHint = "Controlled microphones always share the same state",
            StartWithWindows = "Start with Windows",
            StartWithWindowsHint = "Run Muter when you sign in",
            Monitor = "Monitor",
            MonitorHint = "For the overlay and mini indicator",
            Positions = "Positions",
            PositionsMove = "Move",
            PositionsDone = "Done",
            PositionsReset = "Reset",
            PositionsHint = "Drag the overlay and the mini indicator anywhere",
            PositionsHintMoving = "Drag them now, then click Done",
            PositionsHintNothingShown = "Turn on the overlay or the mini indicator first",
            Language = "Language",
            LanguageHint = "The language Muter is shown in",
            LanguageSystem = "Windows language",
            MuteHotkey = "Mute hotkey",
            MuteHotkeyHint = "Mutes and unmutes from any app",
            Change = "Change",
            PushToTalk = "Push to talk",
            PushToTalkHint = "Hold the hotkey to talk; muted again when you let go",
            SoundFeedback = "Sound feedback",
            SoundFeedbackHint = "A short sound when the microphone is turned on or off",
            ShowOverlay = "Show overlay",
            ShowOverlayHint = "Appears when the microphone is turned on or off",
            Position = "Position",
            PositionHint = "Choose Custom to drag it anywhere",
            Size = "Size",
            DisplayTime = "Display time",
            DisplayTimeHint = "How long it stays before fading out",
            ShowMiniIndicator = "Show mini indicator",
            ShowMiniIndicatorHint = "A small, always-visible status square",

            HotkeyWindowTitle = "Muter Hotkey",
            HotkeyDescription = "Press the keys you want to use to mute and unmute your microphone from any app.",
            ResetToDefault = "Reset to default",
            Cancel = "Cancel",
            Save = "Save",
            HotkeyPlaceholder = "Press a key combination",
            HotkeyIsCurrent = "This is your current hotkey. Press another combination to change it.",
            HotkeyCurrent = "Current hotkey. Press a new combination to change it.",
            HotkeyPressSave = "Press Save to use {0}.",
            HotkeyExample = "Press a key combination, for example {0}.",
            HotkeyPending = "Now press the key to combine with it.",
            HotkeyInvalidKey = "That key can't be used as a hotkey.",
            HotkeyReserved = "{0} is reserved by Windows. Try another combination.",
            HotkeyNeedsCtrlOrAlt = "Add Ctrl or Alt: {0} would stop working for typing in other apps.",
            HotkeyChooseValid = "Please choose a valid key combination.",
            HotkeyNotConfigured = "No hotkey is configured.",
            HotkeyTaken = "{0} is already used by another app. Try another combination.",
            HotkeyRegisterFailed = "{0} could not be registered: {1}",
        };
    }
}
