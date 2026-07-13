using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WUM.CLI.Commands;
using WUM.CLI.Helpers;
using WUM.Core.Models;
using WUM.Core.Services;
using Xunit;

namespace WUM.CLI.Tests.Commands
{
    public class AdminBoundaryTests
    {
        [Fact]
        public async Task InstallDryRun_DoesNotRequireAdminOrMutateUpdates()
        {
            var updates = BuildUpdateServiceWithAvailableUpdate();
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => false);

            var result = await InvokeAsync(
                new InstallCommand(services).Build(),
                "install", "--all", "--dry-run");

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("Dry run complete");
            result.Output.Should().NotContain(
                "Administrator privileges required");
            VerifyNoInstallMutation(updates);
        }

        [Fact]
        public async Task InstallMutation_WhenAdminDenied_ReturnsFailureWithoutMutation()
        {
            var updates = BuildUpdateServiceWithAvailableUpdate();
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => false);

            var result = await InvokeAsync(
                new InstallCommand(services).Build(),
                "install", "--all", "--force");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain(
                "Administrator privileges required");
            VerifyNoInstallMutation(updates);
        }

        [Fact]
        public async Task Install_WhenOsIsDownloading_SkipsDuplicateTransaction()
        {
            var updates = BuildUpdateServiceWithAvailableUpdate(
                UpdateStatus.Downloading);
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => false);

            var result = await InvokeAsync(
                new InstallCommand(services).Build(),
                "install", "--all", "--force");

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("already downloading");
            result.Output.Should().NotContain(
                "Administrator privileges required");
            VerifyNoInstallMutation(updates);
        }

        [Fact]
        public async Task Install_WhenRestartAlreadyPending_ReturnsFailureWithoutMutation()
        {
            var updates = BuildUpdateServiceWithAvailableUpdate();
            updates.Setup(service => service.IsRebootRequired())
                .Returns(true);
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => false);

            var result = await InvokeAsync(
                new InstallCommand(services).Build(),
                "install", "--all", "--force");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain(
                "cannot start until Windows restarts");
            result.Output.Should().NotContain(
                "Administrator privileges required");
            VerifyNoInstallMutation(updates);
        }

        [Fact]
        public async Task Install_WhenDownloadFails_ReturnsFailureExitCode()
        {
            var updates = BuildUpdateServiceWithAvailableUpdate();
            updates.Setup(service => service.DownloadUpdateAsync(
                    It.IsAny<WindowsUpdate>(),
                    It.IsAny<IProgress<double>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            updates.SetupGet(service => service.LastError)
                .Returns("simulated WUA failure");
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => true);

            var result = await InvokeAsync(
                new InstallCommand(services).Build(),
                "install", "--all", "--force", "--no-reboot");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain("Download failed");
            result.Output.Should().Contain("simulated WUA failure");
        }

        [Fact]
        public async Task Reboot_WhenAdminDenied_ReturnsFailureWithoutCommit()
        {
            var updates = new Mock<IUpdateService>();
            updates.Setup(service => service.IsRebootRequired())
                .Returns(true);
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => false);

            var result = await InvokeAsync(
                new RebootCommand(services).Build(),
                "reboot", "--force");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain(
                "Administrator privileges required");
            updates.Verify(service =>
                service.CommitAndScheduleRebootAsync(
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Reboot_WhenCommitFails_ReturnsFailureExitCode()
        {
            var updates = new Mock<IUpdateService>();
            updates.Setup(service => service.IsRebootRequired())
                .Returns(true);
            updates.Setup(service =>
                    service.CommitAndScheduleRebootAsync(
                        It.IsAny<int>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            updates.SetupGet(service => service.LastError)
                .Returns("simulated commit failure");
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => true);

            var result = await InvokeAsync(
                new RebootCommand(services).Build(),
                "reboot", "--force");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain(
                "Could not finalize updates");
            result.Output.Should().Contain("simulated commit failure");
        }

        [Fact]
        public async Task Reboot_WithInvalidDelay_ReturnsFailureExitCode()
        {
            var updates = new Mock<IUpdateService>();
            using var services = BuildServices(updates.Object);

            var result = await InvokeAsync(
                new RebootCommand(services).Build(),
                "reboot", "--delay", "-1");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain("Invalid restart delay");
        }

        [Fact]
        public async Task DiagnoseReset_WhenAdminDenied_DoesNotResetComponents()
        {
            var updates = new Mock<IUpdateService>();
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => false);

            var result = await InvokeAsync(
                new DiagnoseCommand(services).Build(),
                "diagnose", "--fix", "--force");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain(
                "Administrator privileges required");
            updates.Verify(
                service => service.ResetComponentsAsync(
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Uninstall_WhenAdminDenied_DoesNotRequestUninstall()
        {
            var updates = new Mock<IUpdateService>();
            using var services = BuildServices(updates.Object);
            using var admin = AdminHelper.BeginAdminCheckOverride(() => false);

            var result = await InvokeAsync(
                new UninstallCommand(services).Build(),
                "uninstall", "KB1234567", "--force");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain(
                "Administrator privileges required");
            updates.Verify(
                service => service.UninstallUpdateAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static Mock<IUpdateService> BuildUpdateServiceWithAvailableUpdate(
            UpdateStatus status = UpdateStatus.Available)
        {
            var updates = new Mock<IUpdateService>();
            updates.Setup(service => service.GetAvailableUpdatesAsync(
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WindowsUpdate>
                {
                    new()
                    {
                        Id = "test-update",
                        Title = "Test Update",
                        KBArticle = "KB1234567",
                        Category = UpdateCategory.Security,
                        Status = status
                    }
                });
            return updates;
        }

        private static ServiceProvider BuildServices(IUpdateService updates) =>
            new ServiceCollection()
                .AddSingleton(updates)
                .BuildServiceProvider();

        private static void VerifyNoInstallMutation(
            Mock<IUpdateService> updates)
        {
            updates.Verify(service => service.DownloadUpdateAsync(
                    It.IsAny<WindowsUpdate>(),
                    It.IsAny<IProgress<double>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            updates.Verify(service => service.InstallUpdateAsync(
                    It.IsAny<WindowsUpdate>(),
                    It.IsAny<IProgress<double>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static async Task<CommandRunResult> InvokeAsync(
            Command command,
            params string[] args)
        {
            var root = new RootCommand();
            root.AddCommand(command);
            var parser = Program.BuildParser(root);

            TextReader originalIn = Console.In;
            TextWriter originalOut = Console.Out;
            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();

            try
            {
                Console.SetIn(input);
                Console.SetOut(output);
                int exitCode = await parser.InvokeAsync(args);
                return new CommandRunResult(exitCode, output.ToString());
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
            }
        }

        private sealed record CommandRunResult(int ExitCode, string Output);
    }
}
