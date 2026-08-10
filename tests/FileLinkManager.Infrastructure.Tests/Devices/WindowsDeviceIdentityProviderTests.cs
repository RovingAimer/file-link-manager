using Microsoft.Win32;
using FileLinkManager.Infrastructure.Devices;

namespace FileLinkManager.Infrastructure.Tests.Devices;

public sealed class WindowsDeviceIdentityProviderTests
{
    [Fact]
    public void GetCurrent_SameMachineGuidWithDifferentNames_KeepsKeyAndUsesCurrentDisplayName()
    {
        var first = CreateProvider("machine-guid-001", "Original PC").GetCurrent();
        var renamed = CreateProvider("machine-guid-001", "Renamed PC").GetCurrent();

        Assert.Equal(first.DeviceKey, renamed.DeviceKey);
        Assert.Equal("Original PC", first.DisplayName);
        Assert.Equal("Renamed PC", renamed.DisplayName);
    }

    [Fact]
    public void GetCurrent_SameMachineNameWithDifferentGuids_ProducesDifferentKeys()
    {
        var first = CreateProvider("machine-guid-001", "Workstation").GetCurrent();
        var second = CreateProvider("machine-guid-002", "Workstation").GetCurrent();

        Assert.NotEqual(first.DeviceKey, second.DeviceKey);
        Assert.Equal("Workstation", first.DisplayName);
        Assert.Equal("Workstation", second.DisplayName);
    }

    [Fact]
    public void GetCurrent_NormalizesGuidBeforeHashing_AndDoesNotReturnRawGuid()
    {
        var identity = CreateProvider("  machine-guid-001  ", "Workstation").GetCurrent();

        Assert.Equal("55dc64d532488f9c22410ef7d3a9429b51d260358bc9a70257f8057c6cf37da6", identity.DeviceKey);
        Assert.DoesNotContain("machine-guid-001", identity.DeviceKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machine-guid-001", identity.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCurrent_PrefersRegistry64MachineGuid()
    {
        var requestedViews = new List<RegistryView>();
        var provider = new WindowsDeviceIdentityProvider(
            view =>
            {
                requestedViews.Add(view);
                return view == RegistryView.Registry64 ? "registry64-guid" : "default-guid";
            },
            () => "Workstation");

        var identity = provider.GetCurrent();

        Assert.Equal(new[] { RegistryView.Registry64 }, requestedViews);
        Assert.Equal("45705d3bb59fe8f2c2e2e3e51464e23f155f1e03aa711ce87181da8dd9842b21", identity.DeviceKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCurrent_WhenRegistry64IsMissingOrBlank_FallsBackToDefault(string? registry64Guid)
    {
        var requestedViews = new List<RegistryView>();
        var provider = new WindowsDeviceIdentityProvider(
            view =>
            {
                requestedViews.Add(view);
                return view == RegistryView.Registry64 ? registry64Guid : "default-guid";
            },
            () => "Workstation");

        var identity = provider.GetCurrent();

        Assert.Equal(new[] { RegistryView.Registry64, RegistryView.Default }, requestedViews);
        Assert.Equal("7655fd98c23cff27bde82757affeb58578f3d44c6a311a0ee0ddaf9dfe640c32", identity.DeviceKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCurrent_WhenNoUsableMachineGuidExists_ThrowsClearException(string? machineGuid)
    {
        var provider = new WindowsDeviceIdentityProvider(_ => machineGuid, () => "Workstation");

        var exception = Assert.Throws<InvalidOperationException>(provider.GetCurrent);

        Assert.Contains("MachineGuid", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCurrent_WhenMachineNameIsMissingOrBlank_ThrowsClearException(string? machineName)
    {
        var provider = new WindowsDeviceIdentityProvider(_ => "machine-guid-001", () => machineName!);

        var exception = Assert.Throws<InvalidOperationException>(provider.GetCurrent);

        Assert.Contains("machine name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCurrent_WhenMachineNameReadFails_WrapsExceptionWithActionableMessage()
    {
        var machineNameFailure = new InvalidOperationException("Machine name unavailable.");
        var provider = new WindowsDeviceIdentityProvider(
            _ => "machine-guid-001",
            () => throw machineNameFailure);

        var exception = Assert.Throws<InvalidOperationException>(provider.GetCurrent);

        Assert.Contains("machine name", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(machineNameFailure, exception.InnerException);
    }

    [Fact]
    public void GetCurrent_WhenRegistryAccessFails_WrapsExceptionWithActionableMessage()
    {
        var registryFailure = new UnauthorizedAccessException("Access denied.");
        var provider = new WindowsDeviceIdentityProvider(_ => throw registryFailure, () => "Workstation");

        var exception = Assert.Throws<InvalidOperationException>(provider.GetCurrent);

        Assert.Contains("registry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(registryFailure, exception.InnerException);
    }

    private static WindowsDeviceIdentityProvider CreateProvider(string machineGuid, string machineName) =>
        new(_ => machineGuid, () => machineName);
}
