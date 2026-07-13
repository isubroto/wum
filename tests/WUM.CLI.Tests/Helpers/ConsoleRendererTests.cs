// tests/WUM.CLI.Tests/Helpers/ConsoleRendererTests.cs
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using WUM.CLI.Helpers;
using WUM.Core.Models;
using Xunit;

namespace WUM.CLI.Tests.Helpers
{
    public class ConsoleRendererTests
    {
        [Fact]
        public void Success_WritesGreenText()
        {
            var sw = new StringWriter();
            Console.SetOut(sw);

            ConsoleRenderer.Success("Test message");

            sw.ToString().Should().Contain("Test message");
        }

        [Fact]
        public void Error_WritesText()
        {
            var sw = new StringWriter();
            Console.SetOut(sw);

            ConsoleRenderer.Error("Something broke");

            sw.ToString().Should().Contain("Something broke");
        }

        [Fact]
        public void UpdateTable_RendersDistinctCurrentStates()
        {
            TextWriter original = Console.Out;
            using var sw = new StringWriter();

            try
            {
                Console.SetOut(sw);
                TableRenderer.RenderUpdates(new List<WindowsUpdate>
                {
                    new() { Title = "A", Status = UpdateStatus.Available },
                    new() { Title = "B", Status = UpdateStatus.Downloading },
                    new() { Title = "C", Status = UpdateStatus.PendingInstallation },
                    new() { Title = "D", Status = UpdateStatus.Installing },
                    new() { Title = "E", Status = UpdateStatus.RestartRequired },
                    new() { Title = "F", Status = UpdateStatus.Failed },
                    new() { Title = "G", Status = UpdateStatus.SupersededOrNotApplicable }
                }, noColor: true);
            }
            finally
            {
                Console.SetOut(original);
            }

            string output = sw.ToString();
            output.Should().Contain("Available");
            output.Should().Contain("Downloading");
            output.Should().Contain("Pending install");
            output.Should().Contain("Installing");
            output.Should().Contain("Restart needed");
            output.Should().Contain("Failed");
            output.Should().Contain("Not applicable");
        }

        [Fact]
        public async Task SpinnerTimeout_CancelsAndObservesAction()
        {
            bool actionExited = false;
            TextWriter original = Console.Out;
            using var sw = new StringWriter();

            try
            {
                Console.SetOut(sw);
                Func<Task> act = () => ConsoleRenderer.ShowSpinnerAsync(
                    "Waiting...",
                    async ct =>
                    {
                        try
                        {
                            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                        }
                        finally
                        {
                            actionExited = true;
                        }
                    },
                    timeoutSeconds: 0,
                    silent: true);

                await act.Should().ThrowAsync<TimeoutException>();
            }
            finally
            {
                Console.SetOut(original);
            }

            actionExited.Should().BeTrue();
        }
    }
}
