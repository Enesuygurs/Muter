using System.Globalization;

namespace Muter.Localization
{
    // Every text the user sees, in one language. Languages.All lists the translations; English is the reference and a
    // test checks that every translation has every text with the same {0} placeholders.
    internal sealed class Strings
    {
        private static Strings current;

        // The language in use (English until set). Set from the settings at start and when the language is changed.
        public static Strings Current
        {
            get => current ?? Languages.English;
            set => current = value;
        }

        public string Code { get; set; }
        public string NativeName { get; set; }

        public CultureInfo Culture => CultureInfo.GetCultureInfo(Code);

        // Application
        public string AlreadyRunning { get; set; }
        public string StartupChangeFailed { get; set; }
        public string HotkeyNotActiveTitle { get; set; }
        public string HotkeyNotActiveText { get; set; }

        // Tray menu and devices
        public string MenuDevices { get; set; }
        public string MenuSettings { get; set; }
        public string MenuExit { get; set; }
        public string DeviceDefault { get; set; }
        public string DeviceDefaultTip { get; set; }
        public string DeviceAll { get; set; }
        public string DeviceFallback { get; set; }
        public string DeviceDisconnected { get; set; }
        public string DevicesListFailed { get; set; }
        public string DevicesNoneActive { get; set; }

        // Microphone state (overlay, mini indicator, tray tooltip)
        public string Microphone { get; set; }
        public string AllMicrophones { get; set; } // overlay title, must be short
        public string StateOn { get; set; }
        public string StateOff { get; set; }
        public string StateNoDevice { get; set; }
        public string StateUnknown { get; set; }
        public string TipPartlyLive { get; set; }
        public string TipUnknown { get; set; }
        public string TipNotResponding { get; set; }
        public string DragToMove { get; set; }

        // Positions, sizes, times and monitors
        public string AnchorTopLeft { get; set; }
        public string AnchorTopCenter { get; set; }
        public string AnchorTopRight { get; set; }
        public string AnchorCenter { get; set; }
        public string AnchorLowerCenter { get; set; }
        public string AnchorBottomLeft { get; set; }
        public string AnchorBottomCenter { get; set; }
        public string AnchorBottomRight { get; set; }
        public string AnchorCustom { get; set; }
        public string SizeSmall { get; set; }
        public string SizeMedium { get; set; }
        public string SizeLarge { get; set; }
        public string OneSecond { get; set; }
        public string Seconds { get; set; }
        public string AlwaysVisible { get; set; }
        public string MonitorPrimary { get; set; }
        public string MonitorUnderMouse { get; set; }
        public string DisplayNumber { get; set; }

        // Settings window
        public string SettingsTitle { get; set; }
        public string SettingsWindowTitle { get; set; }
        public string PageGeneral { get; set; }
        public string PageHotkey { get; set; }
        public string PageOverlay { get; set; }
        public string PageMiniIndicator { get; set; }
        public string SectionMicrophone { get; set; }
        public string SectionStartup { get; set; }
        public string SectionScreen { get; set; }
        public string SectionLanguage { get; set; }
        public string SectionHotkey { get; set; }
        public string SectionSound { get; set; }
        public string Microphones { get; set; }
        public string MicrophonesHint { get; set; }
        public string KeepInSync { get; set; }
        public string KeepInSyncHint { get; set; }
        public string StartWithWindows { get; set; }
        public string StartWithWindowsHint { get; set; }
        public string Monitor { get; set; }
        public string MonitorHint { get; set; }
        public string Positions { get; set; }
        public string PositionsMove { get; set; }
        public string PositionsDone { get; set; }
        public string PositionsReset { get; set; }
        public string PositionsHint { get; set; }
        public string PositionsHintMoving { get; set; }
        public string PositionsHintNothingShown { get; set; }
        public string Language { get; set; }
        public string LanguageHint { get; set; }
        public string LanguageSystem { get; set; }
        public string MuteHotkey { get; set; }
        public string MuteHotkeyHint { get; set; }
        public string Change { get; set; }
        public string PushToTalk { get; set; }
        public string PushToTalkHint { get; set; }
        public string SoundFeedback { get; set; }
        public string SoundFeedbackHint { get; set; }
        public string ShowOverlay { get; set; }
        public string ShowOverlayHint { get; set; }
        public string Position { get; set; }
        public string PositionHint { get; set; }
        public string Size { get; set; }
        public string DisplayTime { get; set; }
        public string DisplayTimeHint { get; set; }
        public string ShowMiniIndicator { get; set; }
        public string ShowMiniIndicatorHint { get; set; }

        // Hotkey dialog and hotkey problems
        public string HotkeyWindowTitle { get; set; }
        public string HotkeyDescription { get; set; }
        public string ResetToDefault { get; set; }
        public string Cancel { get; set; }
        public string Save { get; set; }
        public string HotkeyPlaceholder { get; set; }
        public string HotkeyIsCurrent { get; set; }
        public string HotkeyCurrent { get; set; }
        public string HotkeyPressSave { get; set; }
        public string HotkeyExample { get; set; }
        public string HotkeyPending { get; set; }
        public string HotkeyInvalidKey { get; set; }
        public string HotkeyReserved { get; set; }
        public string HotkeyNeedsCtrlOrAlt { get; set; }
        public string HotkeyChooseValid { get; set; }
        public string HotkeyNotConfigured { get; set; }
        public string HotkeyTaken { get; set; }
        public string HotkeyRegisterFailed { get; set; }
    }
}
