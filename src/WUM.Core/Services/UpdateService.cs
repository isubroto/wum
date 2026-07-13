// src/WUM.Core/Services/UpdateService.cs
// Complete rewrite using PowerShell + WUA scripting
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

        // Set by Download/Install on failure; surfaced to the CLI.
        public string? LastError { get; private set; }

        public UpdateService(RegistryHelper registry)
        {
            _registry = registry;
            _log      = Log.ForContext<UpdateService>();
            RemoveLegacyScanCache();
        }

        // ── WUA ResultCode → human reason ─────────────────────────────────
        // 0=NotStarted 1=InProgress 2=Succeeded 3=SucceededWithErrors
        // 4=Failed 5=Aborted
        private static string ResultCodeReason(string code) => code switch
        {
            "4" => "WUA reported Failed (ResultCode 4)",
            "5" => "operation aborted (ResultCode 5)",
            "0" => "operation never started (ResultCode 0)",
            "1" => "still in progress when polled (ResultCode 1)",
            _   => "unexpected WUA ResultCode " + code,
        };

        // ── Get Available Updates ─────────────────────────────────────────
        public async Task<List<WindowsUpdate>> GetAvailableUpdatesAsync(
            bool includeHidden      = false,
            bool useMicrosoftUpdate = false,
            bool forceRefresh       = false,
            CancellationToken ct    = default)
        {
            try
            {
                if (forceRefresh)
                    await RequestUserInitiatedRefreshAsync(ct);

                // Capture Settings/orchestrator state before starting the
                // legacy online search. WUA can block behind an active feature
                // update; this snapshot keeps that live update visible if the
                // catalog search times out or fails.
                OrchestratorSnapshot retainedOrchestrator =
                    await QueryOrchestratorSnapshotAsync(ct);
                bool orchestratorCoversQuery = OrchestratorCoversQuery(
                    retainedOrchestrator.Available,
                    includeHidden,
                    useMicrosoftUpdate);

                // GetApplicableUpdates is the Settings-backed, effective
                // default view. Do not block a live download/install/reboot or
                // attention state behind WUA catalog enrichment. Expanded
                // hidden/Microsoft Update queries still require WUA below.
                if (orchestratorCoversQuery && HasLiveOrchestratorState(
                        retainedOrchestrator.Updates))
                {
                    _log.Information(
                        "Returning {Count} live orchestrator update(s) without waiting for WUA enrichment",
                        retainedOrchestrator.Updates.Count);
                    return retainedOrchestrator.Updates;
                }

                string criteria = includeHidden
                    ? "IsInstalled=0"
                    : "IsInstalled=0 AND IsHidden=0";

                string script = @"
$ErrorActionPreference = 'Stop'
$Session = New-Object -ComObject Microsoft.Update.Session
$Session.ClientApplicationID = 'WUM'
$script:Updates = @()
$script:Seen = @{}

function Add-WumSearchResults([string]$source, [int]$serverSelection, [string]$serviceId) {
    $searcher = $Session.CreateUpdateSearcher()
    $searcher.Online = $true
    # Explicitly request normal applicable results. Modern Windows ignores
    # this legacy property, so tolerate E_NOTIMPL while keeping intent clear.
    try { $searcher.IncludePotentiallySupersededUpdates = $false } catch { }
    if ($serverSelection -eq 3) {
        $searcher.ServerSelection = 3
        $searcher.ServiceID = $serviceId
    } else {
        $searcher.ServerSelection = 0
    }

    $results = $searcher.Search('" + criteria + @"')
    $resultCode = [int]$results.ResultCode
    if ($resultCode -ne 2 -and $resultCode -ne 3) {
        throw ('WUA search failed for ' + $source + ' (ResultCode ' + $resultCode + ')')
    }
    foreach ($warning in $results.Warnings) {
        [Console]::Error.WriteLine(
            'WUA search warning for ' + $source + ': ' +
            ('0x{0:X8}' -f [int]$warning.HResult) + ' ' + $warning.Message)
    }

    foreach ($u in $results.Updates) {
        $key = $u.Identity.UpdateID + ':' + $u.Identity.RevisionNumber
        if ($script:Seen.ContainsKey($key)) { continue }
        $script:Seen[$key] = $true

        $kb = ''
        if ($u.KBArticleIDs.Count -gt 0) { $kb = 'KB' + $u.KBArticleIDs[0] }
        $categoryNames = @()
        $categoryIds = @()
        foreach ($category in $u.Categories) {
            $categoryNames += $category.Name
            $categoryIds += $category.CategoryID
        }
        $supersededIds = @()
        try { foreach ($id in $u.SupersededUpdateIDs) { $supersededIds += $id } } catch { }
        $actualReboot = $false
        try { $actualReboot = [bool]$u.RebootRequired } catch { }
        $browseOnly = $false
        try { $browseOnly = [bool]$u.BrowseOnly } catch { }
        $updateType = switch ([int]$u.Type) { 1 { 'Software' } 2 { 'Driver' } default { $u.Type.ToString() } }

        $script:Updates += [PSCustomObject]@{
            Id                  = $u.Identity.UpdateID
            RevisionNumber      = [int]$u.Identity.RevisionNumber
            ServerSelection     = $serverSelection
            ServiceId           = $serviceId
            Source              = $source
            Title               = $u.Title
            Description         = $u.Description
            KBArticle           = $kb
            UpdateType          = $updateType
            CategoryNames       = @($categoryNames)
            CategoryIds         = @($categoryIds)
            IsInstalled         = [bool]$u.IsInstalled
            IsHidden            = [bool]$u.IsHidden
            IsDownloaded        = [bool]$u.IsDownloaded
            BrowseOnly          = $browseOnly
            IsBeta              = [bool]$u.IsBeta
            IsMandatory         = [bool]$u.IsMandatory
            SizeInBytes         = [long]$u.MaxDownloadSize
            SupportUrl          = $u.SupportUrl
            Severity            = $u.MsrcSeverity
            RequiresReboot      = $actualReboot
            MayRequireReboot    = ([int]$u.InstallationBehavior.RebootBehavior -ne 0)
            ReleaseDate         = $u.LastDeploymentChangeTime.ToString('o')
            SupersededUpdateIds = @($supersededIds)
            IsOrchestratorUpdate          = $false
            OrchestratorUpdateId          = ''
            OrchestratorRevisionNumber    = 0
            OrchestratorProviderId        = ''
            CurrentAction                 = ''
            OrchestratorProgress          = 0.0
            HasOrchestratorActionResult   = $false
            OrchestratorActionResultAction= ''
            OrchestratorActionSucceeded   = $null
            OrchestratorResultHResult     = 0
            OrchestratorResultError       = ''
            OrchestratorActionTimestamp   = $null
            AttentionRequiredReason       = ''
            AttentionRequiredTimestamp    = $null
        }
    }
}

# ssDefault honors the device's effective WUA/WSUS policy and includes OS
# flighting when the Service Locator makes it applicable to an Insider device.
Add-WumSearchResults 'Default' 0 ''
" + (useMicrosoftUpdate ? @"
$muId = '7971f918-a847-4430-9279-4a52d1efe18d'
$serviceManager = New-Object -ComObject Microsoft.Update.ServiceManager
$mu = $serviceManager.Services | Where-Object { $_.ServiceID -eq $muId } | Select-Object -First 1
if ($mu) {
    Add-WumSearchResults 'Microsoft Update' 3 $muId
} else {
    [Console]::Error.WriteLine('Microsoft Update service is not registered; default WUA results were retained.')
}
" : "") + @"
ConvertTo-Json -InputObject @($script:Updates) -Depth 6 -Compress
";
                List<WindowsUpdate> wuaUpdates;
                try
                {
                    using var cts = CancellationTokenSource
                        .CreateLinkedTokenSource(ct);
                    cts.CancelAfter(orchestratorCoversQuery
                        ? TimeSpan.FromSeconds(30)
                        : TimeSpan.FromMinutes(5));

                    var (success, output, error) =
                        await PowerShellHelper.RunScriptAsync(
                            script, ct: cts.Token);

                    if (!success || string.IsNullOrWhiteSpace(output))
                    {
                        string message = string.IsNullOrWhiteSpace(error)
                            ? "WUA search returned no JSON output."
                            : error.Trim();
                        throw new InvalidOperationException(message);
                    }

                    wuaUpdates = ParseUpdatesFromJson(output);
                }
                catch (OperationCanceledException ex)
                    when (!ct.IsCancellationRequested)
                {
                    if (orchestratorCoversQuery)
                    {
                        _log.Warning(
                            ex,
                            "WUA enrichment timed out; returning authoritative default orchestrator view with {Count} update(s)",
                            retainedOrchestrator.Updates.Count);
                        return retainedOrchestrator.Updates;
                    }

                    throw new TimeoutException(
                        "The online Windows Update Agent search timed out after five minutes, " +
                        "and no complete orchestrator fallback was available for this query.",
                        ex);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (orchestratorCoversQuery)
                    {
                        _log.Warning(
                            ex,
                            "WUA enrichment failed; returning authoritative default orchestrator view with {Count} update(s)",
                            retainedOrchestrator.Updates.Count);
                        return retainedOrchestrator.Updates;
                    }

                    throw;
                }

                // WUA may take long enough for download/install progress to
                // change. Re-read the lightweight orchestrator state before
                // merging; if that read is unavailable, use the retained
                // snapshot rather than dropping known live state.
                OrchestratorSnapshot currentOrchestrator =
                    await QueryOrchestratorSnapshotAsync(ct);
                List<WindowsUpdate> overlay = currentOrchestrator.Available
                    ? currentOrchestrator.Updates
                    : retainedOrchestrator.Updates;
                List<WindowsUpdate> updates = MergeOrchestratorUpdates(
                    wuaUpdates, overlay);

                _log.Information(
                    "Fresh update scan completed. Updates={Count}, WuaUpdates={WuaCount}, OrchestratorUpdates={OrchestratorCount}, IncludeHidden={Hidden}, MicrosoftUpdate={Mu}, UserRefresh={Refresh}",
                    updates.Count, wuaUpdates.Count, overlay.Count,
                    includeHidden, useMicrosoftUpdate, forceRefresh);
                return updates;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Error(ex, "Error fetching available updates");
                throw;
            }
        }

        private async Task RequestUserInitiatedRefreshAsync(
            CancellationToken ct)
        {
            using var cts = CancellationTokenSource
                .CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(6));

            try
            {
                var (success, _, error) =
                    await PowerShellHelper.RunScriptAsync(
                        BuildUserInitiatedRefreshScript(), ct: cts.Token);
                if (!success)
                {
                    _log.Warning(
                        "User-initiated orchestrator refresh failed; continuing with live queries: {Error}",
                        string.IsNullOrWhiteSpace(error)
                            ? "PowerShell returned an error"
                            : error.Trim());
                }
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested)
            {
                _log.Warning(
                    "User-initiated orchestrator refresh exceeded its six-minute safety bound; continuing with live queries");
            }
        }

        private sealed record OrchestratorSnapshot(
            bool Available,
            List<WindowsUpdate> Updates)
        {
            internal static readonly OrchestratorSnapshot Unavailable =
                new(false, new List<WindowsUpdate>());
        }

        internal static bool HasLiveOrchestratorState(
            List<WindowsUpdate> updates) => updates.Any(update =>
                update.Status == UpdateStatus.Downloading ||
                update.Status == UpdateStatus.Installing ||
                update.Status == UpdateStatus.PendingInstallation ||
                update.Status == UpdateStatus.RestartRequired ||
                update.Status == UpdateStatus.PendingReboot ||
                update.Status == UpdateStatus.Failed ||
                (!string.IsNullOrWhiteSpace(update.CurrentAction) &&
                    !update.CurrentAction.Equals(
                        "None", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(
                        update.AttentionRequiredReason) &&
                    !update.AttentionRequiredReason.Equals(
                        "None", StringComparison.OrdinalIgnoreCase)));

        internal static bool OrchestratorCoversQuery(
            bool available,
            bool includeHidden,
            bool useMicrosoftUpdate) =>
            available && !includeHidden && !useMicrosoftUpdate;

        private async Task<OrchestratorSnapshot>
            QueryOrchestratorSnapshotAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource
                .CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                var (success, output, error) =
                    await PowerShellHelper.RunScriptAsync(
                        BuildOrchestratorSnapshotScript(), ct: cts.Token);
                if (!success || string.IsNullOrWhiteSpace(output))
                {
                    _log.Warning(
                        "Windows Update orchestrator snapshot was unavailable: {Error}",
                        string.IsNullOrWhiteSpace(error)
                            ? "no JSON output"
                            : error.Trim());
                    return OrchestratorSnapshot.Unavailable;
                }

                using var doc = JsonDocument.Parse(output);
                JsonElement root = doc.RootElement;
                bool available = root.TryGetProperty(
                        "Available", out JsonElement availableElement) &&
                    availableElement.ValueKind is JsonValueKind.True;
                if (!root.TryGetProperty(
                        "Updates", out JsonElement updatesElement))
                {
                    _log.Warning(
                        "Windows Update orchestrator snapshot contained no Updates field");
                    return OrchestratorSnapshot.Unavailable;
                }

                List<WindowsUpdate> updates = ParseUpdatesFromJson(
                    updatesElement.GetRawText());
                _log.Debug(
                    "Windows Update orchestrator snapshot completed. Available={Available}, Updates={Count}",
                    available, updates.Count);
                return new OrchestratorSnapshot(available, updates);
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested)
            {
                _log.Warning(
                    "Windows Update orchestrator snapshot timed out after 10 seconds");
                return OrchestratorSnapshot.Unavailable;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warning(
                    ex,
                    "Windows Update orchestrator snapshot could not be parsed");
                return OrchestratorSnapshot.Unavailable;
            }
        }

        internal static string BuildOrchestratorSnapshotScript() => @"
