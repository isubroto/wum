using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WUM.CLI;
using WUM.Core.Models;
using WUM.Core.Services;
using Xunit;

namespace WUM.CLI.Tests
{
    public class ProgramTests
    {
        [Theory]
        [MemberData(nameof(ValidCommandLines))]
        public void BuildParser_ParsesExistingOneShotCommands(string[] args)
        {
            using var services = BuildFakeServices();
            var parser = Program.BuildParser(Program.BuildRootCommand(services));

            var result = parser.Parse(args);

            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task Main_WithNoArgs_StartsInteractiveMode()
        {
            var result = await RunProgramAsync(
                Array.Empty<string>(),
                "/exit" + Environment.NewLine);

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("WUM interactive mode");
            result.Output.Should().Contain("›");
            result.Output.Should().Contain("Goodbye.");
        }

        [Fact]
        public async Task Main_WithInfo_PrintsDeveloperInfo()
        {
            var result = await RunProgramAsync(new[] { "--info" }, "");

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("WUM - Windows Update Manager CLI");
            result.Output.Should().Contain("Application");
            result.Output.Should().Contain("Runtime");
        }

        [Fact]
        public async Task Main_WithVersion_PrintsVersion()
        {
            var result = await RunProgramAsync(new[] { "--version" }, "");

            result.ExitCode.Should().Be(0);
            result.Output.Trim().Should().NotBeEmpty();
        }

        public static IEnumerable<object[]> ValidCommandLines()
        {
            yield return new object[] { new[] { "status" } };
            yield return new object[] { new[] { "list", "--installed" } };
            yield return new object[] { new[] { "search", "KB5034441" } };
            yield return new object[] { new[] { "install", "--all", "--dry-run" } };
            yield return new object[] { new[] { "uninstall", "KB5034441" } };
            yield return new object[] { new[] { "hide", "add", "update-id" } };
            yield return new object[] { new[] { "hide", "remove", "update-id" } };
            yield return new object[] { new[] { "hide", "list" } };
            yield return new object[] { new[] { "history" } };
            yield return new object[] { new[] { "pause", "--days", "14" } };
            yield return new object[] { new[] { "pause", "resume" } };
            yield return new object[] { new[] { "schedule" } };
            yield return new object[] { new[] { "schedule", "show" } };
            yield return new object[] { new[] { "schedule", "set", "--day", "Friday", "--time", "03:00" } };
            yield return new object[] { new[] { "schedule", "clear" } };
            yield return new object[] { new[] { "settings" } };
            yield return new object[] { new[] { "settings", "show" } };
            yield return new object[] { new[] { "settings", "set", "active-hours", "9-18" } };
            yield return new object[] { new[] { "settings", "reset" } };
            yield return new object[] { new[] { "reboot" } };
            yield return new object[] { new[] { "diagnose" } };
            yield return new object[] { new[] { "--help" } };
        }

        private static async Task<ProgramRunResult> RunProgramAsync(
            string[] args,
            string inputText)
        {
            TextReader originalIn = Console.In;
            TextWriter originalOut = Console.Out;

            using var input = new StringReader(inputText);
            using var output = new StringWriter();

            try
            {
                Console.SetIn(input);
                Console.SetOut(output);

                int exitCode = await Program.Main(args);
                return new ProgramRunResult(exitCode, output.ToString());
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
            }
        }

        private static ServiceProvider BuildFakeServices()
        {
            var updates = new Mock<IUpdateService>();
            updates.Setup(s => s.GetAvailableUpdatesAsync(
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WindowsUpdate>());

            updates.Setup(s => s.GetInstalledUpdatesAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WindowsUpdate>());

            return new ServiceCollection()
                .AddSingleton(updates.Object)
                .AddSingleton(Mock.Of<IPauseService>())
                .AddSingleton(Mock.Of<IHistoryService>())
                .AddSingleton(Mock.Of<ISchedulerService>())
                .AddSingleton(Mock.Of<ISettingsService>())
                .BuildServiceProvider();
        }

        private sealed class ProgramRunResult
        {
            public ProgramRunResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }

            public int ExitCode { get; }
            public string Output { get; }
        }
    }
}
