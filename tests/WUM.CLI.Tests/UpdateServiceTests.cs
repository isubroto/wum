using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using WUM.Core.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;
using Xunit;

namespace WUM.CLI.Tests
{
    public class UpdateServiceTests
    {
        [Fact]
        public void ParseUpdates_MapsInsiderUpgradeWithoutInventingReboot()
        {
            const string json = """
            [{
              "Id":"f5bee5b5-c4f4-44a7-960a-160f115b7cce",
              "RevisionNumber":205,
              "ServerSelection":0,
              "Source":"Default",
              "Title":"Windows 11 Insider Preview Feature Update (26220.8764)",
              "KBArticle":"KB5095132",
              "UpdateType":"Software",
              "CategoryNames":["Upgrades","Windows Insider Preview"],
              "IsInstalled":false,
              "IsDownloaded":false,
              "RequiresReboot":false,
              "MayRequireReboot":true
            }]
            """;

            WindowsUpdate update = UpdateService
                .ParseUpdatesFromJson(json).Should().ContainSingle().Subject;

            update.Category.Should().Be(UpdateCategory.FeatureUpdate);
            update.Status.Should().Be(UpdateStatus.Available);
            update.RequiresReboot.Should().BeFalse(
                "installation capability is not an actual pending restart");
            update.MayRequireReboot.Should().BeTrue();
            update.RevisionNumber.Should().Be(205);
            update.Source.Should().Be("Default");
        }

        [Fact]
        public void ParseUpdates_KeepsCumulativeCategoryAndSecurityMembership()
        {
            const string json = """
            [{
              "Id":"update-id",
              "Title":"2026-07 Cumulative Update for Windows 11",
              "UpdateType":"Software",
              "CategoryNames":["Security Updates"],
              "IsInstalled":false
            }]
            """;

            WindowsUpdate update = UpdateService
                .ParseUpdatesFromJson(json).Should().ContainSingle().Subject;

            update.Category.Should().Be(UpdateCategory.CumulativeUpdate);
            update.IsSecurityUpdate.Should().BeTrue();
        }

        [Theory]
        [InlineData(false, false, false, false, UpdateStatus.Available)]
        [InlineData(false, false, true,  false, UpdateStatus.PendingInstallation)]
        [InlineData(false, true,  false, false, UpdateStatus.Hidden)]
        [InlineData(true,  false, false, false, UpdateStatus.Installed)]
        [InlineData(true,  false, false, true,  UpdateStatus.RestartRequired)]
        [InlineData(false, false, false, true,  UpdateStatus.RestartRequired)]
        public void MapStatus_UsesCurrentWuaState(
            bool installed,
            bool hidden,
            bool downloaded,
            bool rebootRequired,
            UpdateStatus expected)
        {
            UpdateService.MapStatus(
                installed, hidden, downloaded, rebootRequired)
                .Should().Be(expected);
        }

        [Theory]
        [InlineData("Driver", "", "Intel display", UpdateCategory.Driver)]
        [InlineData("Software", "Updates", "2026-07 Cumulative Update", UpdateCategory.CumulativeUpdate)]
        [InlineData("Software", "Definition Updates", "Security intelligence update", UpdateCategory.Definition)]
        [InlineData("Software", "Upgrades", "Windows 11", UpdateCategory.FeatureUpdate)]
        [InlineData("Software", "Security Updates", "Security update", UpdateCategory.Security)]
        [InlineData("Software", "", "Optional quality update", UpdateCategory.Optional)]
        public void ParseCategory_UsesTypeAllCategoriesAndTitle(
            string updateType,
            string categoryName,
            string title,
            UpdateCategory expected)
        {
            UpdateService.ParseCategory(
                string.IsNullOrEmpty(categoryName)
                    ? new()
                    : new() { categoryName },
                updateType,
                title).Should().Be(expected);
        }

