using System.Text.Json;

namespace MyDock.Core.Docker;

/// <summary>A start/stop/restart request that the daemon rejected, carrying a message fit for display.</summary>
public sealed class DockerOperationException(string message, Exception inner) : Exception(message, inner)
{
    /// <summary>Extracts the text from the daemon's <c>{"message": "..."}</c> error body.</summary>
    internal static string Describe(string? responseBody, string fallback)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return fallback;

        try
        {
            using var json = JsonDocument.Parse(responseBody);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return responseBody.Trim();
    }
}
