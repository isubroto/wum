// src/WUM.Core/Services/UpdateService.cs
// Complete rewrite using PowerShell + WUA scripting
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using WUM.Core.Helpers;
using WUM.Core.Models;

namespace WUM.Core.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly RegistryHelper _registry;
        private readonly ILogger        _log;

        public UpdateService(RegistryHelper registry)
        {
            _registry = registry;
            _log      = Log.ForContext<UpdateService>();
        }

        // ── Get Available Updates ─────────────────────────────────────────
        public async Task<List<WindowsUpdate>> GetAvailableUpdatesAsync(
            bool includeHidden      = false,
            bool useMicrosoftUpdate = false,
            CancellationToken ct    = default)
        {
            try
            {
                // Microsoft Update service (vs default Windows Update) surfaces
                // drivers + other MS-product updates. Register + select it on
                // demand: ServerSelection=3 (ssOthers) + ServiceID.
                string serviceSetup = useMicrosoftUpdate ? @"
$muId = '7971f918-a847-4430-9279-4a52d1efe18d'
try {
    $sm  = New-Object -ComObject Microsoft.Update.ServiceManager
    $has = $false
    foreach ($s in $sm.Services) { if ($s.ServiceID -eq $muId) { $has = $true } }
    if (-not $has) { $sm.AddService2($muId, 2, '') | Out-Null }
    $Searcher.ServerSelection = 3
    $Searcher.ServiceID = $muId
} catch { }
" : "";

                // Leave Type open (like PSWindowsUpdate) so driver updates are
                // returned too — forcing Type='Software' silently hides drivers.
                // Honor includeHidden via the IsHidden clause.
                string criteria = includeHidden
                    ? "IsInstalled=0"
                    : "IsInstalled=0 AND IsHidden=0";

                // Use PowerShell to query WUA — works reliably with admin
                string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Searcher.Online = $true
" + serviceSetup + @"
$Results  = $Searcher.Search('" + criteria + @"')
$Updates  = @()
foreach ($u in $Results.Updates) {
    $kb  = ''
    if ($u.KBArticleIDs.Count -gt 0) { $kb = 'KB' + $u.KBArticleIDs[0] }
    $cat = 'Optional'
    if ($u.Categories.Count -gt 0) { $cat = $u.Categories.Item(0).Name }
    $Updates += [PSCustomObject]@{
        Id             = $u.Identity.UpdateID
        Title          = $u.Title
        Description    = $u.Description
        KBArticle      = $kb
        Category       = $cat
        IsHidden       = $u.IsHidden
        IsDownloaded   = $u.IsDownloaded
        IsMandatory    = $u.IsMandatory
        SizeInBytes    = $u.MaxDownloadSize
        SupportUrl     = $u.SupportUrl
        Severity       = $u.MsrcSeverity
        RequiresReboot = ($u.InstallationBehavior.RebootBehavior -ne 0)
    }
}
$Updates | ConvertTo-Json -Depth 3
";
                using var cts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));

                var (success, output, error) =
                    await PowerShellHelper.RunScriptAsync(script);

                if (!success || string.IsNullOrWhiteSpace(output))
                {
                    _log.Warning("WUA search returned no output. Error: {E}", error);
                    return new List<WindowsUpdate>();
                }

                return ParseUpdatesFromJson(output);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error fetching available updates");
                return new List<WindowsUpdate>();
            }
        }

        // ── Get Installed Updates ─────────────────────────────────────────
        public async Task<List<WindowsUpdate>> GetInstalledUpdatesAsync(
            CancellationToken ct = default)
        {
            try
            {
                string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Results  = $Searcher.Search('IsInstalled=1 AND Type=\'Software\'')
$Updates  = @()
foreach ($u in $Results.Updates) {
    $kb  = ''
    if ($u.KBArticleIDs.Count -gt 0) { $kb = 'KB' + $u.KBArticleIDs[0] }
    $cat = 'Optional'
    if ($u.Categories.Count -gt 0) { $cat = $u.Categories.Item(0).Name }
    $Updates += [PSCustomObject]@{
        Id             = $u.Identity.UpdateID
        Title          = $u.Title
        KBArticle      = $kb
        Category       = $cat
        IsHidden       = $u.IsHidden
        IsDownloaded   = $true
        IsMandatory    = $u.IsMandatory
        SizeInBytes    = $u.MaxDownloadSize
        SupportUrl     = $u.SupportUrl
        Severity       = $u.MsrcSeverity
        RequiresReboot = $false
        Status         = 'Installed'
    }
}
$Updates | ConvertTo-Json -Depth 3
";
                var (_, output, _) =
                    await PowerShellHelper.RunScriptAsync(script);

                if (string.IsNullOrWhiteSpace(output))
                    return new List<WindowsUpdate>();

                return ParseUpdatesFromJson(output, installed: true);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error fetching installed updates");
                return new List<WindowsUpdate>();
            }
        }

        // ── Get History ───────────────────────────────────────────────────
        public async Task<List<UpdateHistory>> GetUpdateHistoryAsync(
            int count = 50)
        {
            try
            {
                string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Total    = $Searcher.GetTotalHistoryCount()
$Fetch    = [Math]::Min(" + count + @", $Total)
if ($Fetch -le 0) { '[]'; exit }
$History  = $Searcher.QueryHistory(0, $Fetch)
$Items    = @()
foreach ($h in $History) {
    $kb = ''
    if ($h.Title -match 'KB\d+') { $kb = $Matches[0] }
    $Items += [PSCustomObject]@{
        Title         = $h.Title
        KBArticle     = $kb
        InstalledDate = $h.Date.ToString('o')
        Success       = ($h.ResultCode -eq 2)
        ResultCode    = [int]$h.ResultCode
        ErrorMessage  = if ($h.UnmappedResultCode -ne 0) { '0x' + $h.UnmappedResultCode.ToString('X8') } else { '' }
        Operation     = $h.Operation.ToString()
    }
}
$Items | ConvertTo-Json -Depth 3
";
                var (_, output, _) =
                    await PowerShellHelper.RunScriptAsync(script);

                if (string.IsNullOrWhiteSpace(output))
                    return new List<UpdateHistory>();

                return ParseHistoryFromJson(output);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error fetching update history");
                return new List<UpdateHistory>();
            }
        }

        // ── Download ──────────────────────────────────────────────────────
        public async Task<bool> DownloadUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default)
        {
            try
            {
                progress?.Report(5);

                string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Results  = $Searcher.Search(""IsInstalled=0 AND UpdateID='""  + '" + updateId + @"' + """""")
if ($Results.Updates.Count -eq 0) { Write-Output 'NOT_FOUND'; exit }
$Downloader         = $Session.CreateUpdateDownloader()
$Downloader.Updates = $Results.Updates
$Result             = $Downloader.Download()
Write-Output $Result.ResultCode
";
                using var cts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(15));

                var (_, output, _) =
                    await PowerShellHelper.RunScriptAsync(script);

                progress?.Report(100);

                output = output.Trim();
                _log.Information("Download result for {Id}: {Out}", updateId, output);

                // ResultCode 2 = Succeeded, 3 = SucceededWithErrors
                return output == "2" || output == "3";
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Download failed for {UpdateId}", updateId);
                return false;
            }
        }

        // ── Install ───────────────────────────────────────────────────────
        public async Task<bool> InstallUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default)
        {
            try
            {
                progress?.Report(5);

                string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Results  = $Searcher.Search(""IsInstalled=0 AND UpdateID='""  + '" + updateId + @"' + """""")
if ($Results.Updates.Count -eq 0) { Write-Output 'NOT_FOUND'; exit }
$Installer         = $Session.CreateUpdateInstaller()
$Installer.Updates = $Results.Updates
$Result            = $Installer.Install()
Write-Output $Result.ResultCode
";
                using var cts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(30));

                var (_, output, _) =
                    await PowerShellHelper.RunScriptAsync(script);

                progress?.Report(100);

                output = output.Trim();
                _log.Information("Install result for {Id}: {Out}", updateId, output);

                return output == "2" || output == "3";
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Install failed for {UpdateId}", updateId);
                return false;
            }
        }

        // ── Uninstall ─────────────────────────────────────────────────────
        public async Task<bool> UninstallUpdateAsync(string kbArticle)
        {
            var kb = kbArticle.Replace("KB", "",
                StringComparison.OrdinalIgnoreCase);
            return await PowerShellHelper.RunCommandAsync(
                "wusa.exe /uninstall /kb:" + kb + " /quiet /norestart");
        }

        // ── Hide ──────────────────────────────────────────────────────────
        public async Task<bool> HideUpdateAsync(string updateId)
        {
            string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Results  = $Searcher.Search(""UpdateID='"  + updateId + @"'"")
foreach ($u in $Results.Updates) { $u.IsHidden = $true }
Write-Output 'OK'
";
            var (_, output, _) = await PowerShellHelper.RunScriptAsync(script);
            return output.Trim() == "OK";
        }

        // ── Unhide ────────────────────────────────────────────────────────
        public async Task<bool> UnhideUpdateAsync(string updateId)
        {
            string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Results  = $Searcher.Search(""IsHidden=1 AND UpdateID='"  + updateId + @"'"")
foreach ($u in $Results.Updates) { $u.IsHidden = $false }
Write-Output 'OK'
";
            var (_, output, _) = await PowerShellHelper.RunScriptAsync(script);
            return output.Trim() == "OK";
        }

        // ── Settings ──────────────────────────────────────────────────────
        public async Task<UpdateSettings> GetSettingsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    return new UpdateSettings
                    {
                        ActiveHoursStart = _registry.GetValue<int>(
                            @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            "ActiveHoursStart", 8),
                        ActiveHoursEnd = _registry.GetValue<int>(
                            @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            "ActiveHoursEnd", 22),
                        AutoDownload = _registry.GetValue<int>(
                            @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                            "AUOptions", 3) >= 3,
                        AutoInstall = _registry.GetValue<int>(
                            @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                            "AUOptions", 3) == 4,
                        PauseOnMeteredConnection = _registry.GetValue<int>(
                            @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
                            "AllowAutoWindowsUpdateDownloadOverMeteredNetwork",
                            0) == 0
                    };
                }
                catch
                {
                    return new UpdateSettings();
                }
            });
        }

        public async Task SaveSettingsAsync(UpdateSettings settings)
        {
            await Task.Run(() =>
            {
                const string path =
                    @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
                _registry.SetValue(path, "ActiveHoursStart",
                    settings.ActiveHoursStart);
                _registry.SetValue(path, "ActiveHoursEnd",
                    settings.ActiveHoursEnd);
            });
        }

        // ── Reboot Check ──────────────────────────────────────────────────
        public bool IsRebootRequired()
        {
            try
            {
                return _registry.KeyExists(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\" +
                    @"WindowsUpdate\Auto Update\RebootRequired");
            }
            catch { return false; }
        }

        // ── Service Status ────────────────────────────────────────────────
        public async Task<string> GetServiceStatusAsync()
        {
            try
            {
                var (_, output, _) = await PowerShellHelper.RunScriptAsync(
                    "(Get-Service -Name wuauserv " +
                    "-ErrorAction SilentlyContinue).Status");

                return string.IsNullOrWhiteSpace(output)
                    ? "Unknown"
                    : output.Trim();
            }
            catch { return "Unknown"; }
        }

        // ── JSON Parsers ──────────────────────────────────────────────────
        private static List<WindowsUpdate> ParseUpdatesFromJson(
            string json, bool installed = false)
        {
            var list = new List<WindowsUpdate>();
            try
            {
                // Handle single object (when only 1 result)
                json = json.Trim();
                if (json.StartsWith("{"))
                    json = "[" + json + "]";

                using var doc  = JsonDocument.Parse(json);
                var       root = doc.RootElement;

                foreach (var el in root.EnumerateArray())
                {
                    var u = new WindowsUpdate
                    {
                        Id          = GetStr(el, "Id"),
                        Title       = GetStr(el, "Title"),
                        Description = GetStr(el, "Description"),
                        KBArticle   = GetStr(el, "KBArticle"),
                        IsHidden    = GetBool(el, "IsHidden"),
                        IsMandatory = GetBool(el, "IsMandatory"),
                        SizeInBytes = GetLong(el, "SizeInBytes"),
                        SupportUrl  = GetStr(el, "SupportUrl"),
                        Severity    = GetStr(el, "Severity"),
                        RequiresReboot = GetBool(el, "RequiresReboot"),
                        Status      = installed
                            ? UpdateStatus.Installed
                            : GetBool(el, "IsDownloaded")
                                ? UpdateStatus.Downloaded
                                : UpdateStatus.NotStarted,
                        Category    = ParseCategory(GetStr(el, "Category"))
                    };
                    list.Add(u);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse updates JSON");
            }
            return list;
        }

        private static List<UpdateHistory> ParseHistoryFromJson(string json)
        {
            var list = new List<UpdateHistory>();
            try
            {
                json = json.Trim();
                if (json.StartsWith("{"))
                    json = "[" + json + "]";

                using var doc  = JsonDocument.Parse(json);
                var       root = doc.RootElement;

                foreach (var el in root.EnumerateArray())
                {
                    var dateStr = GetStr(el, "InstalledDate");
                    DateTime.TryParse(dateStr, out var date);

                    list.Add(new UpdateHistory
                    {
                        Title         = GetStr(el, "Title"),
                        KBArticle     = GetStr(el, "KBArticle"),
                        InstalledDate = date,
                        Success       = GetBool(el, "Success"),
                        ResultCode    = GetInt(el, "ResultCode"),
                        ErrorMessage  = GetStr(el, "ErrorMessage"),
                        Operation     = GetStr(el, "Operation")
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse history JSON");
            }
            return list;
        }

        // ── Category Parser ───────────────────────────────────────────────
        private static UpdateCategory ParseCategory(string name)
        {
            if (string.IsNullOrEmpty(name)) return UpdateCategory.Unknown;
            name = name.ToLowerInvariant();

            if (name.Contains("security"))    return UpdateCategory.Security;
            if (name.Contains("critical"))    return UpdateCategory.Critical;
            if (name.Contains("driver"))      return UpdateCategory.Driver;
            if (name.Contains("feature"))     return UpdateCategory.FeatureUpdate;
            if (name.Contains("cumulative"))  return UpdateCategory.CumulativeUpdate;
            if (name.Contains("definition"))  return UpdateCategory.Definition;
            if (name.Contains("service pack"))return UpdateCategory.ServicePack;
            return UpdateCategory.Optional;
        }

        // ── JSON Helpers ──────────────────────────────────────────────────
        private static string GetStr(JsonElement el, string key)
        {
            try { return el.GetProperty(key).GetString() ?? ""; }
            catch { return ""; }
        }

        private static bool GetBool(JsonElement el, string key)
        {
            try { return el.GetProperty(key).GetBoolean(); }
            catch { return false; }
        }

        private static long GetLong(JsonElement el, string key)
        {
            try { return el.GetProperty(key).GetInt64(); }
            catch { return 0; }
        }

        private static int GetInt(JsonElement el, string key)
        {
            try { return el.GetProperty(key).GetInt32(); }
            catch { return 0; }
        }
        // Add this method to src/WUM.Core/Services/UpdateService.cs
// Paste it just before the last closing brace of the class

        // ── Diagnostics ───────────────────────────────────────────────────
        public async Task<string> DiagnoseAsync()
        {
            string script = @"
Write-Output '=== WUA Diagnostics ==='

# 1. WU + dependent services (BITS, dosvc, appidsvc, cryptsvc)
#    A Disabled StartType fails silently - flag it, not just Stopped.
$svcMap = [ordered]@{
    'WU Service   ' = 'wuauserv'
    'BITS         ' = 'bits'
    'Delivery Opt ' = 'dosvc'
    'App Identity ' = 'appidsvc'
    'Crypto Svc   ' = 'cryptsvc'
}
foreach ($label in $svcMap.Keys) {
    $name = $svcMap[$label]
    try {
        $svc = Get-Service -Name $name -ErrorAction Stop
        $start = (Get-CimInstance Win32_Service -Filter (""Name='"" + $name + ""'"") `
            -ErrorAction SilentlyContinue).StartMode
        $extra = ''
        if ($start -eq 'Disabled') { $extra = ' (DISABLED)' }
        Write-Output ($label + ': ' + $svc.Status + $extra)
    } catch {
        Write-Output ($label + ': NOT FOUND')
    }
}

# 2. Network connectivity to Windows Update
#    WU traffic is HTTPS/443 - ICMP is blocked on MS hosts, so ping
#    gives false UNREACHABLE. Probe real WU endpoints on TCP 443 instead.
try {
    $wuHosts = @(
        'sls.update.microsoft.com',
        'download.windowsupdate.com',
        'ctldl.windowsupdate.com'
    )
    $reachable = $false
    foreach ($h in $wuHosts) {
        try {
            $ok = Test-NetConnection -ComputerName $h -Port 443 `
                -InformationLevel Quiet -WarningAction SilentlyContinue
            if ($ok) { $reachable = $true; break }
        } catch { }
    }
    if ($reachable) {
        Write-Output 'Network (WU)  : Reachable'
    } else {
        Write-Output 'Network (WU)  : UNREACHABLE'
    }
} catch {
    Write-Output 'Network (WU)  : UNREACHABLE'
}

# 3. COM object creation
try {
    $Session = New-Object -ComObject Microsoft.Update.Session
    Write-Output 'COM Session   : OK'
} catch {
    Write-Output ('COM Session   : FAILED - ' + $_.Exception.Message)
    exit
}

# 4. Create searcher
try {
    $Searcher = $Session.CreateUpdateSearcher()
    $Searcher.Online = $true
    Write-Output 'COM Searcher  : Created OK'
} catch {
    Write-Output ('COM Searcher  : FAILED - ' + $_.Exception.Message)
    exit
}

# 4b. Registered update services (WU vs Microsoft Update vs WSUS/Store)
#     Default searcher hits Windows Update only. Microsoft Update service
#     (7971f918-a847-4430-9279-4a52d1efe18d) adds drivers + other MS products.
try {
    $sm = New-Object -ComObject Microsoft.Update.ServiceManager
    $muId = '7971f918-a847-4430-9279-4a52d1efe18d'
    $hasMu = $false
    foreach ($s in $sm.Services) {
        $flag = ''
        if ($s.IsDefaultAUService) { $flag = ' [default]' }
        if ($s.ServiceID -eq $muId) { $hasMu = $true; $flag += ' [Microsoft Update]' }
        Write-Output ('Service       : ' + $s.Name + $flag)
    }
    if ($hasMu) {
        Write-Output 'Microsoft Upd : Registered'
    } else {
        Write-Output 'Microsoft Upd : NOT registered (drivers/MS-product updates hidden)'
    }
} catch {
    Write-Output ('Service Mgr   : FAILED - ' + $_.Exception.Message)
}

# 5. Search for updates
try {
    Write-Output 'Searching     : Please wait...'
    $r = $Searcher.Search('IsInstalled=0 AND Type=''Software''')
    Write-Output ('Updates Found : ' + $r.Updates.Count)
    if ($r.Updates.Count -gt 0) {
        foreach ($u in $r.Updates) {
            $kb = ''
            if ($u.KBArticleIDs.Count -gt 0) { $kb = 'KB' + $u.KBArticleIDs[0] }
            Write-Output ('  -> ' + $kb.PadRight(14) + $u.Title.Substring(0, [Math]::Min(60, $u.Title.Length)))
        }
    }
} catch {
    Write-Output ('Search Error  : ' + $_.Exception.Message)
}

# 6. Proxy info
try {
    $proxy = [System.Net.WebRequest]::GetSystemWebProxy()
    $uri   = $proxy.GetProxy('http://windowsupdate.microsoft.com')
    Write-Output ('Proxy         : ' + $uri)
} catch {
    Write-Output 'Proxy         : Unable to determine'
}

# 7. Last WU check time
try {
    $path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect'
    $val  = (Get-ItemProperty -Path $path -ErrorAction SilentlyContinue).LastSuccessTime
    if ($val) {
        Write-Output ('Last WU Check : ' + $val)
    } else {
        Write-Output 'Last WU Check : Never or unknown'
    }
} catch {
    Write-Output 'Last WU Check : Unable to read'
}

# 8. Windows Update registry settings
try {
    $auPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
    $au     = Get-ItemProperty -Path $auPath -ErrorAction SilentlyContinue
    if ($au) {
        Write-Output ('WU AU Options : ' + $au.AUOptions)
        Write-Output ('WU No AutoUpd : ' + $au.NoAutoUpdate)
    } else {
        Write-Output 'WU AU Options : Not configured (default)'
    }
} catch {
    Write-Output 'WU AU Options : Unable to read'
}

# 8b. Update source: WSUS vs Microsoft Update (explains '0 found' on managed PCs)
try {
    $wuPath   = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'
    $wu       = Get-ItemProperty -Path $wuPath -ErrorAction SilentlyContinue
    $auPath2  = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
    $au2      = Get-ItemProperty -Path $auPath2 -ErrorAction SilentlyContinue
    $useWsus  = $au2 -and ($au2.UseWUServer -eq 1)
    if ($useWsus -and $wu -and $wu.WUServer) {
        Write-Output ('Update Source : WSUS (' + $wu.WUServer + ')')
    } elseif ($useWsus) {
        Write-Output 'Update Source : WSUS (policy set, server URL missing)'
    } else {
        Write-Output 'Update Source : Microsoft Update (direct)'
    }
} catch {
    Write-Output 'Update Source : Unable to read'
}

# 9. Admin check
$id        = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
$isAdmin   = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output ('Running Admin : ' + $isAdmin)

# 10. OS Version
$os = Get-WmiObject -Class Win32_OperatingSystem -ErrorAction SilentlyContinue
if ($os) {
    Write-Output ('OS Version    : ' + $os.Caption + ' ' + $os.Version)
}

Write-Output ''
Write-Output '=== End Diagnostics ==='
";
            try
            {
                var (_, output, error) =
                    await PowerShellHelper.RunScriptAsync(script);

                string result = output ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(error))
                    result += "\nPS Errors:\n" + error;

                return result;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "DiagnoseAsync failed");
                return "Diagnostics failed: " + ex.Message;
            }
        }

        // ── WU component reset (destructive) ──────────────────────────────
        // Mirrors PSWindowsUpdate Reset-WUComponents: stop services, clear the
        // SoftwareDistribution + Catroot2 caches, reregister DLLs, reset network
        // stacks, restart services. Caller must enforce admin + confirmation.
        public async Task<string> ResetComponentsAsync()
        {
            string script = @"
$ErrorActionPreference = 'Continue'
$svcs = @('bits','wuauserv','appidsvc','cryptsvc')

Write-Output 'Step 1: Stop Windows Update services'
foreach ($s in $svcs) {
    try { Stop-Service -Name $s -Force -ErrorAction Stop; Write-Output ('  stopped ' + $s) }
    catch { Write-Output ('  could not stop ' + $s + ' - ' + $_.Exception.Message) }
}

Write-Output 'Step 2: Delete qmgr*.dat (BITS queue)'
$dl = Join-Path $env:ALLUSERSPROFILE 'Microsoft\Network\Downloader'
Remove-Item (Join-Path $dl 'qmgr*.dat') -Force -ErrorAction SilentlyContinue

Write-Output 'Step 3: Rename SoftwareDistribution + Catroot2'
$sd = Join-Path $env:windir 'SoftwareDistribution'
$cr = Join-Path $env:windir 'System32\catroot2'
foreach ($p in @($sd,$cr)) {
    if (Test-Path $p) {
        $bak = $p + '.bak'
        if (Test-Path $bak) { Remove-Item $bak -Recurse -Force -ErrorAction SilentlyContinue }
        try { Rename-Item $p $bak -Force -ErrorAction Stop; Write-Output ('  renamed ' + $p) }
        catch { Write-Output ('  could not rename ' + $p + ' - ' + $_.Exception.Message) }
    }
}

Write-Output 'Step 4: Reregister Windows Update DLLs'
$dlls = @('atl.dll','urlmon.dll','mshtml.dll','wuapi.dll','wuaueng.dll',
         'wups.dll','wups2.dll','wuwebv.dll','wucltux.dll','qmgr.dll',
         'qmgrprxy.dll','jscript.dll','vbscript.dll')
foreach ($d in $dlls) {
    Start-Process -FilePath 'regsvr32.exe' -ArgumentList ('/s ' + $d) `
        -NoNewWindow -Wait -ErrorAction SilentlyContinue
}
Write-Output ('  reregistered ' + $dlls.Count + ' DLLs')

Write-Output 'Step 5: Reset WinSock + WinHTTP proxy'
netsh winsock reset | Out-Null
netsh winhttp reset proxy | Out-Null

Write-Output 'Step 6: Start Windows Update services'
foreach ($s in ($svcs[($svcs.Count-1)..0])) {
    try { Start-Service -Name $s -ErrorAction Stop; Write-Output ('  started ' + $s) }
    catch { Write-Output ('  could not start ' + $s + ' - ' + $_.Exception.Message) }
}

Write-Output 'Step 7: Trigger update detection'
try { Start-Process -FilePath 'wuauclt.exe' -ArgumentList '/resetauthorization /detectnow' -NoNewWindow -ErrorAction SilentlyContinue } catch { }
try { Start-Process -FilePath 'UsoClient.exe' -ArgumentList 'StartScan' -NoNewWindow -ErrorAction SilentlyContinue } catch { }

Write-Output 'Reset complete. A reboot is recommended.'
";
            try
            {
                _log.Information("ResetComponentsAsync started");
                var (_, output, error) =
                    await PowerShellHelper.RunScriptAsync(script);

                string result = output ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(error))
                    result += "\nPS Errors:\n" + error;

                return result;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "ResetComponentsAsync failed");
                return "Reset failed: " + ex.Message;
            }
        }
    }
}