$ErrorActionPreference = 'Stop'
$script:Updates = @()
$script:Seen = @{}
" + BuildOrchestratorOverlayScript() + @"
[PSCustomObject]@{
    Available = [bool]$script:OrchestratorAvailable
    Updates   = @($script:Updates)
} | ConvertTo-Json -Depth 7 -Compress
";

        internal static List<WindowsUpdate> MergeOrchestratorUpdates(
            List<WindowsUpdate> wuaUpdates,
            List<WindowsUpdate> orchestratorUpdates)
        {
            ArgumentNullException.ThrowIfNull(wuaUpdates);
            ArgumentNullException.ThrowIfNull(orchestratorUpdates);

            var merged = new List<WindowsUpdate>(wuaUpdates);
            foreach (WindowsUpdate modern in orchestratorUpdates)
            {
                List<WindowsUpdate> candidates = merged
                    .Where(update => string.Equals(
                        update.Id, modern.Id,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                WindowsUpdate? target = candidates.Count switch
                {
                    0 => null,
                    1 => candidates[0],
                    _ => modern.OrchestratorRevisionNumber > 0
                        ? candidates.FirstOrDefault(update =>
                            update.RevisionNumber ==
                                modern.OrchestratorRevisionNumber)
                        : null
                };
                if (target is null && candidates.Count > 0)
                {
                    target = candidates
                        .OrderByDescending(update => update.RevisionNumber)
                        .First();
                }

                if (target is null)
                {
                    merged.Add(modern);
                    continue;
                }

                ApplyOrchestratorState(target, modern);
            }

            return merged;
        }

        private static void ApplyOrchestratorState(
            WindowsUpdate target,
            WindowsUpdate modern)
        {
            target.IsOrchestratorUpdate = true;
            target.OrchestratorUpdateId = modern.OrchestratorUpdateId;
            target.OrchestratorRevisionNumber =
                modern.OrchestratorRevisionNumber;
            target.OrchestratorProviderId = modern.OrchestratorProviderId;
            target.CurrentAction = modern.CurrentAction;
            target.OrchestratorProgress = modern.OrchestratorProgress;
            target.IsDownloaded =
                target.IsDownloaded || modern.IsDownloaded;
            target.DownloadProgress = modern.DownloadProgress;
            target.InstallProgress = modern.InstallProgress;
            target.HasOrchestratorActionResult =
                modern.HasOrchestratorActionResult;
            target.OrchestratorActionResultAction =
                modern.OrchestratorActionResultAction;
            target.OrchestratorActionSucceeded =
                modern.OrchestratorActionSucceeded;
            target.OrchestratorResultHResult =
                modern.OrchestratorResultHResult;
            target.OrchestratorResultError =
                modern.OrchestratorResultError;
            target.OrchestratorActionTimestamp =
                modern.OrchestratorActionTimestamp;
            target.AttentionRequiredReason =
                modern.AttentionRequiredReason;
            target.AttentionRequiredTimestamp =
                modern.AttentionRequiredTimestamp;
            target.RequiresReboot =
                target.RequiresReboot || modern.RequiresReboot;
            target.Status = MapStatus(
                target.IsInstalled,
                target.IsHidden,
                target.IsDownloaded,
                target.RequiresReboot,
                target.CurrentAction,
                target.HasOrchestratorActionResult,
                target.OrchestratorActionSucceeded,
                target.AttentionRequiredReason);
        }

        // ── Get Installed Updates ─────────────────────────────────────────
        public async Task<List<WindowsUpdate>> GetInstalledUpdatesAsync(
            CancellationToken ct = default)
        {
            try
            {
                string script = @"
$ErrorActionPreference = 'Stop'
$Session  = New-Object -ComObject Microsoft.Update.Session
$Session.ClientApplicationID = 'WUM'
$Searcher = $Session.CreateUpdateSearcher()
$Searcher.Online = $true
$Searcher.ServerSelection = 0
try { $Searcher.IncludePotentiallySupersededUpdates = $false } catch { }
$Results  = $Searcher.Search('IsInstalled=1')
if ([int]$Results.ResultCode -ne 2 -and [int]$Results.ResultCode -ne 3) {
    throw ('WUA installed-update search failed (ResultCode ' + [int]$Results.ResultCode + ')')
}
$Updates  = @()
foreach ($u in $Results.Updates) {
    $kb  = ''
    if ($u.KBArticleIDs.Count -gt 0) { $kb = 'KB' + $u.KBArticleIDs[0] }
    $categoryNames = @()
    $categoryIds = @()
    foreach ($category in $u.Categories) {
        $categoryNames += $category.Name
        $categoryIds += $category.CategoryID
    }
    $actualReboot = $false
    try { $actualReboot = [bool]$u.RebootRequired } catch { }
    $browseOnly = $false
    try { $browseOnly = [bool]$u.BrowseOnly } catch { }
    $updateType = switch ([int]$u.Type) { 1 { 'Software' } 2 { 'Driver' } default { $u.Type.ToString() } }
    $Updates += [PSCustomObject]@{
        Id               = $u.Identity.UpdateID
        RevisionNumber   = [int]$u.Identity.RevisionNumber
        ServerSelection  = 0
        ServiceId        = ''
        Source           = 'Default'
        Title            = $u.Title
        Description      = $u.Description
        KBArticle        = $kb
        UpdateType       = $updateType
        CategoryNames    = @($categoryNames)
        CategoryIds      = @($categoryIds)
        IsInstalled      = [bool]$u.IsInstalled
        IsHidden         = [bool]$u.IsHidden
        IsDownloaded     = [bool]$u.IsDownloaded
        BrowseOnly       = $browseOnly
        IsBeta           = [bool]$u.IsBeta
        IsMandatory      = [bool]$u.IsMandatory
        SizeInBytes      = [long]$u.MaxDownloadSize
        SupportUrl       = $u.SupportUrl
        Severity         = $u.MsrcSeverity
        RequiresReboot   = $actualReboot
        MayRequireReboot = ([int]$u.InstallationBehavior.RebootBehavior -ne 0)
        ReleaseDate      = $u.LastDeploymentChangeTime.ToString('o')
    }
}
ConvertTo-Json -InputObject @($Updates) -Depth 6 -Compress
";
                using var cts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(5));

                var (success, output, error) =
                    await PowerShellHelper.RunScriptAsync(script, ct: cts.Token);

                if (!success || string.IsNullOrWhiteSpace(output))
                {
                    string message = string.IsNullOrWhiteSpace(error)
                        ? "WUA installed-update search returned no JSON output."
                        : error.Trim();
                    _log.Error("Installed-update scan failed: {Error}", message);
                    throw new InvalidOperationException(message);
                }

                return ParseUpdatesFromJson(output, installed: true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Error(ex, "Error fetching installed updates");
                throw;
            }
        }

        // ── Get History ───────────────────────────────────────────────────
        public async Task<List<UpdateHistory>> GetUpdateHistoryAsync(
            int count = 50,
            CancellationToken ct = default)
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
                    await PowerShellHelper.RunScriptAsync(script, ct: ct);

                if (string.IsNullOrWhiteSpace(output))
                    return new List<UpdateHistory>();

                return ParseHistoryFromJson(output);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Error(ex, "Error fetching update history");
                return new List<UpdateHistory>();
            }
        }

        // ── Download ──────────────────────────────────────────────────────
        public Task<bool> DownloadUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default)
            => DownloadUpdateAsync(
                new WindowsUpdate { Id = updateId }, progress, ct);

        public async Task<bool> DownloadUpdateAsync(
            WindowsUpdate update,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default)
        {
            LastError = null;
            ArgumentNullException.ThrowIfNull(update);

            try
            {
                update.Status = UpdateStatus.Downloading;
                progress?.Report(5);

                string criteria = BuildIdentityCriteria(update);
                string source   = BuildSearcherSource(update);
                string script = @"
$ErrorActionPreference = 'Stop'
$Session  = New-Object -ComObject Microsoft.Update.Session
$Session.ClientApplicationID = 'WUM'
$Searcher = $Session.CreateUpdateSearcher()
$Searcher.Online = $true
try { $Searcher.IncludePotentiallySupersededUpdates = $false } catch { }
" + source + @"
$Results  = $Searcher.Search(" + PsQuote(criteria) + @")
$SearchCode = [int]$Results.ResultCode
if ($SearchCode -ne 2 -and $SearchCode -ne 3) {
    throw ('WUA download lookup failed (ResultCode ' + $SearchCode + ')')
}
$Update   = $Results.Updates | Where-Object { -not $_.IsInstalled } | Select-Object -First 1
if (-not $Update) {
    [PSCustomObject]@{ Found = $false } | ConvertTo-Json -Compress
    exit
}
try { if (-not $Update.EulaAccepted) { $Update.AcceptEula() } } catch { }
$Coll = New-Object -ComObject Microsoft.Update.UpdateColl
[void]$Coll.Add($Update)
$Downloader         = $Session.CreateUpdateDownloader()
$Downloader.ClientApplicationID = 'WUM'
$Downloader.Updates = $Coll
$Result             = $Downloader.Download()
$PerUpdate          = $Result.GetUpdateResult(0)
[PSCustomObject]@{
    Found             = $true
    OverallResultCode = [int]$Result.ResultCode
    ResultCode        = [int]$PerUpdate.ResultCode
    HResult           = [int]$PerUpdate.HResult
    IsDownloaded      = [bool]$Update.IsDownloaded
} | ConvertTo-Json -Compress
";
                var (success, output, error) =
                    await PowerShellHelper.RunScriptAsync(
                        script, ct: ct);

                progress?.Report(100);

                if (!success || string.IsNullOrWhiteSpace(output))
                {
                    update.Status = UpdateStatus.Failed;
                    LastError = string.IsNullOrWhiteSpace(error)
                        ? "Windows Update returned no download result."
                        : error.Trim();
                    return false;
                }

                UpdateOperationOutcome outcome =
                    ParseOperationOutcome(output);
                _log.Information(
                    "Download result for {Id} r{Revision} from {Source}: Overall={Overall}, Update={Code} (0x{HResult:X8}), Downloaded={Downloaded}",
                    update.Id, update.RevisionNumber, update.Source,
                    outcome.OverallResultCode, outcome.ResultCode,
                    unchecked((uint)outcome.HResult),
                    outcome.IsDownloaded);

                if (!outcome.Found)
                {
                    update.Status = UpdateStatus.SupersededOrNotApplicable;
                    LastError = "update is no longer offered by its scan source — " +
                        "it may already be installed, superseded, or not applicable";
                    return false;
                }

                bool overallSucceeded =
                    outcome.OverallResultCode == 2 ||
                    outcome.OverallResultCode == 3;
                if (overallSucceeded &&
                    (outcome.ResultCode == 2 || outcome.ResultCode == 3) &&
                    outcome.IsDownloaded)
                {
                    update.IsDownloaded = true;
                    update.Status = UpdateStatus.PendingInstallation;
                    return true;
                }

                update.Status = UpdateStatus.Failed;
                LastError = FormatOperationFailure(
                    "download",
                    IsSuccessfulResult(outcome.ResultCode)
                        ? outcome.OverallResultCode
                        : outcome.ResultCode,
                    outcome.HResult, error);
                return false;
            }
            catch (OperationCanceledException)
            {
                update.Status = UpdateStatus.Failed;
                LastError = "download was cancelled or timed out";
                throw;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Download failed for {UpdateId}", update.Id);
                update.Status = UpdateStatus.Failed;
                LastError = ex.Message;
                return false;
            }
        }

        // ── Install ───────────────────────────────────────────────────────
        public Task<bool> InstallUpdateAsync(
            string updateId,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default)
            => InstallUpdateAsync(
                new WindowsUpdate { Id = updateId }, progress, ct);

        public async Task<bool> InstallUpdateAsync(
            WindowsUpdate update,
            IProgress<double>? progress = null,
            CancellationToken  ct       = default)
        {
            LastError = null;
            ArgumentNullException.ThrowIfNull(update);

            try
            {
                update.Status = UpdateStatus.Installing;
                progress?.Report(5);

                string criteria = BuildIdentityCriteria(update);
                string source   = BuildSearcherSource(update, "$Searcher");
                string script = @"
$ErrorActionPreference = 'Stop'
$Session  = New-Object -ComObject Microsoft.Update.Session
$Session.ClientApplicationID = 'WUM'
$Searcher = $Session.CreateUpdateSearcher()
$Searcher.Online = $true
try { $Searcher.IncludePotentiallySupersededUpdates = $false } catch { }
" + source + @"
$Results  = $Searcher.Search(" + PsQuote(criteria) + @")
$SearchCode = [int]$Results.ResultCode
if ($SearchCode -ne 2 -and $SearchCode -ne 3) {
    throw ('WUA install lookup failed (ResultCode ' + $SearchCode + ')')
}
$Update   = $Results.Updates | Where-Object { -not $_.IsInstalled } | Select-Object -First 1
if (-not $Update) {
    [PSCustomObject]@{ Found = $false } | ConvertTo-Json -Compress
    exit
}
$Coll = New-Object -ComObject Microsoft.Update.UpdateColl
[void]$Coll.Add($Update)
$Installer         = $Session.CreateUpdateInstaller()
$Installer.ClientApplicationID = 'WUM'
$Installer.Updates = $Coll
if ([bool]$Installer.RebootRequiredBeforeInstallation) {
    [PSCustomObject]@{
        Found                            = $true
        RebootRequiredBeforeInstallation = $true
    } | ConvertTo-Json -Compress
    exit
}
$Result            = $Installer.Install()
$PerUpdate         = $Result.GetUpdateResult(0)

# Re-query the exact identity from the exact source. This is deliberately an
# online verification: successful, fully applicable installs must disappear;
# staged installs may remain only when WUA reports a real pending reboot.
$VerifySearcher = $Session.CreateUpdateSearcher()
$VerifySearcher.Online = $true
try { $VerifySearcher.IncludePotentiallySupersededUpdates = $false } catch { }
" + BuildSearcherSource(update, "$VerifySearcher") + @"
$VerifyResult = $VerifySearcher.Search(" + PsQuote(criteria) + @")
$VerifyCode = [int]$VerifyResult.ResultCode
if ($VerifyCode -ne 2 -and $VerifyCode -ne 3) {
    throw ('Post-install verification scan failed (ResultCode ' + $VerifyCode + ')')
}
$StillApplicable = @($VerifyResult.Updates | Where-Object { -not $_.IsInstalled }).Count -gt 0

[PSCustomObject]@{
    Found             = $true
    OverallResultCode = [int]$Result.ResultCode
    ResultCode        = [int]$PerUpdate.ResultCode
    HResult           = [int]$PerUpdate.HResult
    RebootRequired    = ([bool]$Result.RebootRequired -or [bool]$PerUpdate.RebootRequired)
    IsInstalled       = [bool]$Update.IsInstalled
    StillApplicable   = $StillApplicable
} | ConvertTo-Json -Compress
";
                var (success, output, error) =
                    await PowerShellHelper.RunScriptAsync(
                        script, ct: ct);

                progress?.Report(100);

                if (!success || string.IsNullOrWhiteSpace(output))
                {
                    update.Status = UpdateStatus.Failed;
                    LastError = string.IsNullOrWhiteSpace(error)
                        ? "Windows Update returned no installation result."
                        : error.Trim();
                    return false;
                }

                UpdateOperationOutcome outcome =
                    ParseOperationOutcome(output);
                _log.Information(
                    "Install result for {Id} r{Revision} from {Source}: Overall={Overall}, Update={Code} (0x{HResult:X8}), Reboot={Reboot}, StillApplicable={Applicable}",
                    update.Id, update.RevisionNumber, update.Source,
                    outcome.OverallResultCode, outcome.ResultCode,
                    unchecked((uint)outcome.HResult),
                    outcome.RebootRequired, outcome.StillApplicable);

                if (!outcome.Found)
                {
                    update.Status = UpdateStatus.SupersededOrNotApplicable;
                    LastError = "update is no longer offered by its scan source — " +
                        "it may already be installed, superseded, or not applicable";
                    return false;
                }

                if (outcome.RebootRequiredBeforeInstallation)
                {
                    update.RequiresReboot = true;
                    update.Status = UpdateStatus.RestartRequired;
                    LastError = "Windows Update requires a restart before this " +
                        "update can be installed";
                    return false;
                }

                bool operationSucceeded =
                    (outcome.OverallResultCode == 2 ||
                     outcome.OverallResultCode == 3) &&
                    (outcome.ResultCode == 2 ||
                     outcome.ResultCode == 3);
                bool stateConfirmed =
                    !outcome.StillApplicable || outcome.RebootRequired;

                if (operationSucceeded && stateConfirmed)
                {
                    update.RequiresReboot = outcome.RebootRequired;
                    update.IsInstalled = !outcome.StillApplicable;
                    update.Status = outcome.RebootRequired
                        ? UpdateStatus.RestartRequired
                        : UpdateStatus.Installed;
                    return true;
                }

                update.Status = UpdateStatus.Failed;
                LastError = operationSucceeded
                    ? "Windows Update completed the installer, but a fresh scan " +
                      "still reports the update as applicable"
                    : FormatOperationFailure(
                        "installation",
                        IsSuccessfulResult(outcome.ResultCode)
                            ? outcome.OverallResultCode
                            : outcome.ResultCode,
                        outcome.HResult, error);
                return false;
            }
            catch (OperationCanceledException)
            {
                update.Status = UpdateStatus.Failed;
                LastError = "installation was cancelled or timed out";
                throw;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Install failed for {UpdateId}", update.Id);
                update.Status = UpdateStatus.Failed;
                LastError = ex.Message;
                return false;
            }
        }

        internal static string BuildIdentityCriteria(WindowsUpdate update)
        {
            if (string.IsNullOrWhiteSpace(update.Id))
                throw new ArgumentException("Update ID is required.", nameof(update));

            string criteria = "UpdateID='" +
                update.Id.Replace("'", "''", StringComparison.Ordinal) + "'";
            if (update.RevisionNumber > 0)
                criteria += " AND RevisionNumber=" + update.RevisionNumber;
            return criteria;
        }

        internal static string BuildSearcherSource(
            WindowsUpdate update, string searcherVariable = "$Searcher")
        {
            if (string.IsNullOrWhiteSpace(searcherVariable) ||
                !searcherVariable.StartsWith('$'))
            {
                throw new ArgumentException(
                    "A PowerShell variable name is required.",
                    nameof(searcherVariable));
            }

            if (update.ServerSelection == 3 &&
                !string.IsNullOrWhiteSpace(update.ServiceId))
            {
                return searcherVariable + ".ServerSelection = 3\n" +
                    searcherVariable + ".ServiceID = " +
                    PsQuote(update.ServiceId);
            }

            return searcherVariable + ".ServerSelection = 0";
        }

        private static string PsQuote(string value) =>
            "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        private sealed class UpdateOperationOutcome
        {
            public bool Found { get; set; }
            public int OverallResultCode { get; set; }
            public int ResultCode { get; set; }
            public int HResult { get; set; }
            public bool IsDownloaded { get; set; }
            public bool RebootRequired { get; set; }
            public bool RebootRequiredBeforeInstallation { get; set; }
            public bool IsInstalled { get; set; }
            public bool StillApplicable { get; set; }
        }

        private static UpdateOperationOutcome ParseOperationOutcome(
            string json)
        {
            var result = JsonSerializer.Deserialize<UpdateOperationOutcome>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            return result ?? throw new InvalidOperationException(
                "Windows Update returned an empty operation result.");
        }

        private static string FormatOperationFailure(
            string operation, int resultCode, int hresult, string stderr)
        {
            string code = "0x" + unchecked((uint)hresult).ToString("X8");
            string? decoded = WuErrorCodes.Lookup(code);
            string reason = resultCode == 0 || resultCode == 1 ||
                            resultCode == 4 || resultCode == 5
                ? ResultCodeReason(resultCode.ToString())
                : "WUA reported ResultCode " + resultCode;

            if (hresult != 0)
                reason += "; " + code +
                    (decoded == null ? string.Empty : " — " + decoded);
            if (!string.IsNullOrWhiteSpace(stderr))
                reason += "; " + stderr.Trim();
            return operation + " failed: " + reason;
        }

        private static bool IsSuccessfulResult(int resultCode) =>
            resultCode == 2 || resultCode == 3;

        // Windows Update Agent supplies catalog metadata and an installable
        // identity. WindowsUpdateManager supplies the live orchestrator state
        // shown by Settings. This overlay is deliberately read-only and
        // best-effort: unsupported/failed WinRT access leaves WUA intact.
        internal static string BuildOrchestratorOverlayScript() => @"
