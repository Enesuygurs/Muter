using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;
using Muter.Hotkeys;
using Muter.Localization;
using Muter.Properties;
using Muter.Tests.Fakes;
using Muter.UI;
using Muter.UI.Controls;
using Xunit;

namespace Muter.Tests
{
    // Every language must have every text, keep the {0} placeholders of English, and fit where it is shown.
    public sealed class LocalizationTests : IDisposable
    {
        private const TextFormatFlags RowFlags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public LocalizationTests() => TestSetup.Init();

        public void Dispose()
        {
            Strings.Current = Languages.English;
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MuterTests", throwOnMissingSubKey: false);
        }

        public static IEnumerable<object[]> AllLanguages => Languages.All.Select(l => new object[] { l.Code });

        private static IEnumerable<PropertyInfo> TextProperties =>
            typeof(Strings).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.PropertyType == typeof(string));

        private static IEnumerable<string> Placeholders(string text) =>
            Regex.Matches(text, @"\{\d+\}").Cast<Match>().Select(m => m.Value).OrderBy(v => v);

        private static Strings Language(string code) => Languages.All.Single(l => l.Code == code);

        [Theory]
        [MemberData(nameof(AllLanguages))]
        public void Every_text_is_translated_with_the_same_placeholders(string code)
        {
            Strings language = Language(code);
            foreach (PropertyInfo property in TextProperties)
            {
                string english = (string)property.GetValue(Languages.English);
                string translated = (string)property.GetValue(language);
                Assert.False(string.IsNullOrWhiteSpace(translated), $"{code}.{property.Name} is missing");
                Assert.True(Placeholders(english).SequenceEqual(Placeholders(translated)), $"{code}.{property.Name}: placeholders differ from English");
                Assert.Equal(translated.Trim(), translated);
            }
            Assert.NotNull(language.Culture);
        }

        [Fact]
        public void Codes_and_names_are_unique()
        {
            Assert.Equal(Languages.All.Count, Languages.All.Select(l => l.Code.ToLowerInvariant()).Distinct().Count());
            Assert.Equal(Languages.All.Count, Languages.All.Select(l => l.NativeName).Distinct().Count());
            Assert.True(Languages.All.Count >= 14);
        }

        [Theory]
        [InlineData("", "tr-TR", "tr")]
        [InlineData("", "de-AT", "de")]
        [InlineData("", "pt-PT", "pt-BR")]
        [InlineData("", "pt-BR", "pt-BR")]
        [InlineData("", "zh-CN", "zh-CN")]
        [InlineData("", "zh-SG", "zh-CN")]
        [InlineData("", "ja-JP", "ja")]
        [InlineData("", "uk-UA", "uk")]
        [InlineData("", "ar-SA", "en")] // not translated: English
        [InlineData("", "en-GB", "en")]
        [InlineData("de", "tr-TR", "de")] // a chosen language wins over Windows
        [InlineData("xx", "fr-FR", "fr")] // an unknown saved value follows Windows
        public void Resolves_the_chosen_or_the_Windows_language(string setting, string windows, string expected)
        {
            Assert.Equal(expected, Languages.Resolve(setting, CultureInfo.GetCultureInfo(windows)).Code);
        }

        [Fact]
        public void Display_times_use_the_language_number_format()
        {
            Strings.Current = Languages.Turkish;
            Assert.Equal("0,5 saniye", TrayApplicationContext.DisplayTimeLabel(500));
            Assert.Equal("1 saniye", TrayApplicationContext.DisplayTimeLabel(1000));
            Strings.Current = Languages.English;
            Assert.Equal("0.5 seconds", TrayApplicationContext.DisplayTimeLabel(500));
        }

