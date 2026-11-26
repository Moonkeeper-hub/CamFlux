using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using CamFlux.Core;

namespace CamFlux.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class ProfileStore(string directory)
{
    private sealed record Entry(Profile Profile, byte[] Publishing);
    private string FilePath(Guid id) => Path.Combine(directory, id.ToString("N") + ".json");

    public IEnumerable<(Guid Id, string Name)> List()
    {
        if (!Directory.Exists(directory))
            yield break;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id))
                continue;
            Entry? entry;
            try { entry = JsonSerializer.Deserialize<Entry>(File.ReadAllBytes(file)); }
            catch (JsonException) { continue; }
            catch (IOException) { continue; }
            if (entry?.Profile is { } profile)
                yield return (id, profile.Name);
        }
    }

    public (Profile Profile, Credentials Publishing) Load(Guid id)
    {
        var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllBytes(FilePath(id)))
            ?? throw new InvalidDataException("ProfileError");
        var bytes = ProtectedData.Unprotect(entry.Publishing, null, DataProtectionScope.CurrentUser);
        try
        {
            return (entry.Profile, JsonSerializer.Deserialize<Credentials>(bytes)
                ?? throw new InvalidDataException("ProfileError"));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(Guid id, Profile profile, Credentials publishing)
    {
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(publishing);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var entry = new Entry(profile, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            using (var stream = new FileStream(temporary, FileMode.CreateNew))
            {
                JsonSerializer.Serialize(stream, entry);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath(id), overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            File.Delete(temporary);
        }
    }
}
