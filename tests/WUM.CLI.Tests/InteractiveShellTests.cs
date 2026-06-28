using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using WUM.CLI.Interactive;
using Xunit;

namespace WUM.CLI.Tests
{
    public class InteractiveShellTests
    {
        [Fact]
        public async Task RunAsync_ShowsWelcomeAndExits()
        {
            var result = await RunShellAsync("/exit");

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("WUM interactive mode");
            result.Output.Should().Contain("›");
            result.Output.Should().Contain("╭");
            result.Output.Should().Contain("╰");
            result.Output.Should().Contain("Goodbye.");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_MapsSlashListToParserArgs()
        {
            var result = await RunShellAsync("/list", "/exit");

            result.Invocations.Should().ContainSingle()
                .Which.Should().Equal("list");
        }

        [Fact]
        public async Task RunAsync_PreservesOptions()
        {
            var result = await RunShellAsync("/list --installed", "/exit");

            result.Invocations.Should().ContainSingle()
                .Which.Should().Equal("list", "--installed");
        }

        [Fact]
        public async Task RunAsync_PreservesSubcommandsAndArguments()
        {
            var result = await RunShellAsync(
                "/settings set active-hours 9-18",
                "/exit");

            result.Invocations.Should().ContainSingle()
                .Which.Should().Equal("settings", "set", "active-hours", "9-18");
        }

        [Fact]
        public async Task RunAsync_PreservesQuotedArguments()
        {
            var result = await RunShellAsync(
                "/search \"security update\"",
                "/exit");

            result.Invocations.Should().ContainSingle()
                .Which.Should().Equal("search", "security update");
        }

        [Fact]
        public async Task RunAsync_HandlesSessionCommands()
        {
            var result = await RunShellAsync(
                "/help",
                "/?",
                "/commands",
                "/keys",
                "/clear",
                "/version",
                "/info",
                "/quit");

            result.Output.Should().Contain("Usage:");
            result.Output.Should().Contain("Command palette");
            result.Output.Should().Contain("Keyboard shortcuts");
            result.Output.Should().Contain("/settings set active-hours 9-18");
            result.ClearCalled.Should().BeTrue();
            result.InfoCalled.Should().BeTrue();
            result.Invocations.Should().ContainSingle()
                .Which.Should().Equal("--version");
        }

        [Fact]
        public async Task RunAsync_AllowsBareExit()
        {
            var result = await RunShellAsync("exit");

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("Goodbye.");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_ShowsHintForNonSlashInput()
        {
            var result = await RunShellAsync("list --installed", "/exit");

            result.Output.Should().Contain("Commands in interactive mode start with /.");
            result.Output.Should().Contain("Try /list or type /help.");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_ShowsSuggestionForUnknownSlashCommand()
        {
            var result = await RunShellAsync("/lst", "/exit");

            result.Output.Should().Contain("Unknown command: /lst");
            result.Output.Should().Contain("Did you mean /list?");
            result.Output.Should().Contain("Type /help to see available commands.");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_ShowsTokenizerErrorForUnmatchedQuote()
        {
            var result = await RunShellAsync("/search \"security update", "/exit");

            result.Output.Should().Contain("Unmatched quote");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public void GetCommandSuggestions_WithSlash_ListsAllCommands()
        {
            var commands = InteractiveShell.GetCommandSuggestions("/")
                .Select(s => s.Command);

            commands.Should().Contain(new[] { "/status", "/list", "/install" });
        }

        [Fact]
        public void GetCommandSuggestions_WithPrefix_MatchesCommand()
        {
            InteractiveShell.GetCommandSuggestions("/li")
                .Select(s => s.Command)
                .Should().Contain("/list");
        }

        [Fact]
        public void GetCommandSuggestions_WithoutSlash_ReturnsEmpty()
        {
            InteractiveShell.GetCommandSuggestions("list")
                .Should().BeEmpty();
        }


        [Fact]
        public void GetCommandSuggestions_AfterCommandSpace_ListsOptions()
        {
            InteractiveShell.GetCommandSuggestions("/list ")
                .Select(s => s.Command)
                .Should().Contain("--installed");
        }

        [Fact]
        public void GetCommandSuggestions_ForSubcommandPrefix_MatchesSubcommand()
        {
            InteractiveShell.GetCommandSuggestions("/schedule s")
                .Select(s => s.Command)
                .Should().Contain(new[] { "show", "set" });
        }

        [Fact]
        public void GetCommandSuggestions_ForSubcommandOptionPrefix_MatchesOption()
        {
            InteractiveShell.GetCommandSuggestions("/schedule set --t")
                .Select(s => s.Command)
                .Should().Contain("--time");
        }

        [Fact]
        public void GetGhostCompletion_CompletesCommand()
        {
            InteractiveShell.GetGhostCompletion("/li")
                .Should().Be("st");

            InteractiveShell.GetGhostCompletion("/list --j")
                .Should().Be("son");
        }
        private static async Task<ShellRunResult> RunShellAsync(params string[] lines)
        {
            string inputText = string.Join(Environment.NewLine, lines) +
                               Environment.NewLine;
            using var input = new StringReader(inputText);
            using var output = new StringWriter();

            var invocations = new List<string[]>();
            bool clearCalled = false;
            bool infoCalled = false;

            var shell = new InteractiveShell(
                args =>
                {
                    invocations.Add(args);
                    return Task.FromResult(0);
                },
                input,
                output,
                () => clearCalled = true,
                () =>
                {
                    infoCalled = true;
                    output.WriteLine("developer info");
                });

            int exitCode = await shell.RunAsync();

            return new ShellRunResult(
                exitCode,
                output.ToString(),
                invocations,
                clearCalled,
                infoCalled);
        }

        private sealed class ShellRunResult
        {
            public ShellRunResult(
                int exitCode,
                string output,
                List<string[]> invocations,
                bool clearCalled,
                bool infoCalled)
            {
                ExitCode = exitCode;
                Output = output;
                Invocations = invocations;
                ClearCalled = clearCalled;
                InfoCalled = infoCalled;
            }

            public int ExitCode { get; }
            public string Output { get; }
            public List<string[]> Invocations { get; }
            public bool ClearCalled { get; }
            public bool InfoCalled { get; }
        }
    }
}
