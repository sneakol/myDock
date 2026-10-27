using System.Runtime.CompilerServices;
using MyDock.Core.Localization;

namespace MyDock.Core.Tests;

internal static class TestSetup
{
    // The tests read the English texts, whatever language the machine runs in.
    [ModuleInitializer]
    internal static void UseEnglish() => Loc.Use("en");
}
