using System.Threading.Tasks;
using FluentAssertions;
using WUM.CLI.Helpers;
using Xunit;

namespace WUM.CLI.Tests.Helpers
{
    public class CommandPresentationTests
    {
        [Fact]
        public void FormatCommand_DefaultsToCliSyntax()
        {
            CommandPresentation.FormatCommand("list --refresh")
                .Should().Be("wum list --refresh");

            CommandPresentation.FormatCommand("--info", "info")
                .Should().Be("wum --info");
        }

        [Fact]
        public async Task BeginInteractive_UsesSlashSyntaxAcrossAsyncCalls_AndRestoresCli()
        {
            using (CommandPresentation.BeginInteractive())
            {
                await Task.Yield();
                CommandPresentation.FormatCommand("status")
                    .Should().Be("/status");
                CommandPresentation.FormatCommand("--info", "info")
                    .Should().Be("/info");
            }

            CommandPresentation.FormatCommand("status")
                .Should().Be("wum status");
        }

        [Fact]
        public void ElevationGuidance_MatchesPresentationMode()
        {
            CommandPresentation.ElevationGuidance
                .Should().Contain("Start-Process wum");

            using (CommandPresentation.BeginInteractive())
            {
                CommandPresentation.ElevationGuidance
                    .Should().Be(
                        "Exit interactive mode, then reopen WUM from an Administrator terminal.");
            }
        }
    }
}