$script:OrchestratorAvailable = $false

function Get-WumModernValue($item, [string]$name, $fallback) {
    try {
        $value = $item.$name
        if ($null -eq $value) { return $fallback }
        return $value
    } catch {
        return $fallback
    }
}

try {
    $managerType = [Type]::GetType(
        'Windows.Management.Update.WindowsUpdateManager, Windows.Management, ContentType=WindowsRuntime',
        $false)
    if ($null -eq $managerType) {
        [Console]::Error.WriteLine(
            'Windows Update orchestrator state is unavailable on this Windows build; WUA results were retained.')
    } else {
        $manager = [Activator]::CreateInstance(
            $managerType, [object[]]@('WUM'))

        $modernUpdates = @($manager.GetApplicableUpdates())
        $script:OrchestratorAvailable = $true
        foreach ($modern in $modernUpdates) {
            $rawUpdateId = [string](
                Get-WumModernValue $modern 'UpdateId' '')
            if ([string]::IsNullOrWhiteSpace($rawUpdateId)) { continue }

            # WindowsUpdateManager commonly appends a provider revision (for
            # example :1) that is not WUA's Identity.RevisionNumber (for
            # example 205). Normalize the GUID first. Use the suffix only to
            # break a tie between multiple WUA revisions; never reject the
            # same GUID merely because the two revision values differ.
            $identityId = $rawUpdateId
            $modernRevision = 0
            $identityMatch = [regex]::Match(
                $rawUpdateId,
                '^(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?::(?<revision>[0-9]+))?$')
            if ($identityMatch.Success) {
                $identityId = $identityMatch.Groups['id'].Value
                if ($identityMatch.Groups['revision'].Success) {
                    [void][int]::TryParse(
                        $identityMatch.Groups['revision'].Value,
                        [ref]$modernRevision)
                }
            }

            $candidates = @($script:Updates | Where-Object {
                [string]::Equals(
                    [string]$_.Id, $identityId,
                    [StringComparison]::OrdinalIgnoreCase)
            })
            $target = $null
            if ($candidates.Count -eq 1) {
                $target = $candidates[0]
            } elseif ($candidates.Count -gt 1) {
                if ($modernRevision -gt 0) {
                    $target = $candidates |
                        Where-Object {
                            [int]$_.RevisionNumber -eq $modernRevision
                        } |
                        Select-Object -First 1
                }
                if ($null -eq $target) {
                    $target = $candidates |
                        Sort-Object RevisionNumber -Descending |
                        Select-Object -First 1
                }
            }

            $providerId = [string](
                Get-WumModernValue $modern 'ProviderId' '')
            $currentAction = [string](
                Get-WumModernValue $modern 'CurrentAction' '')
            $actionProgress = Get-WumModernValue $modern 'ActionProgress' $null
            $progress = 0.0
            if ($null -ne $actionProgress) {
                try {
                    $progress = [Math]::Min(
                        1.0, [Math]::Max(
                            0.0, [double]$actionProgress.Progress))
                } catch { $progress = 0.0 }
                if ([string]::IsNullOrWhiteSpace($currentAction)) {
                    $currentAction = [string](
                        Get-WumModernValue $actionProgress 'Action' '')
                }
            }

            $hasActionResult = $false
            $resultAction = ''
            $actionSucceeded = $null
            $resultHResult = 0
            $resultError = ''
            $resultTimestamp = $null
            $actionResult = Get-WumModernValue $modern 'ActionResult' $null
            if ($null -ne $actionResult) {
                $hasActionResult = $true
                $resultAction = [string](
                    Get-WumModernValue $actionResult 'Action' '')
                $actionSucceeded = [bool](
                    Get-WumModernValue $actionResult 'Succeeded' $false)
                $timestamp = Get-WumModernValue $actionResult 'Timestamp' $null
                if ($null -ne $timestamp) {
                    try { $resultTimestamp = $timestamp.ToString('o') }
                    catch { $resultTimestamp = [string]$timestamp }
                }

                $extendedError = Get-WumModernValue $actionResult 'ExtendedError' $null
                if ($null -ne $extendedError) {
                    try { $resultHResult = [int]$extendedError.HResult }
                    catch {
                        try { $resultHResult = [int]$extendedError }
                        catch { $resultHResult = 0 }
                    }
                    try { $resultError = [string]$extendedError.Message }
                    catch { $resultError = [string]$extendedError }
                    if ([string]::IsNullOrWhiteSpace($resultError)) {
                        $resultError = [string]$extendedError
                    }
                }
            }
            $modernDownloaded = (
                $hasActionResult -and
                ($actionSucceeded -eq $true) -and
                [string]::Equals(
                    $resultAction, 'Download',
                    [StringComparison]::OrdinalIgnoreCase))

            $attentionReason = ''
            $attentionTimestamp = $null
            $attention = Get-WumModernValue $modern 'AttentionRequiredInfo' $null
            if ($null -ne $attention) {
                $attentionReason = [string](
                    Get-WumModernValue $attention 'Reason' '')
                $timestamp = Get-WumModernValue $attention 'Timestamp' $null
                if ($null -ne $timestamp) {
                    try { $attentionTimestamp = $timestamp.ToString('o') }
                    catch { $attentionTimestamp = [string]$timestamp }
                }
            }

            if ($null -eq $target) {
                # A modern provider can expose an applicable update that WUA's
                # COM catalog does not. Keep it visible, while leaving the WUA
                # revision at zero so later lookup never pins the provider's
                # unrelated revision suffix as a WUA revision.
                $categoryNames = @()
                $isDriver = [bool](
                    Get-WumModernValue $modern 'IsDriver' $false)
                if ($isDriver) { $categoryNames += 'Drivers' }
                if ([bool](Get-WumModernValue $modern 'IsFeatureUpdate' $false)) {
                    $categoryNames += 'Feature Updates'
                }
                if ([bool](Get-WumModernValue $modern 'IsSecurity' $false)) {
                    $categoryNames += 'Security Updates'
                }
                if ([bool](Get-WumModernValue $modern 'IsCritical' $false)) {
                    $categoryNames += 'Critical Updates'
                }

                $target = [PSCustomObject]@{
                    Id                  = $identityId
                    RevisionNumber      = 0
                    ServerSelection     = 0
                    ServiceId           = ''
                    Source              = 'Windows Update Orchestrator'
                    Title               = [string](Get-WumModernValue $modern 'Title' '')
                    Description         = [string](Get-WumModernValue $modern 'Description' '')
                    KBArticle           = ''
                    UpdateType          = if ($isDriver) { 'Driver' } else { 'Software' }
                    CategoryNames       = @($categoryNames)
                    CategoryIds         = @()
                    IsInstalled         = $false
                    IsHidden            = $false
                    IsDownloaded        = [bool]$modernDownloaded
                    BrowseOnly          = [bool](Get-WumModernValue $modern 'IsSeeker' $false)
                    IsBeta              = ([string](Get-WumModernValue $modern 'Title' '') -match 'Insider|Preview')
                    IsMandatory         = [bool](Get-WumModernValue $modern 'IsMandatory' $false)
                    SizeInBytes         = 0
                    SupportUrl          = [string](Get-WumModernValue $modern 'SupportUrl' '')
                    Severity            = ''
                    RequiresReboot      = $false
                    MayRequireReboot    = $false
                    ReleaseDate         = $null
                    SupersededUpdateIds = @()
                    IsOrchestratorUpdate           = $true
                    OrchestratorUpdateId           = $rawUpdateId
                    OrchestratorRevisionNumber     = $modernRevision
                    OrchestratorProviderId         = $providerId
                    CurrentAction                  = $currentAction
                    OrchestratorProgress           = $progress
                    HasOrchestratorActionResult    = $hasActionResult
                    OrchestratorActionResultAction = $resultAction
                    OrchestratorActionSucceeded    = $actionSucceeded
                    OrchestratorResultHResult      = $resultHResult
                    OrchestratorResultError        = $resultError
                    OrchestratorActionTimestamp    = $resultTimestamp
                    AttentionRequiredReason        = $attentionReason
                    AttentionRequiredTimestamp     = $attentionTimestamp
                }
                $script:Updates += $target
                $script:Seen[$identityId + ':0'] = $true
            } else {
                $target.IsOrchestratorUpdate = $true
                $target.OrchestratorUpdateId = $rawUpdateId
                $target.OrchestratorRevisionNumber = $modernRevision
                $target.OrchestratorProviderId = $providerId
                $target.CurrentAction = $currentAction
                $target.OrchestratorProgress = $progress
                $target.HasOrchestratorActionResult = $hasActionResult
                $target.OrchestratorActionResultAction = $resultAction
                $target.OrchestratorActionSucceeded = $actionSucceeded
                $target.OrchestratorResultHResult = $resultHResult
                $target.OrchestratorResultError = $resultError
                $target.OrchestratorActionTimestamp = $resultTimestamp
                $target.AttentionRequiredReason = $attentionReason
                $target.AttentionRequiredTimestamp = $attentionTimestamp
            }
        }
    }
} catch {
    [Console]::Error.WriteLine(
        'Windows Update orchestrator state query failed: ' +
        $_.Exception.GetBaseException().Message +
        '; WUA results were retained.')
}
";

        // --refresh uses the Windows Update orchestrator on builds that expose
        // Windows.Management.Update. StartScan(true) is the OS-supported
        // equivalent of the Settings "Check for updates" action. Older builds
        // simply continue to the uncached online WUA search below.
        internal static string BuildUserInitiatedRefreshScript() => @"
