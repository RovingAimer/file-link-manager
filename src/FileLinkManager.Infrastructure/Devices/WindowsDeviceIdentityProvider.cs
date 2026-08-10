using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using FileLinkManager.Application.Abstractions;
using FileLinkManager.Domain.Storage;

namespace FileLinkManager.Infrastructure.Devices;

public sealed class WindowsDeviceIdentityProvider : IDeviceIdentityProvider
{
    private const string CryptographyRegistryPath = @"SOFTWARE\Microsoft\Cryptography";
    private const string MachineGuidValueName = "MachineGuid";

    private readonly Func<RegistryView, string?> readMachineGuid;
    private readonly Func<string> getMachineName;

    public WindowsDeviceIdentityProvider()
        : this(ReadMachineGuid, () => Environment.MachineName)
    {
    }

    internal WindowsDeviceIdentityProvider(
        Func<RegistryView, string?> readMachineGuid,
        Func<string> getMachineName)
    {
        this.readMachineGuid = readMachineGuid ?? throw new ArgumentNullException(nameof(readMachineGuid));
        this.getMachineName = getMachineName ?? throw new ArgumentNullException(nameof(getMachineName));
    }

    public DeviceIdentity GetCurrent()
    {
        var machineGuid = ReadMachineGuidWithFallback();
        if (string.IsNullOrWhiteSpace(machineGuid))
        {
            throw new InvalidOperationException(
                "A usable Windows MachineGuid is required to determine this device identity.");
        }

        var machineName = ReadMachineName();
        if (string.IsNullOrWhiteSpace(machineName))
        {
            throw new InvalidOperationException(
                "A usable Windows machine name is required to determine this device identity.");
        }

        var normalizedMachineGuid = machineGuid.Trim().ToUpperInvariant();
        var deviceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedMachineGuid)))
            .ToLowerInvariant();

        return new DeviceIdentity(deviceKey, machineName);
    }

    private string? ReadMachineGuidWithFallback()
    {
        try
        {
            var registry64MachineGuid = readMachineGuid(RegistryView.Registry64);
            return string.IsNullOrWhiteSpace(registry64MachineGuid)
                ? readMachineGuid(RegistryView.Default)
                : registry64MachineGuid;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
            System.Security.SecurityException or
            IOException)
        {
            throw new InvalidOperationException(
                "Unable to read the Windows MachineGuid from the registry. Verify that HKLM\\SOFTWARE\\Microsoft\\Cryptography is accessible.",
                exception);
        }
    }

    private string ReadMachineName()
    {
        try
        {
            return getMachineName();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                "Unable to read the Windows machine name required to determine this device identity.",
                exception);
        }
    }

    private static string? ReadMachineGuid(RegistryView registryView)
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
        using var cryptographyKey = localMachine.OpenSubKey(CryptographyRegistryPath);
        return cryptographyKey?.GetValue(MachineGuidValueName) as string;
    }
}
