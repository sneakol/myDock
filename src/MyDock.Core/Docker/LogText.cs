using System.Text.RegularExpressions;

namespace MyDock.Core.Docker;

public static partial class LogText
{
    /// <summary>Appends a log chunk, drops terminal color codes and keeps only the last <paramref name="maxLines"/> lines.</summary>
    public static string Append(string current, string chunk, int maxLines)
    {
        var text = current + Ansi().Replace(chunk, "");

        var lines = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            // A trailing newline ends the last line rather than starting an empty one.
            if (text[i] == '\n' && i != text.Length - 1 && ++lines == maxLines)
                return text[(i + 1)..];
        }

        return text;
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();
}
