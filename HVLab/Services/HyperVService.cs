using System.Diagnostics;
using System.Text.Json;
using HVLab.Models;

namespace HVLab.Services;

public class HyperVService
{
    private const string PowerShellExe = "powershell.exe";

    private async Task<string> ExecutePowerShellAsync(string script)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = PowerShellExe,
                Arguments = $"-NoProfile -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
                return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await Task.Run(() => process.WaitForExit());

            if (!string.IsNullOrEmpty(error))
                throw new Exception($"PowerShell error: {error}");

            return output;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to execute PowerShell: {ex.Message}", ex);
        }
    }

    public async Task<List<VirtualSwitch>> GetVirtualSwitchesAsync()
    {
        try
        {
            var script = "Get-VMSwitch | ConvertTo-Json -AsArray";
            var output = await ExecutePowerShellAsync(script);
            return ParseVirtualSwitches(output);
        }
        catch
        {
            return new List<VirtualSwitch>();
        }
    }

    public async Task CreateVSwitchNATAsync(string name, string ipAddress, string subnetMask)
    {
        var script = $@"
            New-VMSwitch -Name '{name}' -SwitchType NAT -ErrorAction Stop
            New-NetIPAddress -IPAddress '{ipAddress}' -PrefixLength 24 -AddressFamily IPv4
        ";
        await ExecutePowerShellAsync(script);
    }

    public async Task<List<VirtualMachine>> GetVirtualMachinesAsync()
    {
        try
        {
            var script = "Get-VM | ConvertTo-Json -AsArray";
            var output = await ExecutePowerShellAsync(script);
            return ParseVirtualMachines(output);
        }
        catch
        {
            return new List<VirtualMachine>();
        }
    }

    public async Task CreateVirtualMachineAsync(string name, long memoryMB, int processors, string switchName)
    {
        var script = $@"
            New-VM -Name '{name}' -MemoryStartupBytes {memoryMB * 1024 * 1024} -ProcessorCount {processors} -SwitchName '{switchName}' -ErrorAction Stop
        ";
        await ExecutePowerShellAsync(script);
    }

    public async Task StartVMAsync(string vmName)
    {
        var script = $"Start-VM -Name '{vmName}' -ErrorAction Stop";
        await ExecutePowerShellAsync(script);
    }

    public async Task StopVMAsync(string vmName)
    {
        var script = $"Stop-VM -Name '{vmName}' -Force -ErrorAction Stop";
        await ExecutePowerShellAsync(script);
    }

    public async Task RemoveVMAsync(string vmName)
    {
        var script = $"Remove-VM -Name '{vmName}' -Force -ErrorAction Stop";
        await ExecutePowerShellAsync(script);
    }

    public async Task CreateDifferentialDiskAsync(string basePath, string diffPath)
    {
        var script = $@"
            New-VHD -Path '{diffPath}' -ParentPath '{basePath}' -Differencing -ErrorAction Stop
        ";
        await ExecutePowerShellAsync(script);
    }

    private List<VirtualSwitch> ParseVirtualSwitches(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<VirtualSwitch>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var switches = new List<VirtualSwitch>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                switches.Add(new VirtualSwitch
                {
                    Name = element.GetProperty("Name").GetString() ?? string.Empty,
                    SwitchType = element.GetProperty("SwitchType").GetString() ?? "NAT",
                    Description = element.TryGetProperty("Notes", out var notes) ? notes.GetString() ?? string.Empty : string.Empty,
                    Enabled = true
                });
            }

            return switches;
        }
        catch
        {
            return new List<VirtualSwitch>();
        }
    }

    private List<VirtualMachine> ParseVirtualMachines(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<VirtualMachine>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var vms = new List<VirtualMachine>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                vms.Add(new VirtualMachine
                {
                    Name = element.GetProperty("Name").GetString() ?? string.Empty,
                    State = element.GetProperty("State").GetString() ?? "Off",
                    MemoryMB = element.TryGetProperty("MemoryAssigned", out var mem) ? mem.GetInt64() / (1024 * 1024) : 0,
                    ProcessorCount = element.TryGetProperty("ProcessorCount", out var cpu) ? cpu.GetInt32() : 0,
                    CreatedAt = DateTime.Now
                });
            }

            return vms;
        }
        catch
        {
            return new List<VirtualMachine>();
        }
    }
}