$ErrorActionPreference = 'Stop'
try {
    $managerType = [Type]::GetType(
        'Windows.Management.Update.WindowsUpdateManager, Windows.Management, ContentType=WindowsRuntime',
        $false)
    if ($null -ne $managerType) {
        $manager = [Activator]::CreateInstance(
            $managerType, [object[]]@('WUM'))
        $before = $manager.LastSuccessfulScanTimestamp
        $started = [bool]$manager.IsScanning

        if (-not $started) {
            $manager.StartScan($true)
        }

        $startDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        while (-not $started -and
               [DateTimeOffset]::UtcNow -lt $startDeadline) {
            if ([bool]$manager.IsScanning) {
                $started = $true
                break
            }
            $after = $manager.LastSuccessfulScanTimestamp
            if ($null -ne $after -and
                ($null -eq $before -or $after -gt $before)) {
                $started = $true
                break
            }
            Start-Sleep -Milliseconds 250
        }

        if ($started -and [bool]$manager.IsScanning) {
            $finishDeadline = [DateTimeOffset]::UtcNow.AddMinutes(5)
            while ([bool]$manager.IsScanning -and
                   [DateTimeOffset]::UtcNow -lt $finishDeadline) {
                Start-Sleep -Milliseconds 500
            }
            if ([bool]$manager.IsScanning) {
                throw 'Windows Update orchestrator scan timed out after five minutes.'
            }
        } elseif (-not $started) {
            [Console]::Error.WriteLine(
                'Windows Update orchestrator did not report a scan start; continuing with an online WUA scan.')
        }
    }
} catch {
    [Console]::Error.WriteLine(
        'Windows Update orchestrator refresh was unavailable: ' +
        $_.Exception.GetBaseException().Message +
        '; continuing with an online WUA scan.')
}
";

        // Releases before this fix wrote four five-minute scan snapshots here.
        // They are never read now. Delete only those exact legacy filenames;
        // leave unrelated diagnostic/configuration files untouched.
        private void RemoveLegacyScanCache()
        {
            try
            {
                string cacheDirectory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData),
                    "WUM", "cache");

                foreach (string hidden in new[] { "0", "1" })
                foreach (string mu in new[] { "0", "1" })
                {
                    string path = Path.Combine(
                        cacheDirectory,
                        "available-h" + hidden + "-m" + mu + ".json");
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        _log.Information(
                            "Removed obsolete WUM scan cache {Path}", path);
                    }
                }

                if (Directory.Exists(cacheDirectory) &&
                    Directory.GetFileSystemEntries(cacheDirectory).Length == 0)
                {
                    Directory.Delete(cacheDirectory);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Cache files created by an elevated older build may be
                // read-only to a standard user. They are never read now and
                // the next elevated WUM run will remove them.
                _log.Information(
                    "Obsolete elevated WUM scan cache is ignored until an elevated cleanup can remove it");
            }
            catch (Exception ex)
            {
                _log.Warning(ex,
                    "Could not remove obsolete WUM scan cache; it will be ignored");
            }
        }

        // ── Uninstall ─────────────────────────────────────────────────────
        public async Task<bool> UninstallUpdateAsync(
            string kbArticle,
            CancellationToken ct = default)
        {
            var kb = kbArticle.Replace("KB", "",
                StringComparison.OrdinalIgnoreCase);
            bool ok = await PowerShellHelper.RunCommandAsync(
                "wusa.exe /uninstall /kb:" + kb + " /quiet /norestart",
                ct: ct);
            return ok;
        }

        // ── Hide ──────────────────────────────────────────────────────────
        public async Task<bool> HideUpdateAsync(
            string updateId,
            CancellationToken ct = default)
        {
            string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Results  = $Searcher.Search(""UpdateID='"  + updateId + @"'"")
foreach ($u in $Results.Updates) { $u.IsHidden = $true }
Write-Output 'OK'
";
            var (_, output, _) = await PowerShellHelper.RunScriptAsync(
                script, ct: ct);
            bool ok = output.Trim() == "OK";
            return ok;
        }

        // ── Unhide ────────────────────────────────────────────────────────
        public async Task<bool> UnhideUpdateAsync(
            string updateId,
            CancellationToken ct = default)
        {
            string script = @"
$Session  = New-Object -ComObject Microsoft.Update.Session
$Searcher = $Session.CreateUpdateSearcher()
$Results  = $Searcher.Search(""IsHidden=1 AND UpdateID='"  + updateId + @"'"")
foreach ($u in $Results.Updates) { $u.IsHidden = $false }
Write-Output 'OK'
";
            var (_, output, _) = await PowerShellHelper.RunScriptAsync(
                script, ct: ct);
            bool ok = output.Trim() == "OK";
            return ok;
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
        // Must match diagnose D1 markers so 'diagnose' and 'reboot' agree.
        // Update-specific keys only — PendingFileRenameOperations is excluded
        // (noisy, set by routine installers; false positive for update reboots).
        public bool IsRebootRequired()
        {
            string? commitMarker = _registry.GetValue<string>(
                @"SOFTWARE\WUM\Runtime",
                "UpdateCommitBootSession",
                null);
            if (IsCommitMarkerForCurrentBoot(
                    commitMarker,
                    DateTimeOffset.UtcNow,
                    Environment.TickCount64))
            {
                return true;
            }

            object? systemInfo = null;
            try
            {
                Type? type = Type.GetTypeFromProgID(
                    "Microsoft.Update.SystemInfo", throwOnError: false);
                systemInfo = type == null ? null : Activator.CreateInstance(type);
                if (type != null && systemInfo != null)
                {
                    object? value = type.InvokeMember(
                        "RebootRequired",
                        BindingFlags.GetProperty,
                        binder: null,
                        target: systemInfo,
                        args: null);
                    if (Convert.ToBoolean(value)) return true;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex,
                    "WUA system reboot check failed; using registry markers");
            }
            finally
            {
                if (systemInfo != null && Marshal.IsComObject(systemInfo))
                {
                    try { Marshal.FinalReleaseComObject(systemInfo); }
                    catch { /* COM object is already disconnected */ }
                }
            }

            try
            {
                return _registry.KeyExists(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\" +
                        @"WindowsUpdate\Auto Update\RebootRequired")
                    || _registry.KeyExists(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\" +
                        @"Component Based Servicing\RebootPending");
            }
            catch { return false; }
        }

        internal static bool IsCommitMarkerForCurrentBoot(
            string? marker,
            DateTimeOffset nowUtc,
            long tickCountMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(marker) ||
                tickCountMilliseconds < 0)
            {
                return false;
            }

            string[] parts = marker.Split('|', 2);
            if (parts.Length != 2 ||
                !(parts[0].Equals(
                        "Started", StringComparison.OrdinalIgnoreCase) ||
                  parts[0].Equals(
                        "Committed", StringComparison.OrdinalIgnoreCase)) ||
                !DateTimeOffset.TryParse(
                    parts[1],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset markerBoot))
            {
                return false;
            }

            DateTimeOffset estimatedBoot = nowUtc -
                TimeSpan.FromMilliseconds(tickCountMilliseconds);
            return Math.Abs(
                (markerBoot.ToUniversalTime() -
                 estimatedBoot.ToUniversalTime()).TotalMinutes) <= 5;
        }

        // WUA feature updates must be committed exactly once immediately
        // before reboot. Keep commit and shutdown in one PowerShell process,
        // serialize all WUM processes, and persist Started before Commit so a
        // crash can never make a retry invoke the unsupported second Commit.
        public async Task<bool> CommitAndScheduleRebootAsync(
            int secondsDelay,
            string message,
            CancellationToken ct = default)
        {
            LastError = null;
            if (secondsDelay < 0 || secondsDelay > 315_360_000)
                throw new ArgumentOutOfRangeException(nameof(secondsDelay));
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            ct.ThrowIfCancellationRequested();

            try
            {
                string script = BuildCommitAndScheduleRebootScript(
                    secondsDelay, message);
                // From the first durable Started marker through shutdown.exe,
                // cancellation would leave an unknowable transaction state.
                // Let this short critical section finish. If the process is
                // externally terminated, the Started marker makes retry safe.
                var (success, output, error) =
                    await PowerShellHelper.RunScriptAsync(
                        script, ct: CancellationToken.None);

                if (!success)
                {
                    LastError = string.IsNullOrWhiteSpace(error)
                        ? "Windows Update commit or restart scheduling failed."
                        : error.Trim();
                    _log.Error(
                        "Update commit/restart scheduling failed: {Error}",
                        LastError);
                    return false;
                }

                _log.Information(
                    "Update commit/restart scheduling completed: {Result}",
                    output);
                if (output.Contains("\"CommitUncertain\":true",
                        StringComparison.OrdinalIgnoreCase))
                {
                    _log.Warning(
                        "A prior commit was interrupted; WUM safely skipped " +
                        "a duplicate Commit and scheduled the required reboot");
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                LastError = "update commit/restart scheduling was cancelled";
                throw;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _log.Error(ex, "Update commit/restart scheduling failed");
                return false;
            }
        }

        internal static string BuildCommitAndScheduleRebootScript(
            int secondsDelay,
            string message) => @"
$ErrorActionPreference = 'Stop'
$statePath = 'HKLM:\SOFTWARE\WUM\Runtime'
$markerName = 'UpdateCommitBootSession'
$mutex = New-Object System.Threading.Mutex(
    $false, 'Global\WUM.UpdateCommitReboot')
$mutexHeld = $false
try {
    try {
        $mutexHeld = $mutex.WaitOne([TimeSpan]::FromMinutes(2))
    } catch [System.Threading.AbandonedMutexException] {
        # The previous process died while holding the lock. The durable
        # Started marker below determines whether Commit may be called.
        $mutexHeld = $true
    }
    if (-not $mutexHeld) {
        throw 'Timed out waiting for another WUM commit/restart operation.'
    }

    $boot = (Get-CimInstance -ClassName Win32_OperatingSystem `
        -Property LastBootUpTime).LastBootUpTime
    if ($null -eq $boot) {
        throw 'Could not determine the current boot session.'
    }
    $bootToken = $boot.ToUniversalTime().ToString('o')
    $startedMarker = 'Started|' + $bootToken
    $committedMarker = 'Committed|' + $bootToken
    $lastMarker = (Get-ItemProperty -LiteralPath $statePath `
        -Name $markerName -ErrorAction SilentlyContinue).$markerName
    $alreadyCommitted = ($lastMarker -eq $committedMarker)
    $commitUncertain = ($lastMarker -eq $startedMarker)
    $commitPerformed = $false

    if (-not $alreadyCommitted -and -not $commitUncertain) {
        # Prove the marker is writable before changing WUA transaction state.
        [void](New-Item -Path $statePath -Force)
        Set-ItemProperty -LiteralPath $statePath `
            -Name 'CommitWriteProbe' -Value $bootToken
        Remove-ItemProperty -LiteralPath $statePath `
            -Name 'CommitWriteProbe' -ErrorAction SilentlyContinue

        # Record intent before the non-repeatable call. If the process dies
        # after this write, a retry reboots without risking a second Commit.
        Set-ItemProperty -LiteralPath $statePath `
            -Name $markerName -Value $startedMarker

        $session = New-Object -ComObject Microsoft.Update.Session
        $session.ClientApplicationID = 'WUM'
        $installer = $session.CreateUpdateInstaller()
        $installer.ClientApplicationID = 'WUM'
        $installer.Commit([uint32]0)
        $commitPerformed = $true

        Set-ItemProperty -LiteralPath $statePath `
            -Name $markerName -Value $committedMarker
    }

    $shutdown = Join-Path $env:SystemRoot 'System32\shutdown.exe'
    & $shutdown '/r' '/t' '" + secondsDelay + @"' '/c' " + PsQuote(message) + @"
    if ($LASTEXITCODE -ne 0) {
        throw ('shutdown.exe failed with exit code ' + $LASTEXITCODE)
    }

    [PSCustomObject]@{
        Scheduled        = $true
        CommitPerformed  = $commitPerformed
        AlreadyCommitted = $alreadyCommitted
        CommitUncertain  = $commitUncertain
        BootSession      = $bootToken
    } | ConvertTo-Json -Compress
} finally {
    if ($mutexHeld) {
        try { [void]$mutex.ReleaseMutex() } catch { }
    }
    $mutex.Dispose()
}
";

        // ── Service Status ────────────────────────────────────────────────
        public async Task<string> GetServiceStatusAsync(
            CancellationToken ct = default)
        {
            try
            {
                var (_, output, _) = await PowerShellHelper.RunScriptAsync(
                    "(Get-Service -Name wuauserv " +
                    "-ErrorAction SilentlyContinue).Status",
                    ct: ct);

                return string.IsNullOrWhiteSpace(output)
                    ? "Unknown"
                    : output.Trim();
            }
            catch (OperationCanceledException) { throw; }
            catch { return "Unknown"; }
        }

        // ── JSON Parsers ──────────────────────────────────────────────────
        internal static List<WindowsUpdate> ParseUpdatesFromJson(
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
                    var categoryNames = GetStrings(el, "CategoryNames");
                    var updateType    = GetStr(el, "UpdateType");
                    var title         = GetStr(el, "Title");
                    bool isInstalled  = installed || GetBool(el, "IsInstalled");
                    bool isHidden     = GetBool(el, "IsHidden");
                    bool isDownloaded = GetBool(el, "IsDownloaded");
                    string currentAction = GetStr(el, "CurrentAction");
                    string attentionReason =
                        GetStr(el, "AttentionRequiredReason");
                    bool? actionSucceeded =
                        GetNullableBool(el, "OrchestratorActionSucceeded");
                    bool hasActionResult =
                        GetBool(el, "HasOrchestratorActionResult") ||
                        actionSucceeded.HasValue;
                    double orchestratorProgress = Math.Clamp(
                        GetDouble(el, "OrchestratorProgress"), 0.0, 1.0);
                    bool needsReboot =
                        GetBool(el, "RequiresReboot") ||
                        IsRestartOrchestratorState(attentionReason);

                    var u = new WindowsUpdate
                    {
                        Id             = GetStr(el, "Id"),
                        RevisionNumber = GetInt(el, "RevisionNumber"),
                        ServerSelection= GetInt(el, "ServerSelection"),
                        ServiceId      = GetStr(el, "ServiceId"),
                        Source         = GetStr(el, "Source"),
                        Title          = title,
                        Description    = GetStr(el, "Description"),
                        KBArticle      = GetStr(el, "KBArticle"),
                        UpdateType     = updateType,
                        CategoryNames  = categoryNames,
                        CategoryIds    = GetStrings(el, "CategoryIds"),
                        IsInstalled    = isInstalled,
                        IsHidden       = isHidden,
                        IsDownloaded   = isDownloaded,
                        BrowseOnly     = GetBool(el, "BrowseOnly"),
                        IsBeta         = GetBool(el, "IsBeta"),
                        IsMandatory    = GetBool(el, "IsMandatory"),
                        SizeInBytes    = GetLong(el, "SizeInBytes"),
                        SupportUrl     = GetStr(el, "SupportUrl"),
                        Severity       = GetStr(el, "Severity"),
                        RequiresReboot = needsReboot,
                        MayRequireReboot = GetBool(el, "MayRequireReboot"),
                        ReleaseDate    = GetDate(el, "ReleaseDate"),
                        SupersededUpdateIds = GetStrings(el, "SupersededUpdateIds"),
                        IsOrchestratorUpdate =
                            GetBool(el, "IsOrchestratorUpdate"),
                        OrchestratorUpdateId =
                            GetStr(el, "OrchestratorUpdateId"),
                        OrchestratorRevisionNumber =
                            GetInt(el, "OrchestratorRevisionNumber"),
                        OrchestratorProviderId =
                            GetStr(el, "OrchestratorProviderId"),
                        CurrentAction = currentAction,
                        OrchestratorProgress = orchestratorProgress,
                        DownloadProgress = IsAction(
                                currentAction, "Download")
                            ? orchestratorProgress * 100.0
                            : 0.0,
                        InstallProgress = IsInstallAction(currentAction)
                            ? orchestratorProgress * 100.0
                            : 0.0,
                        HasOrchestratorActionResult = hasActionResult,
                        OrchestratorActionResultAction =
                            GetStr(el, "OrchestratorActionResultAction"),
                        OrchestratorActionSucceeded = actionSucceeded,
                        OrchestratorResultHResult =
                            GetInt(el, "OrchestratorResultHResult"),
                        OrchestratorResultError =
                            GetStr(el, "OrchestratorResultError"),
                        OrchestratorActionTimestamp =
                            GetDate(el, "OrchestratorActionTimestamp"),
                        AttentionRequiredReason = attentionReason,
                        AttentionRequiredTimestamp =
                            GetDate(el, "AttentionRequiredTimestamp"),
                        Status         = MapStatus(
                            isInstalled, isHidden, isDownloaded, needsReboot,
                            currentAction, hasActionResult, actionSucceeded,
                            attentionReason),
                        Category       = ParseCategory(
                            categoryNames, updateType, title)
                    };
                    list.Add(u);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse WUA updates JSON");
                throw new InvalidOperationException(
                    "Windows Update returned malformed update data.", ex);
            }
            return list;
        }

        internal static UpdateStatus MapStatus(
            bool installed,
            bool hidden,
            bool downloaded,
            bool rebootRequired,
            string currentAction = "",
            bool hasActionResult = false,
            bool? actionSucceeded = null,
            string attentionReason = "")
        {
            // A genuine reboot gate is more actionable than a historical
            // failed action result that may still be attached to the item.
            if (rebootRequired || IsRestartOrchestratorState(
                    attentionReason))
                return UpdateStatus.RestartRequired;
            if (IsAction(currentAction, "Download"))
                return UpdateStatus.Downloading;
            if (IsInstallAction(currentAction))
                return UpdateStatus.Installing;
            if ((hasActionResult && actionSucceeded == false) ||
                IsTerminalAttention(attentionReason))
                return UpdateStatus.Failed;
            if (installed)  return UpdateStatus.Installed;
            if (hidden)     return UpdateStatus.Hidden;
            if (downloaded) return UpdateStatus.PendingInstallation;
            return UpdateStatus.Available;
        }

        private static bool IsAction(string actual, string expected) =>
            string.Equals(
                actual?.Trim(), expected,
                StringComparison.OrdinalIgnoreCase);

        private static bool IsInstallAction(string action) =>
            IsAction(action, "Install") ||
            IsAction(action, "Deploy") ||
            IsAction(action, "Commit") ||
            IsAction(action, "Reboot") ||
            IsAction(action, "PostReboot");

        private static bool IsRestartOrchestratorState(
            string attentionReason) =>
            IsAction(attentionReason, "ReadyToReboot");

        internal static bool IsTerminalAttention(string attentionReason) =>
            IsAction(attentionReason, "CompatibilityError") ||
            IsAction(attentionReason, "BlockedByPostRebootFailure") ||
            IsAction(attentionReason, "BlockedByTooManyAttempts") ||
            IsAction(attentionReason, "BlockedByFailure") ||
            IsAction(attentionReason, "Demotion");

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
        internal static UpdateCategory ParseCategory(
            List<string> names, string updateType, string title)
        {
            string text = (string.Join(" ", names) + " " + title)
                .ToLowerInvariant();
            string type = updateType.ToLowerInvariant();

            if (type.Contains("driver") || text.Contains("driver"))
                return UpdateCategory.Driver;
            if (text.Contains("feature update") || text.Contains("upgrade") ||
                text.Contains("insider preview"))
                return UpdateCategory.FeatureUpdate;
            if (text.Contains("cumulative"))
                return UpdateCategory.CumulativeUpdate;
            if (text.Contains("definition") ||
                text.Contains("security intelligence") ||
                text.Contains("defender antivirus"))
                return UpdateCategory.Definition;
            if (text.Contains("security"))
                return UpdateCategory.Security;
            if (text.Contains("critical"))
                return UpdateCategory.Critical;
            if (text.Contains("service pack"))
                return UpdateCategory.ServicePack;
            if (names.Count == 0 && string.IsNullOrWhiteSpace(title))
                return UpdateCategory.Unknown;
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

        private static double GetDouble(JsonElement el, string key)
        {
            try { return el.GetProperty(key).GetDouble(); }
            catch { return 0.0; }
        }

        private static bool? GetNullableBool(JsonElement el, string key)
        {
            try
            {
                JsonElement value = el.GetProperty(key);
                return value.ValueKind == JsonValueKind.True
                    ? true
                    : value.ValueKind == JsonValueKind.False
                        ? false
                        : null;
            }
            catch { return null; }
        }

        private static DateTime? GetDate(JsonElement el, string key)
        {
            string value = GetStr(el, key);
            return DateTime.TryParse(value, out var date) ? date : null;
        }

        private static List<string> GetStrings(JsonElement el, string key)
        {
            var values = new List<string>();
            try
            {
                JsonElement property = el.GetProperty(key);
                if (property.ValueKind == JsonValueKind.String)
                {
                    string? value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
                }
                else if (property.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in property.EnumerateArray())
                    {
                        string? value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
                    }
                }
            }
            catch { /* optional metadata */ }
            return values;
        }
        // ── Diagnostics ───────────────────────────────────────────────────
        public async Task<string> DiagnoseAsync(
            CancellationToken ct = default)
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
    'Update Orch  ' = 'UsoSvc'
    'Modules Inst ' = 'TrustedInstaller'
    'MSI Installer' = 'msiserver'
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

