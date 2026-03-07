using System.Text.Json;
using System.Text.Json.Serialization;
using WebMusicPlayer.Models;

namespace WebMusicPlayer.Services;

public sealed class AppStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _stateFilePath = Path.Combine(FileSystem.AppDataDirectory, "app-state.json");

    public async Task<AppState> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new AppState();
            }

            await using var stream = File.OpenRead(_stateFilePath);
            var state = await JsonSerializer.DeserializeAsync<AppState>(stream, SerializerOptions);
            return state ?? new AppState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppState state)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
            await using var stream = File.Create(_stateFilePath);
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions);
        }
        finally
        {
            _gate.Release();
        }
    }
}
