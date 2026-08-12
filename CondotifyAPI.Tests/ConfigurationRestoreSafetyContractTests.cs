using CondotifyAPI.Data.Backups;

namespace CondotifyAPI.Tests;

public sealed class ConfigurationRestoreSafetyContractTests
{
    [Fact]
    public void Preview_defaults_to_live_comparison_and_mass_scope()
    {
        var input = new PreviewConfigurationRestoreIn();

        Assert.True(input.CompareWithEquipment);
        Assert.Empty(input.TargetDeviceIds);
        Assert.True(input.IncludeDevices);
        Assert.True(input.IncludeRoutes);
        Assert.True(input.IncludeCredentials);
    }

    [Fact]
    public void Conflict_report_distinguishes_blocking_items_from_warnings()
    {
        var blocking = new ConfigurationRestoreConflictOut
        {
            Code = "DuplicateDeviceIdentity",
            Severity = "Critical",
            Blocking = true
        };
        var warning = new ConfigurationRestoreConflictOut
        {
            Code = "EquipmentDrift",
            Severity = "Warning",
            Blocking = false
        };

        Assert.True(blocking.Blocking);
        Assert.False(warning.Blocking);
        Assert.NotEqual(blocking.Severity, warning.Severity);
    }

    [Fact]
    public void Restore_execution_reports_target_scope()
    {
        var result = new ConfigurationRestoreExecutionOut
        {
            TargetDeviceCount = 3,
            IsMassRestore = false
        };

        Assert.Equal(3, result.TargetDeviceCount);
        Assert.False(result.IsMassRestore);
    }
}