        [Fact]
        public void ParseUpdates_HandlesExplicitEmptyArray()
        {
            UpdateService.ParseUpdatesFromJson("[]").Should().BeEmpty();
        }

        [Fact]
        public void ParseUpdates_RejectsMalformedScanData()
        {
            Action act = () => UpdateService.ParseUpdatesFromJson("not-json");

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*malformed update data*");
        }

        [Fact]
        public void IdentityCriteria_PinsUpdateAndRevision()
        {
            var update = new WindowsUpdate
            {
                Id = "f5bee5b5-c4f4-44a7-960a-160f115b7cce",
                RevisionNumber = 205
            };

            UpdateService.BuildIdentityCriteria(update).Should().Be(
                "UpdateID='f5bee5b5-c4f4-44a7-960a-160f115b7cce' " +
                "AND RevisionNumber=205");
        }

        [Fact]
        public void SearcherSource_PreservesMicrosoftUpdateService()
        {
            var update = new WindowsUpdate
            {
                ServerSelection = 3,
                ServiceId = "7971f918-a847-4430-9279-4a52d1efe18d"
            };

            string script = UpdateService.BuildSearcherSource(
                update, "$VerifySearcher");

            script.Should().Contain("$VerifySearcher.ServerSelection = 3");
            script.Should().Contain(
                "$VerifySearcher.ServiceID = '7971f918-a847-4430-9279-4a52d1efe18d'");
            script.Should().NotContain("$Searcher.");
        }

        [Fact]
        public void RefreshScript_RequestsSettingsEquivalentUserScan()
        {
            string script = UpdateService.BuildUserInitiatedRefreshScript();

            script.Should().Contain("Windows.Management.Update.WindowsUpdateManager");
            script.Should().Contain("$manager.StartScan($true)");
            script.Should().Contain("continuing with an online WUA scan");
        }

        [Fact]
        public void ParseUpdates_UsesOrchestratorDownloadStateAndPercent()
        {
            const string json = """
            [{
              "Id":"f5bee5b5-c4f4-44a7-960a-160f115b7cce",
              "RevisionNumber":205,
              "Title":"Windows 11 Insider Preview Feature Update",
              "UpdateType":"Software",
              "CategoryNames":["Upgrades"],
              "IsDownloaded":true,
              "IsOrchestratorUpdate":true,
              "OrchestratorUpdateId":"f5bee5b5-c4f4-44a7-960a-160f115b7cce:1",
              "OrchestratorRevisionNumber":1,
              "OrchestratorProviderId":"WuProvider",
              "CurrentAction":"Download",
              "OrchestratorProgress":0.28,
              "AttentionRequiredReason":"None"
            }]
            """;

            WindowsUpdate update = UpdateService
                .ParseUpdatesFromJson(json).Should().ContainSingle().Subject;

            update.Status.Should().Be(UpdateStatus.Downloading,
                "the Settings/orchestrator action is newer than WUA's downloaded flag");
            update.OrchestratorProgress.Should().BeApproximately(0.28, 0.001);
            update.DownloadProgress.Should().BeApproximately(28.0, 0.001);
            update.InstallProgress.Should().Be(0);
            update.OrchestratorUpdateId.Should().EndWith(":1");
            update.OrchestratorRevisionNumber.Should().Be(1);
            update.RevisionNumber.Should().Be(205,
                "provider and WUA revisions are separate identities");
            update.OrchestratorProviderId.Should().Be("WuProvider");
        }

        [Theory]
        [InlineData("Install", UpdateStatus.Installing)]
        [InlineData("Deploy", UpdateStatus.Installing)]
        [InlineData("Commit", UpdateStatus.Installing)]
        [InlineData("Reboot", UpdateStatus.Installing)]
        [InlineData("PostReboot", UpdateStatus.Installing)]
        public void MapStatus_UsesLiveOrchestratorAction(
            string action, UpdateStatus expected)
        {
            UpdateService.MapStatus(
                installed: false,
                hidden: false,
                downloaded: false,
                rebootRequired: false,
                currentAction: action)
                .Should().Be(expected);
        }