# 4b. Registered update services. ssDefault follows the effective Automatic
#     Updates service/policy. Service Locator may also surface OS flighting on
#     an Insider device.
try {
    $sm = New-Object -ComObject Microsoft.Update.ServiceManager
    $muId = '7971f918-a847-4430-9279-4a52d1efe18d'
    $flightId = '8b24b027-1dee-babb-9a95-3517dfb9c552'
    $hasMu = $false
    $script:defaultAuName = ''
    $script:defaultAuId = ''
    foreach ($s in $sm.Services) {
        $flag = ''
        if ($s.IsDefaultAUService) {
            $flag = ' [effective default]'
            $script:defaultAuName = $s.Name
            $script:defaultAuId = $s.ServiceID
        }
        if ($s.ServiceID -eq $muId) { $hasMu = $true; $flag += ' [Microsoft Update]' }
        if ($s.ServiceID -eq $flightId) { $flag += ' [OS flighting]' }
        Write-Output ('Service       : ' + $s.Name + $flag)
    }
    if ($hasMu) {
        Write-Output 'Microsoft Upd : Registered'
    } else {
        Write-Output 'Microsoft Upd : NOT registered (other Microsoft-product updates unavailable)'
    }
} catch {
    Write-Output ('Service Mgr   : FAILED - ' + $_.Exception.Message)
}