        [Theory]
        [MemberData(nameof(AllLanguages))]
        public void Overlay_texts_fit_the_overlay(string code)
        {
            Strings language = Language(code);
            using (var font = new Font("Segoe UI Semibold", 13.333f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                foreach (string text in new[] { language.Microphone, language.AllMicrophones, language.StateOn, language.StateOff, language.StateNoDevice, language.StateUnknown, language.DragToMove })
                    Assert.True(TextRenderer.MeasureText(text, font).Width <= 122, $"{code}: \"{text}\" is wider than the overlay");
            }
        }

        // Builds the settings window in the language and measures every text against the space it is drawn in.
        [Theory]
        [MemberData(nameof(AllLanguages))]
        public void Settings_window_texts_fit(string code)
        {
            TestSetup.RunSta(() =>
            {
                var provider = new FakeAudioEndpointProvider();
                provider.Add("a");
                provider.SetDefault("a");
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F9;
                settings.language = code;
                using (var app = new TrayApplicationContext(settings, () => provider, new FakeGlobalHotkey(),
                    new StartupRegistration(Application.ExecutablePath, @"Software\MuterTests\Lang"), TestSetup.Silent))
                {
                    app.WaitForAudio();
                    SettingsForm form = app.OpenSettings();
                    Application.DoEvents();
                    var problems = new List<string>();
                    foreach (Control control in All(form))
                    {
                        switch (control)
                        {
                            case SettingRow row:
                                CheckRow(row, problems);
                                break;
                            case NavButton nav:
                                if (TextRenderer.MeasureText(nav.Text, nav.Font).Width > nav.Width - 44)
                                    problems.Add($"menu \"{nav.Text}\"");
                                break;
                            case FlatButton button when !string.IsNullOrEmpty(button.Text) && button.Font.Name != Theme.IconFontFamily:
                                if (TextRenderer.MeasureText(button.Text, button.Font).Width > button.Width - 12)
                                    problems.Add($"button \"{button.Text}\"");
                                break;
                            case SegmentedControl segments:
                                foreach (string segment in segments.Segments)
                                {
                                    if (TextRenderer.MeasureText(segment, segments.Font).Width > segments.Width / segments.Segments.Count - 8)
                                        problems.Add($"size \"{segment}\"");
                                }
                                break;
                            case DropdownButton dropdown:
                                foreach (DropdownOption option in dropdown.Options)
                                {
                                    if (TextRenderer.MeasureText(option.Text, dropdown.Font).Width > dropdown.Width - 38)
                                        problems.Add($"choice \"{option.Text}\"");
                                }
                                break;
                        }
                    }
                    // Done replaces Move while dragging.
                    form.MovePositionsButton.PerformClick();
                    Application.DoEvents();
                    CheckRow((SettingRow)form.MovePositionsButton.Parent.Parent, problems);
                    form.MovePositionsButton.PerformClick();
                    Assert.True(problems.Count == 0, code + " does not fit: " + string.Join("; ", problems));
                    form.Close();
                }
            });
        }

        private static void CheckRow(SettingRow row, List<string> problems)
        {
            float s = row.DeviceDpi / 96f;
            int available = (row.Setting != null ? row.Setting.Left - (int)(12 * s) : row.Width - (int)(16 * s)) - (int)(16 * s);
            using (var title = new Font(row.Font.FontFamily, row.Font.SizeInPoints, FontStyle.Regular))
            using (var description = new Font(row.Font.FontFamily, row.Font.SizeInPoints * 0.88f))
            {
                if (TextRenderer.MeasureText(row.Title, title, Size.Empty, RowFlags).Width > available)
                    problems.Add($"title \"{row.Title}\"");
                if (TextRenderer.MeasureText(row.Description, description, Size.Empty, RowFlags).Width > available)
                    problems.Add($"hint \"{row.Description}\"");
            }
        }

        [Theory]
        [MemberData(nameof(AllLanguages))]
        public void Hotkey_dialog_texts_fit(string code)
        {
            Strings language = Language(code);
            TestSetup.RunSta(() =>
            {
                Strings.Current = language;
                try
                {
                    using (var form = new HotkeyForm(Hotkey.Default, _ => null))
                    {
                        Label description = Find<Label>(form, "descriptionLabel");
                        Label status = Find<Label>(form, "statusLabel");
                        FlatButton reset = Find<FlatButton>(form, "resetButton");
                        string longKey = HotkeyText.Format(new Hotkey(Keys.PageDown, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift));
                        var problems = new List<string>();
                        if (!FitsLabel(description, description.Text))
                            problems.Add("description");
                        foreach (string message in new[]
                        {
                            language.HotkeyIsCurrent, language.HotkeyCurrent, language.HotkeyPending, language.HotkeyInvalidKey, language.HotkeyChooseValid,
                            string.Format(language.HotkeyPressSave, longKey), string.Format(language.HotkeyExample, longKey),
                            string.Format(language.HotkeyReserved, longKey), string.Format(language.HotkeyNeedsCtrlOrAlt, longKey),
                            string.Format(language.HotkeyTaken, longKey),
                        })
                        {
                            if (!FitsLabel(status, message))
                                problems.Add($"\"{message}\"");
                        }
                        if (TextRenderer.MeasureText(reset.Text, reset.Font).Width > reset.Width)
                            problems.Add("reset link");
                        foreach (string name in new[] { "cancelButton", "saveButton" })
                        {
                            FlatButton button = Find<FlatButton>(form, name);
                            if (TextRenderer.MeasureText(button.Text, button.Font).Width > button.Width - 12)
                                problems.Add(name);
                        }
                        Assert.True(problems.Count == 0, code + " does not fit: " + string.Join("; ", problems));
                    }
                }
                finally
                {
                    Strings.Current = Languages.English;
                }
            });
        }

        private static bool FitsLabel(Label label, string text)
        {
            Size size = TextRenderer.MeasureText(text, label.Font, new Size(label.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return size.Height <= label.Height && size.Width <= label.Width;
        }

        private static T Find<T>(Form form, string name) where T : Control => (T)All(form).Single(c => c.Name == name);

        private static IEnumerable<Control> All(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in All(child))
                    yield return descendant;
            }
        }

        [Fact]
        public void Changing_the_language_updates_the_menu_the_overlay_and_reopens_the_settings_window()
        {
            TestSetup.RunSta(() =>
            {
                var provider = new FakeAudioEndpointProvider();
                provider.Add("a");
                provider.SetDefault("a");
                var settings = new Settings();
                settings.Reset();
                settings.hotkeyKey = (int)Keys.F9;
                using (var app = new TrayApplicationContext(settings, () => provider, new FakeGlobalHotkey(),
                    new StartupRegistration(Application.ExecutablePath, @"Software\MuterTests\Lang"), TestSetup.Silent))
                {
                    app.WaitForAudio();
                    Assert.Equal("en", Strings.Current.Code); // Windows language (en-US in the tests)

                    SettingsForm form = app.OpenSettings();
                    form.ShowPage(SettingsForm.PageOverlay);
                    Application.DoEvents();
                    form.LanguageDropdown.Choose("tr");
                    TestSetup.PumpUntil(() => app.SettingsWindow != null && app.SettingsWindow != form);

                    Assert.Equal("tr", app.Settings.language);
                    Assert.Equal("tr", Strings.Current.Code);
                    Assert.True(form.IsDisposed);
                    SettingsForm reopened = app.SettingsWindow;
                    Assert.Equal(SettingsForm.PageOverlay, reopened.CurrentPage);
                    Assert.Equal("Muter Ayarları", reopened.Text);
                    Assert.Equal("tr", reopened.LanguageDropdown.SelectedValue);

                    app.RebuildMenu();
                    Assert.Equal(new[] { "Cihazlar", "Ayarlar...", "Çıkış" }, app.Menu.Items.OfType<ToolStripMenuItem>().Select(i => i.Text).ToArray());
                    Assert.EndsWith(": AÇIK", app.TrayIcon.Text);
                    Assert.Equal("AÇIK", app.OverlayWindow.Presentation.StateText);
                    Assert.Equal("Mikrofon", app.OverlayWindow.Presentation.Title);

                    reopened.LanguageDropdown.Choose(Languages.System);
                    TestSetup.PumpUntil(() => app.SettingsWindow != null && app.SettingsWindow != reopened);
                    Assert.Equal(string.Empty, app.Settings.language);
                    Assert.Equal("en", Strings.Current.Code);
                    app.SettingsWindow.Close();
                }
            });
        }
    }
}