        [Fact]
        public void MapStatus_UsesFailedActionResult()
        {
            UpdateService.MapStatus(
                installed: false,
                hidden: false,
                downloaded: false,
                rebootRequired: false,
                currentAction: string.Empty,
                hasActionResult: true,
                actionSucceeded: false)
                .Should().Be(UpdateStatus.Failed);
        }

        [Fact]
        public void MapStatus_ActiveRetryOverridesHistoricalFailure()
        {
            UpdateService.MapStatus(
                installed: false,
                hidden: false,
                downloaded: false,
                rebootRequired: false,
                currentAction: "Download",
                hasActionResult: true,
                actionSucceeded: false,
                attentionReason: "BlockedByFailure")
                .Should().Be(UpdateStatus.Downloading);
        }

        [Theory]
        [InlineData("CompatibilityError")]
        [InlineData("BlockedByPostRebootFailure")]
        [InlineData("BlockedByTooManyAttempts")]
        [InlineData("BlockedByFailure")]
        [InlineData("Demotion")]
        public void MapStatus_UsesTerminalAttention(string reason)
        {
            UpdateService.MapStatus(
                false, false, false, false,
                attentionReason: reason)
                .Should().Be(UpdateStatus.Failed);
        }

        [Fact]
        public void MapStatus_UsesReadyToRebootAttention()
        {
            UpdateService.MapStatus(
                false, false, false, false,
                attentionReason: "ReadyToReboot")
                .Should().Be(UpdateStatus.RestartRequired);
        }

        [Fact]
        public void ParseUpdates_CapturesOrchestratorFailureDiagnostics()
        {
            const string json = """
            [{
              "Id":"modern-only-id",
              "Source":"Windows Update Orchestrator",
              "Title":"Provider update",
              "UpdateType":"Software",
              "IsOrchestratorUpdate":true,
              "OrchestratorUpdateId":"modern-only-id",
              "OrchestratorProviderId":"ContosoProvider",
              "HasOrchestratorActionResult":true,
              "OrchestratorActionResultAction":"Install",
              "OrchestratorActionSucceeded":false,
              "OrchestratorResultHResult":-2145124302,
              "OrchestratorResultError":"provider failure",
              "OrchestratorActionTimestamp":"2026-07-13T10:00:00Z",
              "AttentionRequiredReason":"BlockedByFailure",
              "AttentionRequiredTimestamp":"2026-07-13T10:00:01Z"
            }]
            """;

            WindowsUpdate update = UpdateService
                .ParseUpdatesFromJson(json).Should().ContainSingle().Subject;

            update.Status.Should().Be(UpdateStatus.Failed);
            update.IsOrchestratorUpdate.Should().BeTrue();
            update.OrchestratorActionResultAction.Should().Be("Install");
            update.OrchestratorActionSucceeded.Should().BeFalse();
            update.OrchestratorResultHResult.Should().Be(-2145124302);
            update.OrchestratorResultError.Should().Be("provider failure");
            update.OrchestratorActionTimestamp.Should().NotBeNull();
            update.AttentionRequiredReason.Should().Be("BlockedByFailure");
            update.AttentionRequiredTimestamp.Should().NotBeNull();
        }

        [Fact]
        public void OrchestratorOverlay_IsReadOnlyGuidFirstAndKeepsFallback()
        {
            string script = UpdateService.BuildOrchestratorOverlayScript();

            script.Should().Contain("$manager.GetApplicableUpdates()");
            script.Should().Contain("$script:OrchestratorAvailable = $true");
            script.Should().Contain("$identityId = $identityMatch.Groups['id'].Value");
            script.Should().Contain("[int]$_.RevisionNumber -eq $modernRevision");
            script.Should().Contain("Sort-Object RevisionNumber -Descending");
            script.Should().Contain("$script:Updates += $target",
                "modern-only applicable updates must remain visible");
            script.Should().Contain("WUA results were retained");
            script.Should().NotContain("StartScan(");
            script.Should().NotContain("StartInstall");
            script.Should().NotContain("StartDownload");
        }

