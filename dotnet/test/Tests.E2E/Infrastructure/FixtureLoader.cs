using System.Text.Json;

namespace Tests.E2E.Infrastructure;

/// <summary>
/// Loads JSON fixtures from the Fixtures directory (copied next to the test assembly).
/// </summary>
internal static class FixtureLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<T> LoadAsync<T>(string relativePath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);
        await using var stream = File.OpenRead(fullPath);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, Options);
        return result ?? throw new InvalidOperationException(
            $"Fixture '{relativePath}' deserialized to null.");
    }
}
