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
            result.Output.Should().Contain("Welcome back!");
            result.Output.Should().Contain("›");
            result.Output.Should().Contain("╭");
            result.Output.Should().Contain("╰");
            result.Output.Should().Contain("Take care");
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
        public async Task RunAsync_MapsSlashUpdateAliasToParserArgs()
        {
            var result = await RunShellAsync("/upgrade --check", "/exit");

            result.Invocations.Should().ContainSingle()
                .Which.Should().Equal("upgrade", "--check");
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

            result.Output.Should().Contain("How it works:");
            result.Output.Should().Contain("Command palette");
            result.Output.Should().Contain("Keyboard shortcuts");
            result.Output.Should().Contain("Type /help <command> for command-specific options and examples.");
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
            result.Output.Should().Contain("Take care");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_ShowsHintForNonSlashInput()
        {
            var result = await RunShellAsync("list --installed", "/exit");

            result.Output.Should().Contain("Commands here start with /.");
            result.Output.Should().Contain("Try /list or type /help to see everything.");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_ShowsSuggestionForUnknownSlashCommand()
        {
            var result = await RunShellAsync("/lst", "/exit");

            result.Output.Should().Contain("\"/lst\" isn't a WUM command.");
            result.Output.Should().Contain("Did you mean /list?");
            result.Output.Should().Contain("Type /commands to browse, or /help for a quick tour.");
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

            commands.Should().Contain(new[] { "/status", "/list", "/install", "/update", "/upgrade" });
            commands.Should().HaveCountGreaterThan(10);
        }

        [Fact]
        public void GetCommandSuggestions_WithUpdatePrefix_MatchesUpdate()
        {
            InteractiveShell.GetCommandSuggestions("/up")
                .Select(s => s.Command)
                .Should().Contain("/update");
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

        [Fact]
        public void GetCommandSuggestions_ForUpdateListsOptions()
        {
            InteractiveShell.GetCommandSuggestions("/update ")
                .Select(s => s.Command)
                .Should().Contain(new[] { "--check", "--force" });
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