        [Fact]
        public void OrchestratorSnapshot_IsIndependentJsonEnvelope()
        {
            string script = UpdateService.BuildOrchestratorSnapshotScript();

            script.Should().Contain("$script:Updates = @()");
            script.Should().Contain("Available = [bool]$script:OrchestratorAvailable");
            script.Should().Contain("Updates   = @($script:Updates)");
            script.Should().NotContain("Microsoft.Update.Session");
        }

        [Fact]
        public void MergeOrchestratorUpdates_OverlaysMatchingWuaIdentity()
        {
            const string id = "f5bee5b5-c4f4-44a7-960a-160f115b7cce";
            var wua = new WindowsUpdate
            {
                Id = id,
                RevisionNumber = 205,
                Source = "Default",
                IsDownloaded = false,
                Status = UpdateStatus.Available
            };
            var modern = new WindowsUpdate
            {
                Id = id,
                RevisionNumber = 0,
                Source = "Windows Update Orchestrator",
                IsOrchestratorUpdate = true,
                OrchestratorUpdateId = id + ":1",
                OrchestratorRevisionNumber = 1,
                CurrentAction = "Install",
                OrchestratorProgress = 0.42,
                IsDownloaded = true,
                InstallProgress = 42,
                Status = UpdateStatus.Installing
            };

            List<WindowsUpdate> merged = UpdateService
                .MergeOrchestratorUpdates(new() { wua }, new() { modern });

            WindowsUpdate result = merged.Should().ContainSingle().Subject;
            result.Should().BeSameAs(wua);
            result.Source.Should().Be("Default");
            result.RevisionNumber.Should().Be(205);
            result.OrchestratorUpdateId.Should().EndWith(":1");
            result.IsDownloaded.Should().BeTrue();
            result.Status.Should().Be(UpdateStatus.Installing);
            result.InstallProgress.Should().BeApproximately(42, 0.001);
        }

        [Fact]
        public void MergeOrchestratorUpdates_KeepsModernOnlyApplicableUpdate()
        {
            var modern = new WindowsUpdate
            {
                Id = "modern-only-id",
                Source = "Windows Update Orchestrator",
                IsOrchestratorUpdate = true,
                Status = UpdateStatus.Available
            };

            List<WindowsUpdate> merged = UpdateService
                .MergeOrchestratorUpdates(new(), new() { modern });

            merged.Should().ContainSingle().Which.Should().BeSameAs(modern);
        }

        [Theory]
        [InlineData(UpdateStatus.Downloading, "", "")]
        [InlineData(UpdateStatus.Installing, "", "")]
        [InlineData(UpdateStatus.RestartRequired, "", "")]
        [InlineData(UpdateStatus.Failed, "", "")]
        [InlineData(UpdateStatus.Available, "Commit", "")]
        [InlineData(UpdateStatus.Available, "", "NeedMoreSpace")]
        public void LiveOrchestratorState_BypassesBlockingWuaEnrichment(
            UpdateStatus status,
            string action,
            string attention)
        {
            UpdateService.HasLiveOrchestratorState(new()
            {
                new WindowsUpdate
                {
                    Status = status,
                    CurrentAction = action,
                    AttentionRequiredReason = attention
                }
            }).Should().BeTrue();
        }

        [Fact]
        public void QuietAvailableOrchestratorState_AllowsBoundedWuaEnrichment()
        {
            UpdateService.HasLiveOrchestratorState(new()
            {
                new WindowsUpdate
                {
                    Status = UpdateStatus.Available,
                    AttentionRequiredReason = "None"
                }
            }).Should().BeFalse();
        }

