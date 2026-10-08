using System;
using System.IO;
using System.Threading.Tasks;
using LightBulb.Services;
using Xunit;

namespace LightBulb.Core.Tests;

public class ForkIdentitySpecs
{
    [Fact]
    public void Fork_has_distinct_identity_and_disables_upstream_binary_updates()
    {
        Assert.Equal("LightBulb.Fork", Program.Name);
        Assert.Contains("(upstream ", Program.VersionString);
        Assert.False(StartOptions.Parse([]).IsAutoUpdateAllowed);
    }

    [Fact]
    public void Original_settings_directory_is_rejected_before_any_load_or_save()
    {
        var original = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LightBulb"
        );
        Assert.Throws<InvalidOperationException>(() =>
            StartOptions.ValidateSettingsPath(Path.Combine(original, "Settings.json"))
        );
        Assert.Throws<InvalidOperationException>(() =>
            StartOptions.ValidateSettingsPath(Path.Combine(original, "nested", "Settings.json"))
        );
        var fork = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LightBulb.Fork",
            "Settings.json"
        );
        Assert.Equal(fork, StartOptions.ValidateSettingsPath(fork));
    }

    [Fact]
    public async Task Binary_update_service_returns_no_update_even_when_setting_is_enabled()
    {
        using var updater = new UpdateService(new SettingsService { IsAutoUpdateEnabled = true });
        Assert.Null(await updater.CheckForUpdatesAsync());
    }
}
