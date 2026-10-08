using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text.Json;
using Zeus.Core;

namespace Zeus.Windows;

/// <summary>Reads Windows observations; missing sensors and providers stay unknown.</summary>
public sealed class WindowsHardwareDiagnostics : IHardwareDiagnostics
{
    public async Task<HardwareSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O diagnóstico requer Windows.");

        // Parallel providers prevent sequential minute-long waits; WMI enumerations
        // and PowerShell subprocesses additionally have their own deadlines.
        var warnings = new ConcurrentQueue<string>();
        async Task<T> Read<T>(string label, Func<CancellationToken, T> read, T fallback)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(18));
            try
            {
                return await Task.Run(() => read(deadline.Token), deadline.Token).WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                warnings.Enqueue($"{label}: o provedor excedeu o prazo de consulta; dados indisponíveis.");
                return fallback;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                warnings.Enqueue($"{label}: não foi possível obter dados ({error.Message}).");
                return fallback;
            }
        }

        async Task<T> ReadAsync<T>(string label, Func<CancellationToken, Task<T>> read, T fallback)
        {
            try { return await read(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                warnings.Enqueue($"{label}: dados indisponíveis ({error.Message}).");
                return fallback;
            }
        }

        var cpuTask = Read<CpuInfo?>("Processador", ReadCpu, null);
        var memoryTask = Read<MemoryInfo?>("Memória", ReadMemory, null);
        var graphicsTask = Read<IReadOnlyList<GpuInfo>>("Placas de vídeo", ReadGraphics, []);
        var disksTask = Read<IReadOnlyList<DiskInfo>>("Volumes", ReadDisks, []);
        var startupTask = Read<IReadOnlyList<StartupInfo>>("Inicialização", ReadStartup, []);
        var boardTask = Read<BoardInfo?>("Placa-mãe", ReadBoard, null);
        var biosTask = Read<BiosInfo?>("BIOS", ReadBios, null);
        var modulesTask = Read<IReadOnlyList<MemoryModuleInfo>?>("Módulos de memória", token => ReadMemoryModules(token, warnings), null);
        var memorySlotsTask = Read<int?>("Slots de memória", ReadMemoryArraySlots, null);
        var batteriesTask = Read<IReadOnlyList<BatteryInfo>>("Baterias", ReadBatteries, []);
        var networkTask = Read<IReadOnlyList<NetworkAdapterInfo>>("Adaptadores de rede", ReadNetworkAdapters, []);
        var securityTask = ReadAsync<SecurityInfo?>("Defender (outro antivírus pode estar ativo)", ReadSecurityAsync, null);
        var physicalTask = ReadAsync<IReadOnlyList<PhysicalDiskInfo>>("Armazenamento físico", async token =>
        {
            try { return await ReadPhysicalDisksAsync(warnings, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                warnings.Enqueue("Armazenamento: provedor Storage indisponível; inventário básico via Win32_DiskDrive, sem sensores de confiabilidade.");
                return await Read<IReadOnlyList<PhysicalDiskInfo>>("Discos físicos (Win32_DiskDrive)", token => ReadPhysicalDisksFallback(token, warnings), []);
            }
        }, []);
        await Task.WhenAll(cpuTask, memoryTask, graphicsTask, disksTask, startupTask, boardTask,
            biosTask, modulesTask, memorySlotsTask, batteriesTask, networkTask, securityTask, physicalTask);
        cancellationToken.ThrowIfCancellationRequested();
        var physical = await physicalTask;
        // Run the broad, optional inventory after the focused hardware providers. Starting
        // another large PowerShell query alongside every WMI/PowerShell probe can starve it
        // and turn one slow source into a missing inventory for the whole diagnostic.
        var inventory = await ReadAsync<WindowsInventoryInfo?>("Inventário detalhado do Windows", ReadWindowsInventoryAsync, null);
        if (inventory is not null)
            foreach (var warning in inventory.Warnings) warnings.Enqueue(warning);
        warnings.Enqueue(physical.Any(disk => disk.TemperatureCelsius.HasValue)
            ? "Algumas temperaturas de disco foram informadas pelo provedor Storage. Temperaturas de CPU/GPU e consumo não são coletados."
            : "Temperaturas de CPU/GPU e consumo não são coletados; nenhum sensor de temperatura de disco foi disponibilizado pelo provedor.");
        warnings.Enqueue("Saúde e desgaste dos discos refletem somente o provedor consultado; não substituem backup ou inspeção física. Frequência de RAM não determina dual channel; velocidade de rede não mede a Internet.");
        return new HardwareSnapshot(DateTimeOffset.UtcNow, Environment.OSVersion.VersionString,
            Environment.MachineName, await cpuTask, await memoryTask, await graphicsTask, await disksTask,
            await startupTask, await securityTask, warnings.ToArray(), await boardTask, await biosTask,
            await modulesTask, physical, await batteriesTask, await networkTask, inventory, await memorySlotsTask);
    }

    private sealed record WindowsInventoryPayload(WindowsInventoryInfo? Inventory, string[]? Warnings);

    private static async Task<WindowsInventoryInfo?> ReadWindowsInventoryAsync(CancellationToken token)
    {
        // Each source fails independently. Never include process command lines, event
        // XML, user names, or network credentials in this diagnostic export.
        const string script = @"
$warnings = [System.Collections.Generic.List[string]]::new()
function Read-Part([string]$label, [scriptblock]$body) {
  try { return @(& $body) } catch { $warnings.Add($label + ': fonte indisponível.'); return @() }
}
$routes = Read-Part 'Rotas de rede' { Get-NetRoute -ErrorAction Stop | Select-Object -First 300 @{n='AdapterIndex';e={[int]$_.InterfaceIndex}},@{n='Route';e={([string]$_.DestinationPrefix + ' -> ' + [string]$_.NextHop)} } }
$network = Read-Part 'Rede' { Get-NetIPConfiguration -ErrorAction Stop | Select-Object @{n='Adapter';e={$_.InterfaceAlias}},@{n='InterfaceIndex';e={$_.InterfaceIndex}},@{n='Addresses';e={@($_.IPv4Address.IPAddress + $_.IPv6Address.IPAddress)}},@{n='DnsServers';e={@($_.DNSServer.ServerAddresses)}},@{n='Gateways';e={@($_.IPv4DefaultGateway.NextHop + $_.IPv6DefaultGateway.NextHop)}},@{n='Status';e={[string]$_.NetProfile.NetworkCategory}} }
$proxy = $null; $proxyEnabled = $null; $proxyPac = $null; $proxyAutoDetect = $null; $proxyBypass = $null; $proxyAvailable = $false
try {
  $settings = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction Stop
  if ($null -ne $settings.ProxyEnable) { $proxyEnabled = ([int]$settings.ProxyEnable -eq 1) }
  $proxy = [string]$settings.ProxyServer
  if ([string]::IsNullOrWhiteSpace($proxy)) { $proxy = $null } elseif ($proxy -match '@') { $proxy = $proxy -replace '(?i)(^|;)[^;]*@','$1[redigido]@' }
  $proxyPac = [string]$settings.AutoConfigURL
  if (![string]::IsNullOrWhiteSpace($proxyPac)) { $proxyPac = $proxyPac -replace '(?i)://[^/@]*@','://[redigido]@'; $proxyPac = ($proxyPac -split '[?#]',2)[0] } else { $proxyPac = $null }
  if ($null -ne $settings.AutoDetect) { $proxyAutoDetect = ([int]$settings.AutoDetect -eq 1) }
  $proxyBypass = [string]$settings.ProxyOverride
  if ([string]::IsNullOrWhiteSpace($proxyBypass)) { $proxyBypass = $null }
  $proxyAvailable = $true
} catch { $warnings.Add('Proxy do usuário: configurações do Registro HKCU indisponíveis.') }
$drivers = Read-Part 'Drivers' { Get-CimInstance Win32_PnPSignedDriver -ErrorAction Stop | Select-Object DeviceName,DriverProviderName,DriverVersion,@{n='Date';e={if($_.DriverDate){$_.DriverDate.ToString('yyyy-MM-dd')}else{$null}}},Signer,IsSigned }
$pnp = Read-Part 'Dispositivos PnP' { Get-CimInstance Win32_PnPEntity -ErrorAction Stop | Select-Object Name,PNPClass,Status,PNPDeviceID,@{n='ProblemCode';e={if($_.ConfigManagerErrorCode -ne 0){[string]$_.ConfigManagerErrorCode}else{$null}}} }
$presentPnp = $null
try { $presentPnp = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase); Get-PnpDevice -PresentOnly -ErrorAction Stop | ForEach-Object { if (![string]::IsNullOrWhiteSpace([string]$_.InstanceId)) { [void]$presentPnp.Add([string]$_.InstanceId) } } } catch { $warnings.Add('Presença de dispositivos PnP: fonte Get-PnpDevice indisponível; reversão de driver desativada.') }
$processes = Read-Part 'Processos' { Get-Process -ErrorAction Stop | Where-Object { $_.Id -gt 0 } | Sort-Object WorkingSet64 -Descending | Select-Object -First 200 @{n='Name';e={$_.ProcessName}},Id,@{n='CpuSeconds';e={if($_.CPU -ne $null){[double]$_.CPU}else{$null}}},@{n='WorkingSetBytes';e={[uint64]$_.WorkingSet64}} }
 $services = Read-Part 'Serviços' { Get-CimInstance Win32_Service -ErrorAction Stop | Select-Object Name,DisplayName,State,StartMode }
 $serviceDependencies = Read-Part 'Dependências dos serviços' { Get-Service -ErrorAction Stop | ForEach-Object { [pscustomobject]@{Name=[string]$_.Name;Dependencies=@($_.ServicesDependedOn | ForEach-Object {[string]$_.Name})} } }
 $dependencyMap = $null
 if ($null -ne $serviceDependencies) { $dependencyMap = @{}; foreach ($entry in $serviceDependencies) { $dependencyMap[[string]$entry.Name] = @($entry.Dependencies) } }
$tasks = Read-Part 'Tarefas agendadas' { Get-ScheduledTask -ErrorAction Stop | Select-Object -First 500 @{n='Name';e={$_.TaskName}},TaskPath,State }
$software = Read-Part 'Software instalado' { foreach($path in @('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*')) { Get-ItemProperty $path -ErrorAction SilentlyContinue | Where-Object DisplayName | Select-Object @{n='Name';e={$_.DisplayName}},@{n='Version';e={[string]$_.DisplayVersion}},@{n='Publisher';e={[string]$_.Publisher}} } }
$events = foreach($log in @('System','Application','Microsoft-Windows-WindowsUpdateClient/Operational')) { Read-Part ('Eventos ' + $log) { try { Get-WinEvent -FilterHashtable @{LogName=$log;Level=1,2,3;StartTime=(Get-Date).AddDays(-14)} -MaxEvents 20 -ErrorAction Stop | Select-Object @{n='Time';e={$_.TimeCreated.ToUniversalTime().ToString('o')}},@{n='Log';e={$log}},@{n='Provider';e={$_.ProviderName}},Id,@{n='Level';e={if (![string]::IsNullOrWhiteSpace([string]$_.LevelDisplayName)) { [string]$_.LevelDisplayName } else { switch ([int]$_.Level) { 1 {'Critical'} 2 {'Error'} 3 {'Warning'} default {$null} } } }} } catch { if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { return @() }; throw } } }
$secureBoot = $null; try { $secureBoot = [bool](Confirm-SecureBootUEFI -ErrorAction Stop) } catch { $warnings.Add('Secure Boot: consulta indisponível neste firmware ou nesta sessão.') }
$tpmPresent = $null; $tpmReady = $null; try { $t=Get-Tpm -ErrorAction Stop; $tpmPresent=[bool]$t.TpmPresent; $tpmReady=[bool]$t.TpmReady } catch { $warnings.Add('TPM: estado indisponível.') }
$updates = $null; $warnings.Add('Windows Update: atualizações pendentes não foram consultadas nesta coleta para evitar busca online ou espera longa.')
$inventory = [pscustomobject]@{
 NetworkConfiguration=@($network | ForEach-Object { $ifIndex=$_.InterfaceIndex; [pscustomobject]@{Adapter=[string]$_.Adapter;Addresses=@($_.Addresses);DnsServers=@($_.DnsServers);Gateways=@($_.Gateways);Status=[string]$_.Status;Routes=@($routes | Where-Object AdapterIndex -eq $ifIndex | ForEach-Object Route);Proxy=$proxy} });
 ProxyConfiguration=[pscustomobject]@{ManualProxyEnabled=$proxyEnabled;ManualProxyServer=$proxy;AutoConfigUrl=$proxyPac;AutoDetectEnabled=$proxyAutoDetect;BypassList=$proxyBypass;IsAvailable=$proxyAvailable};
 Drivers=@($drivers | ForEach-Object { [pscustomobject]@{Device=[string]$_.DeviceName;Provider=[string]$_.DriverProviderName;Version=[string]$_.DriverVersion;Date=$_.Date;Signer=$_.Signer;IsSigned=$_.IsSigned} });
 PnpDevices=@($pnp | ForEach-Object { $instanceId=[string]$_.PNPDeviceID; $isPresent=$null; if ($null -ne $presentPnp -and ![string]::IsNullOrWhiteSpace($instanceId)) { $isPresent=$presentPnp.Contains($instanceId) }; [pscustomobject]@{Name=[string]$_.Name;Class=[string]$_.PNPClass;Status=[string]$_.Status;ProblemCode=$_.ProblemCode;InstanceId=$instanceId;IsPresent=$isPresent} });
 Processes=@($processes | ForEach-Object { [pscustomobject]@{Name=[string]$_.Name;Id=[int]$_.Id;CpuSeconds=$_.CpuSeconds;WorkingSetBytes=$_.WorkingSetBytes} });
 Services=@($services | ForEach-Object { $serviceName=[string]$_.Name; $hasDependencies=$null -ne $dependencyMap -and $dependencyMap.ContainsKey($serviceName); $dependencies=[string[]]@(); if ($hasDependencies) { $dependencies=[string[]]$dependencyMap[$serviceName] }; [pscustomobject]@{Name=$serviceName;DisplayName=[string]$_.DisplayName;Status=[string]$_.State;StartType=[string]$_.StartMode;DependenciesAvailable=$hasDependencies;Dependencies=$dependencies} });
 ScheduledTasks=@($tasks | ForEach-Object { [pscustomobject]@{Name=[string]$_.Name;Path=[string]$_.TaskPath;State=[string]$_.State} });
 InstalledSoftware=@($software | ForEach-Object { [pscustomobject]@{Name=[string]$_.Name;Version=[string]$_.Version;Publisher=[string]$_.Publisher} });
 RecentEvents=@($events | ForEach-Object { [pscustomobject]@{Time=$_.Time;Log=[string]$_.Log;Provider=[string]$_.Provider;Id=[int]$_.Id;Level=[string]$_.Level;Message=''} });
 SecurityState=[pscustomobject]@{SecureBootEnabled=$secureBoot;TpmPresent=$tpmPresent;TpmReady=$tpmReady};
 UpdateState=[pscustomobject]@{PendingCount=$updates;Source='Não consultado nesta coleta'};
 WindowsImageHealth=$null
}
[void]$warnings.Add('RAM: canais de memória não são inferidos pela quantidade de módulos. Integridade da imagem do Windows não é medida nesta coleta; use o Centro de Reparos. O proxy aqui cobre apenas valores observados em HKCU Internet Settings; auto-detecção ausente no Registro e configurações WinHTTP ou por aplicativo permanecem desconhecidas ou fora desta fonte.')
[pscustomobject]@{Inventory=$inventory;Warnings=@($warnings)} | ConvertTo-Json -Depth 7 -Compress";

        using var json = await RunPowerShellJsonAsync(script, token, TimeSpan.FromSeconds(45), WindowsPowerShellModule.Utility);
        var payload = JsonSerializer.Deserialize<WindowsInventoryPayload>(json.RootElement.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload?.Inventory is not { } inventory) return null;
        return inventory with { Warnings = payload.Warnings ?? [] };
    }

    private static ManagementObjectCollection Query(string query)
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope("root\\CIMV2"), new ObjectQuery(query),
            new System.Management.EnumerationOptions
            {
                Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = true, Rewindable = false
            });
        return searcher.Get();
    }

    private static string StringValue(ManagementBaseObject value, string field)
    {
        var text = Convert.ToString(value[field], CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? "Desconhecido" : text;
    }

    private static ulong? UnsignedValue(ManagementBaseObject row, string field) =>
        row[field] is { } value && ulong.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static CpuInfo? ReadCpu(CancellationToken token)
    {
        using var rows = Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor");
        var names = new List<string>();
        var cores = 0;
        var logical = 0;
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                names.Add(StringValue(row, "Name"));
                cores += Convert.ToInt32(row["NumberOfCores"], CultureInfo.InvariantCulture);
                logical += Convert.ToInt32(row["NumberOfLogicalProcessors"], CultureInfo.InvariantCulture);
            }
        }
        return names.Count == 0 ? null : new CpuInfo(string.Join(" / ", names.Distinct()), cores, logical);
    }

    private static MemoryInfo? ReadMemory(CancellationToken token)
    {
        using var rows = Query("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                var total = UnsignedValue(row, "TotalVisibleMemorySize");
                var free = UnsignedValue(row, "FreePhysicalMemory");
                if (total is null or 0 || free is null)
                    throw new InvalidDataException("O provedor não forneceu memória total e disponível válidas.");
                return new MemoryInfo(checked(total.Value * 1024), checked(free.Value * 1024));
            }
        }
        return null;
    }

    private static IReadOnlyList<GpuInfo> ReadGraphics(CancellationToken token)
    {
        using var rows = Query("SELECT Name,DriverVersion FROM Win32_VideoController");
        var result = new List<GpuInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                // AdapterRAM is UInt32 and truncates modern VRAM; do not report it.
                result.Add(new GpuInfo(StringValue(row, "Name"), StringValue(row, "DriverVersion")));
            }
        }
        return result;
    }

    private static IReadOnlyList<DiskInfo> ReadDisks(CancellationToken token)
    {
        var result = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            token.ThrowIfCancellationRequested();
            if (!drive.IsReady) continue;
            result.Add(new DiskInfo(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Disco local" : drive.VolumeLabel,
                drive.Name, (ulong)drive.TotalSize, (ulong)drive.AvailableFreeSpace, drive.DriveFormat));
        }
        return result;
    }

    private static IReadOnlyList<StartupInfo> ReadStartup(CancellationToken token)
    {
        using var rows = Query("SELECT Name,Location,User FROM Win32_StartupCommand");
        var result = new List<StartupInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                result.Add(new StartupInfo(StringValue(row, "Name"), StringValue(row, "Location"), StringValue(row, "User")));
            }
        }
        return result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static BoardInfo? ReadBoard(CancellationToken token)
    {
        using var rows = Query("SELECT Manufacturer,Product FROM Win32_BaseBoard");
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                return new BoardInfo(StringValue(row, "Manufacturer"), StringValue(row, "Product"));
            }
        }
        return null;
    }

    private static BiosInfo? ReadBios(CancellationToken token)
    {
        using var rows = Query("SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS");
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                string? releaseDate = null;
                if (row["ReleaseDate"] is string raw && raw.Length >= 8 &&
                    DateOnly.TryParseExact(raw[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    releaseDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return new BiosInfo(StringValue(row, "Manufacturer"), StringValue(row, "SMBIOSBIOSVersion"), releaseDate);
            }
        }
        return null;
    }

    private static IReadOnlyList<MemoryModuleInfo> ReadMemoryModules(CancellationToken token, ConcurrentQueue<string> warnings)
    {
        using var rows = Query("SELECT DeviceLocator,BankLabel,Capacity,Speed,ConfiguredClockSpeed,Manufacturer FROM Win32_PhysicalMemory");
        var result = new List<MemoryModuleInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                var location = StringValue(row, "DeviceLocator");
                if (location == "Desconhecido") location = StringValue(row, "BankLabel");
                var capacity = UnsignedValue(row, "Capacity");
                if (!TryReadCapacity(capacity, $"Módulo de memória {location}", warnings, out var bytes)) continue;
                var speed = UnsignedValue(row, "ConfiguredClockSpeed");
                if (speed is null or 0) speed = UnsignedValue(row, "Speed");
                result.Add(new MemoryModuleInfo(location, bytes,
                    speed is > 0 and <= uint.MaxValue ? (uint)speed.Value : null, StringValue(row, "Manufacturer")));
            }
        }
        return result;
    }

    private static int? ReadMemoryArraySlots(CancellationToken token)
    {
        using var rows = Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
        var total = 0;
        var found = false;
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                var devices = UnsignedValue(row, "MemoryDevices");
                if (devices is null or 0 or > 4096) continue;
                total = checked(total + (int)devices.Value);
                found = true;
            }
        }
        return found ? total : null;
    }

    private static IReadOnlyList<BatteryInfo> ReadBatteries(CancellationToken token)
    {
        using var rows = Query("SELECT Name,EstimatedChargeRemaining,BatteryStatus FROM Win32_Battery");
        var result = new List<BatteryInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                var charge = UnsignedValue(row, "EstimatedChargeRemaining");
                var status = UnsignedValue(row, "BatteryStatus") switch
                {
                    3 => "Carga completa", 4 => "Carga baixa", 5 => "Carga crítica", 6 => "Carregando",
                    7 => "Carregando, carga alta", 8 => "Carregando, carga baixa", 9 => "Carregando, carga crítica",
                    11 => "Carga parcial", _ => "Desconhecido"
                };
                result.Add(new BatteryInfo(StringValue(row, "Name"), charge is <= 100 ? (int)charge.Value : null, status));
            }
        }
        return result;
    }

    private static IReadOnlyList<NetworkAdapterInfo> ReadNetworkAdapters(CancellationToken token)
    {
        using var rows = Query("SELECT Name,NetConnectionStatus,Speed FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");
        var result = new List<NetworkAdapterInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                var status = UnsignedValue(row, "NetConnectionStatus") switch
                {
                    0 => "Desconectado", 1 => "Conectando", 2 => "Conectado", 3 => "Desconectando",
                    4 => "Hardware indisponível", 5 => "Hardware desabilitado", 6 => "Falha de hardware",
                    7 => "Mídia desconectada", 8 => "Autenticando", 9 => "Autenticação concluída",
                    10 => "Falha de autenticação", 11 => "Endereço inválido", 12 => "Credenciais necessárias",
                    _ => "Desconhecido"
                };
                var speed = UnsignedValue(row, "Speed");
                result.Add(new NetworkAdapterInfo(StringValue(row, "Name"), status, speed is > 0 ? speed : null));
            }
        }
        return result;
    }

    private static IReadOnlyList<PhysicalDiskInfo> ReadPhysicalDisksFallback(CancellationToken token, ConcurrentQueue<string> warnings)
    {
        using var rows = Query("SELECT Model,Size,InterfaceType,MediaType,Status FROM Win32_DiskDrive");
        var result = new List<PhysicalDiskInfo>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                var name = StringValue(row, "Model");
                if (!TryReadCapacity(UnsignedValue(row, "Size"), $"Disco físico {name}", warnings, out var bytes)) continue;
                // "Fixed hard disk media" does not distinguish SSD/HDD; InterfaceType
                // can report SCSI for NVMe. Preserve the fallback provider limitation.
                result.Add(new PhysicalDiskInfo(name, StringValue(row, "MediaType"),
                    StringValue(row, "InterfaceType") + " (Win32_DiskDrive)", bytes,
                    StringValue(row, "Status") + " (Win32_DiskDrive)", null, null));
            }
        }
        return result;
    }

    private static async Task<IReadOnlyList<PhysicalDiskInfo>> ReadPhysicalDisksAsync(
        ConcurrentQueue<string> warnings, CancellationToken token)
    {
        const string script = "& { $items = @(); $notes = @(); " +
            "foreach ($d in @(Storage\\Get-PhysicalDisk -ErrorAction Stop)) { " +
            "$r = $null; try { $r = $d | Storage\\Get-StorageReliabilityCounter -ErrorAction Stop } " +
            "catch { $notes += ('Sensores de confiabilidade indisponíveis para ' + [string]$d.FriendlyName + '.'); }; " +
            "$items += [pscustomobject]@{ Name = [string]$d.FriendlyName; MediaType = [string]$d.MediaType; " +
            "BusType = [string]$d.BusType; SizeBytes = [uint64]$d.Size; HealthStatus = [string]$d.HealthStatus; " +
            "TemperatureCelsius = if ($null -ne $r -and $null -ne $r.Temperature -and $r.Temperature -gt 0 -and $r.Temperature -le 125) { [double]$r.Temperature } else { $null }; " +
            "TemperatureMaxCelsius = if ($null -ne $r -and $null -ne $r.TemperatureMax -and $r.TemperatureMax -gt 0 -and $r.TemperatureMax -le 125) { [double]$r.TemperatureMax } else { $null }; " +
            "Wear = if ($null -ne $r -and $null -ne $r.Wear) { [uint64]$r.Wear } else { $null }; " +
            "PowerOnHours = if ($null -ne $r) { $r.PowerOnHours } else { $null }; " +
            "ReadErrorsTotal = if ($null -ne $r) { $r.ReadErrorsTotal } else { $null }; " +
            "ReadErrorsUncorrected = if ($null -ne $r) { $r.ReadErrorsUncorrected } else { $null }; " +
            "WriteErrorsTotal = if ($null -ne $r) { $r.WriteErrorsTotal } else { $null }; " +
            "WriteErrorsUncorrected = if ($null -ne $r) { $r.WriteErrorsUncorrected } else { $null } }; }; " +
            "[pscustomobject]@{ Disks = @($items); Warnings = @($notes) } | Microsoft.PowerShell.Utility\\ConvertTo-Json -Depth 4 -Compress }";
        using var json = await RunPowerShellJsonAsync(script, token, WindowsPowerShellModule.Utility, WindowsPowerShellModule.Storage);
        if (json.RootElement.TryGetProperty("Warnings", out var notes))
            foreach (var note in notes.EnumerateArray())
                if (note.GetString() is { } text) warnings.Enqueue(text);
        var disks = json.RootElement.GetProperty("Disks");
        var result = new List<PhysicalDiskInfo>();
        foreach (var disk in disks.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (ParsePhysicalDisk(disk, warnings) is { } observed) result.Add(observed);
        }
        return result;
    }

    internal static PhysicalDiskInfo? ParsePhysicalDisk(JsonElement disk, ConcurrentQueue<string> warnings)
    {
        var name = JsonText(disk, "Name");
        if (!TryReadCapacity(NullableUInt64(disk, "SizeBytes"), $"Disco físico {name}", warnings, out var bytes)) return null;
        var wear = NullableUInt64(disk, "Wear");
        if (wear is > 100)
            warnings.Enqueue($"Disco físico {name}: desgaste informado de {wear}% acima do limite estimado de 100%; preserve um backup e consulte o fabricante.");
        return new PhysicalDiskInfo(name, JsonText(disk, "MediaType"), JsonText(disk, "BusType"), bytes,
            JsonText(disk, "HealthStatus"), NullableDouble(disk, "TemperatureCelsius"), wear,
            NullableDouble(disk, "TemperatureMaxCelsius"), NullableUInt64(disk, "PowerOnHours"),
            NullableUInt64(disk, "ReadErrorsTotal"), NullableUInt64(disk, "ReadErrorsUncorrected"),
            NullableUInt64(disk, "WriteErrorsTotal"), NullableUInt64(disk, "WriteErrorsUncorrected"));
    }

    internal static bool TryReadCapacity(ulong? reported, string component, ConcurrentQueue<string> warnings, out ulong bytes)
    {
        bytes = reported ?? 0;
        if (bytes > 0) return true;
        warnings.Enqueue($"{component}: capacidade não fornecida pelo provedor; componente omitido para evitar informar zero bytes como medição.");
        return false;
    }

    private static string JsonText(JsonElement value, string property) =>
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(field.GetString()) ? field.GetString()! : "Desconhecido";

    private static double? NullableDouble(JsonElement value, string property) =>
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.Number &&
        field.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

    private static ulong? NullableUInt64(JsonElement value, string property) =>
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.Number &&
        field.TryGetUInt64(out var number) ? number : null;

    private static async Task<SecurityInfo?> ReadSecurityAsync(CancellationToken token)
    {
        const string script = "& { $s = Defender\\Get-MpComputerStatus; " +
            "[pscustomobject]@{ DefenderEnabled = [bool]$s.AntivirusEnabled; " +
            "RealTimeProtectionEnabled = [bool]$s.RealTimeProtectionEnabled; " +
            "SignatureUpdatedAt = if ($s.AntivirusSignatureLastUpdated) { $s.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('o') } else { $null }; " +
            "Summary = [string]$s.AMRunningMode } | Microsoft.PowerShell.Utility\\ConvertTo-Json -Compress }";
        using var json = await RunPowerShellJsonAsync(script, token, WindowsPowerShellModule.Utility, WindowsPowerShellModule.Defender);
        var root = json.RootElement;
        DateTimeOffset? updated = null;
        if (root.TryGetProperty("SignatureUpdatedAt", out var date) && date.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            updated = parsed;
        return new SecurityInfo(root.GetProperty("DefenderEnabled").GetBoolean(),
            root.GetProperty("RealTimeProtectionEnabled").GetBoolean(), updated,
            root.GetProperty("Summary").GetString() ?? "Estado consultado no Defender");
    }

    private static Task<JsonDocument> RunPowerShellJsonAsync(string script, CancellationToken token,
        params WindowsPowerShellModule[] modules) =>
        RunPowerShellJsonAsync(script, token, TimeSpan.FromSeconds(25), modules);

    private static async Task<JsonDocument> RunPowerShellJsonAsync(string script, CancellationToken token,
        TimeSpan timeout, params WindowsPowerShellModule[] modules)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        var start = TrustedPowerShell.Create(script, modules);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell não iniciou.");
        var outputTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errorTask = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Código {process.ExitCode}" : error.Trim());
            return JsonDocument.Parse(output.Trim());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"O provedor PowerShell excedeu {timeout.TotalSeconds:0} segundos.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            // Observe stream failures after cancellation without waiting for a failed
            // provider's descendants to close inherited output handles.
            if (!outputTask.IsCompleted) _ = outputTask.ContinueWith(t => _ = t.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            if (!errorTask.IsCompleted) _ = errorTask.ContinueWith(t => _ = t.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