# 5. Search test - validate that the COM searcher works. The CLI performs its
#    own live query after diagnostics to report the current applicable count.
try {
    $null = $Searcher.Search('IsInstalled=0 AND Type=''Software''')
    Write-Output 'Search Test   : OK'
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

# 8b. Effective source: managed WSUS policy wins; otherwise report WUA's
#     current default Automatic Updates service discovered above.
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
    } elseif ($script:defaultAuName) {
        Write-Output ('Update Source : ' + $script:defaultAuName +
            ' (' + $script:defaultAuId + ')')
    } else {
        Write-Output 'Update Source : Default WUA policy/service'
    }
} catch {
    Write-Output 'Update Source : Unable to read'
}

Write-Output ''
Write-Output '=== Deep Checks ==='

# D1. Pending reboot - update-specific markers only. PendingFileRenameOperations
#     is deliberately EXCLUDED: it is set by routine installers/file ops and is a
#     false positive for 'updates need a reboot'. Keep this aligned with
#     IsRebootRequired() so 'diagnose' and 'reboot' never disagree.
try {
    $reasons = @()
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $reasons += 'WU' }
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $reasons += 'CBS' }
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired\UpdateExeVolatile') { $reasons += 'UpdateExe' }
    if ($reasons.Count -gt 0) {
        Write-Output ('Pending Reboot: REBOOT REQUIRED (' + ($reasons -join ', ') + ')')
    } else {
        Write-Output 'Pending Reboot: No'
    }
} catch { Write-Output 'Pending Reboot: Unable to read' }

