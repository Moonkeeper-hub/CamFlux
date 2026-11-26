using CamFlux.Core;
using CamFlux.Infrastructure;

namespace CamFlux.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows DPAPI is required.";
    }
}

public sealed class ProfileStoreTests
{
    [WindowsFact]
    public void PublishingCredentialsRoundTripWithoutPlaintextInProfile()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Directory.CreateTempSubdirectory("camflux-profile-test-");
        try
        {
            var store = new ProfileStore(directory.FullName);
            var id = Guid.NewGuid();
            var profile = new Profile("Testing", new Endpoint("example.invalid"), [], "en", null);
            var credentials = new Credentials("example-publisher", "example-password");
            store.Save(id, profile, credentials);
            store.Save(id, profile with { Language = "ru" }, credentials);
            var file = Assert.Single(directory.GetFiles());
            var contents = File.ReadAllText(file.FullName);
            Assert.DoesNotContain(credentials.User, contents);
            Assert.DoesNotContain(credentials.Password, contents);
            var restored = store.Load(id);
            Assert.Equal(credentials, restored.Publishing);
            Assert.Equal(profile.Endpoint, restored.Profile.Endpoint);
            Assert.Equal("ru", restored.Profile.Language);
            Assert.Equal((id, profile.Name), Assert.Single(store.List()));
        }
        finally { directory.Delete(true); }
    }
}
