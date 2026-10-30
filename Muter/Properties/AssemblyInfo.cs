using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Do not change AssemblyCompany/AssemblyProduct or the namespace of Program: Windows derives the
// user.config folder (%LOCALAPPDATA%\Muter\...) from them, and changing it would lose saved settings.
[assembly: AssemblyTitle("Muter")]
[assembly: AssemblyDescription("Microphone mute overlay with a global hotkey")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("Muter")]
[assembly: AssemblyCopyright("Copyright © 2024-2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("a96241f9-3d3e-484a-b3b5-1e57202b1b6d")]

// Bumping the version moves user.config to a new folder; Program migrates the old values with Settings.Upgrade().
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

[assembly: InternalsVisibleTo("Muter.Tests")]