# D2. System drive free space - low space stalls download/install.
try {
    $sysDrive = $env:SystemDrive.TrimEnd(':')
    $d = Get-PSDrive -Name $sysDrive -ErrorAction SilentlyContinue
    if ($d) {
        $freeGB = [Math]::Round($d.Free / 1GB, 1)
        if ($freeGB -lt 10) {
            Write-Output ('Disk Free     : ' + $freeGB + ' GB (LOW)')
        } else {
            Write-Output ('Disk Free     : ' + $freeGB + ' GB')
        }
    }
} catch { Write-Output 'Disk Free     : Unable to read' }

# D3. SoftwareDistribution download cache size - bloat signals stuck cache.
try {
    $dlPath = Join-Path $env:windir 'SoftwareDistribution\Download'
    if (Test-Path $dlPath) {
        $sz = (Get-ChildItem $dlPath -Recurse -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
        $szMB = [Math]::Round(($sz / 1MB), 1)
        Write-Output ('SoftDist Cache: ' + $szMB + ' MB')
    } else {
        Write-Output 'SoftDist Cache: Missing (reset in progress?)'
    }
} catch { Write-Output 'SoftDist Cache: Unable to read' }

# D4. WU datastore - the WUA index DB; missing/locked breaks search.
try {
    $edb = Join-Path $env:windir 'SoftwareDistribution\DataStore\DataStore.edb'
    if (Test-Path $edb) {
        $edbMB = [Math]::Round(((Get-Item $edb).Length / 1MB), 1)
        Write-Output ('WU Datastore  : OK (' + $edbMB + ' MB)')
    } else {
        Write-Output 'WU Datastore  : MISSING'
    }
} catch { Write-Output 'WU Datastore  : Unable to read' }

# D5. Uptime - very long uptime delays pending-reboot finalization.
try {
    $os2 = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
    if ($os2) {
        $up = (Get-Date) - $os2.LastBootUpTime
        $days = [Math]::Floor($up.TotalDays)
        Write-Output ('Uptime        : ' + $days + 'd ' + $up.Hours + 'h ' + $up.Minutes + 'm')
    }
} catch { Write-Output 'Uptime        : Unable to read' }

# D6. Last successful install time (companion to Last WU Check).
try {
    $ip = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install'
    $iv = (Get-ItemProperty -Path $ip -ErrorAction SilentlyContinue).LastSuccessTime
    if ($iv) {
        Write-Output ('Last Install  : ' + $iv)
    } else {
        Write-Output 'Last Install  : Never or unknown'
    }
} catch { Write-Output 'Last Install  : Unable to read' }

# D7. Pending (downloaded, not installed) updates waiting in queue.
try {
    $rPend = $Searcher.Search('IsInstalled=0 AND IsHidden=0')
    $dlCount = 0
    foreach ($pu in $rPend.Updates) { if ($pu.IsDownloaded) { $dlCount++ } }
    Write-Output ('Pending Queue : ' + $dlCount + ' downloaded, not installed')
} catch { Write-Output 'Pending Queue : Unable to read' }

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
                    await PowerShellHelper.RunScriptAsync(script, ct: ct);

                string result = output ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(error))
                    result += "\nPS Errors:\n" + error;

                return result;
            }
            catch (OperationCanceledException) { throw; }
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
        public async Task<string> ResetComponentsAsync(
            CancellationToken ct = default)
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
                    await PowerShellHelper.RunScriptAsync(script, ct: ct);

                string result = output ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(error))
                    result += "\nPS Errors:\n" + error;

                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Error(ex, "ResetComponentsAsync failed");
                return "Reset failed: " + ex.Message;
            }
        }
    }
}
