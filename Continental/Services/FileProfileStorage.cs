using Continental.Shared.Services;

namespace Continental.Services;

public sealed class FileProfileStorage : IProfileStorage
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static string Folder => FileSystem.AppDataDirectory;

    private static string Main => Path.Combine(Folder, "profile.json");

    private static string Backup => Path.Combine(Folder, "profile.bak.json");

    public bool IsSupported => true;

    public async Task<string?> LoadAsync()
    {
        await _gate.WaitAsync();

        try
        {
            foreach (var path in new[] { Main, Backup })
            {
                if (!File.Exists(path))
                    continue;

                var json = await File.ReadAllTextAsync(path);

                if (!string.IsNullOrWhiteSpace(json))
                    return json;
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(string json)
    {
        await _gate.WaitAsync();

        try
        {
            Directory.CreateDirectory(Folder);

            var temp = Main + ".tmp";
            await File.WriteAllTextAsync(temp, json);

            if (File.Exists(Main))
                File.Copy(Main, Backup, overwrite: true);

            File.Move(temp, Main, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