        [Theory]
        [InlineData(true,  false, false, true)]
        [InlineData(true,  true,  false, false)]
        [InlineData(true,  false, true,  false)]
        [InlineData(false, false, false, false)]
        public void OrchestratorCoverage_IsOnlyDefaultVisibleQuery(
            bool available,
            bool includeHidden,
            bool useMicrosoftUpdate,
            bool expected)
        {
            UpdateService.OrchestratorCoversQuery(
                available, includeHidden, useMicrosoftUpdate)
                .Should().Be(expected);
        }

        [Fact]
        public void CommitRebootScript_CommitsOncePerBootBeforeShutdown()
        {
            string script = UpdateService.BuildCommitAndScheduleRebootScript(
                30, "WUM: Completing Windows Update installation");

            script.Should().Contain("LastBootUpTime");
            script.Should().Contain("UpdateCommitBootSession");
            script.Should().Contain("Global\\WUM.UpdateCommitReboot");
            script.Should().Contain("$mutex.WaitOne");
            script.Should().Contain("$startedMarker");
            script.Should().Contain("$committedMarker");
            script.Should().Contain("$commitUncertain");
            script.Should().Contain("$alreadyCommitted");
            script.Should().Contain("$installer.Commit([uint32]0)");
            script.Should().Contain("System32\\shutdown.exe");
            script.IndexOf("-Value $startedMarker",
                    StringComparison.Ordinal)
                .Should().BeLessThan(
                    script.IndexOf("$installer.Commit",
                        StringComparison.Ordinal));
            script.IndexOf("$installer.Commit", StringComparison.Ordinal)
                .Should().BeLessThan(
                    script.IndexOf("-Value $committedMarker",
                        StringComparison.Ordinal));
            script.IndexOf("-Value $committedMarker",
                    StringComparison.Ordinal)
                .Should().BeLessThan(
                    script.IndexOf("System32\\shutdown.exe",
                        StringComparison.Ordinal));
            script.Should().NotContain("shutdown /a");
        }

        [Fact]
        public void CommitMarker_BlocksOnlyTheBootThatCreatedIt()
        {
            var now = new DateTimeOffset(
                2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
            var boot = now.AddHours(-3);
            long uptime = (long)TimeSpan.FromHours(3).TotalMilliseconds;

            UpdateService.IsCommitMarkerForCurrentBoot(
                    "Started|" + boot.ToString("o"),
                    now,
                    uptime)
                .Should().BeTrue();
            UpdateService.IsCommitMarkerForCurrentBoot(
                    "Committed|" + boot.ToString("o"),
                    now,
                    uptime)
                .Should().BeTrue();
            UpdateService.IsCommitMarkerForCurrentBoot(
                    "Committed|" + boot.AddDays(-1).ToString("o"),
                    now,
                    uptime)
                .Should().BeFalse(
                    "a marker from an earlier boot must not block installs");
            UpdateService.IsCommitMarkerForCurrentBoot(
                    boot.ToString("o"),
                    now,
                    uptime)
                .Should().BeFalse(
                    "legacy boot-only marker values are not transaction state");
        }

        [Fact]
        public async Task CommitRebootScript_ParsesInWindowsPowerShell()
        {
            string script = UpdateService.BuildCommitAndScheduleRebootScript(
                30, "WUM: Completing Windows Update installation");
            string encoded = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(script));
            string parseOnly = "$text=[Text.Encoding]::Unicode.GetString(" +
                "[Convert]::FromBase64String('" + encoded + "'));" +
                "$tokens=$null;$errors=$null;" +
                "[Management.Automation.Language.Parser]::ParseInput(" +
                "$text,[ref]$tokens,[ref]$errors)|Out-Null;" +
                "if($errors.Count -gt 0){" +
                "$errors|ForEach-Object{[Console]::Error.WriteLine($_)};exit 1}";

            var result = await PowerShellHelper.RunScriptAsync(parseOnly);

            result.Success.Should().BeTrue(result.Error);
        }
    }
}
