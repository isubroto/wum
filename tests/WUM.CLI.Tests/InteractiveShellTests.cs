using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WUM.CLI.Commands;
using WUM.CLI.Interactive;
using WUM.Core.Models;
using WUM.Core.Services;
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

            string[] lines = result.Output.Split(
                Environment.NewLine,
                StringSplitOptions.None);
            string topBorder = lines.First(line => line.StartsWith("╭"));
            string contentLine = lines.First(line => line.StartsWith("│"));
            string bottomBorder = lines.First(line => line.StartsWith("╰"));
            topBorder.Length.Should().Be(contentLine.Length);
            bottomBorder.Length.Should().Be(contentLine.Length);
            topBorder.IndexOf('╮').Should().Be(contentLine.LastIndexOf('│'));
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
        public async Task RunAsync_HelpContainsOnlyInteractiveCommandSyntax()
        {
            var result = await RunShellAsync("/help", "/exit");

            result.Output.Should().Contain("/status");
            result.Output.Should().Contain("/list --installed");
            result.Output.Should().NotContain("wum status");
            result.Output.Should().NotContain("wum list");
            result.Output.Should().NotContain("wum install");
        }

        [Fact]
        public async Task RunAsync_CommandHelpDescribesOptionBehaviorAccurately()
        {
            var result = await RunShellAsync(
                "/help list",
                "/help search",
                "/help install",
                "/help uninstall",
                "/help reboot",
                "/help diagnose",
                "/exit");

            result.Output.Should().Contain("Include hidden updates");
            result.Output.Should().Contain(
                "Also query Microsoft Update for other Microsoft products");
            result.Output.Should().Contain(
                "Skip the installation confirmation prompt");
            result.Output.Should().Contain(
                "Do not prompt for restart after installation");
            result.Output.Should().Contain(
                "Skip the uninstall confirmation prompt");
            result.Output.Should().Contain(
                "Restart even if not required; skip confirmation");
            result.Output.Should().Contain(
                "Skip the reset confirmation prompt");
            result.Output.Should().NotContain("Only hidden updates");
            result.Output.Should().NotContain(
                "Use Microsoft Update instead of Windows Update");
            result.Output.Should().NotContain("Force installation");
            result.Output.Should().NotContain("Force uninstallation");
        }

        [Theory]
        [InlineData("--help")]
        [InlineData("-h")]
        [InlineData("-?")]
        public async Task RunAsync_CommandHelpFlagUsesInteractiveHelp(
            string helpOption)
        {
            var result = await RunShellAsync(
                "/list " + helpOption,
                "/exit");

            result.Output.Should().Contain("/list");
            result.Output.Should().Contain("Only installed updates");
            result.Output.Should().NotContain("Usage:" + Environment.NewLine + "  wum list");
            result.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task RunAsync_InvalidOptionDoesNotEmitCliUsage()
        {
            using var services = BuildStatusServices();
            Parser parser = BuildStatusParser(services);

            var result = await RunParserShellAsync(
                parser,
                "/status --not-a-real-option");

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain("could not be parsed");
            result.Output.Should().Contain("/help status");
            result.Output.Should().NotContain("Usage:" + Environment.NewLine + "  wum status");
        }

        [Fact]
        public async Task RunAsync_StatusHandlerUsesInteractiveCommandHints()
        {
            using var services = BuildStatusServices();
            Parser parser = BuildStatusParser(services);

            var result = await RunParserShellAsync(
                parser,
                "/status",
                "/exit");

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("/list -> see all updates");
            result.Output.Should().Contain("/install --security -> install security updates");
            result.Output.Should().Contain("/reboot -> restart to finish install");
            result.Output.Should().NotContain("wum list");
            result.Output.Should().NotContain("wum install");
            result.Output.Should().NotContain("wum reboot");
        }

        [Fact]
        public async Task StatusHandler_OneShotInvocationUsesCliCommandHints()
        {
            using var services = BuildStatusServices();
            Parser parser = BuildStatusParser(services);

            string output = await RunParserCommandAsync(parser, "status");

            output.Should().Contain("wum list -> see all updates");
            output.Should().Contain(
                "wum install --security -> install security updates");
            output.Should().Contain("wum reboot -> restart to finish install");
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
        public async Task RunAsync_ExplainsDiagnoseFailureExitCode()
        {
            var result = await RunShellAsync(
                _ => Task.FromResult(4),
                "/diagnose",
                "/exit");

            result.Output.Should().Contain("failed (exit 4)");
            result.Output.Should().Contain(
                "Windows Update service, BITS, or Crypto service");
            result.Output.Should().Contain("/diagnose --fix");
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
        private static Task<ShellRunResult> RunShellAsync(params string[] lines) =>
            RunShellAsync(_ => Task.FromResult(0), lines);

        private static async Task<ShellRunResult> RunShellAsync(
            Func<string[], Task<int>> invokeAsync,
            params string[] lines)
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
                    return invokeAsync(args);
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

        private static async Task<ShellRunResult> RunParserShellAsync(
            Parser parser,
            params string[] lines)
        {
            string inputText = string.Join(Environment.NewLine, lines) +
                               Environment.NewLine;
            TextReader originalIn = Console.In;
            TextWriter originalOut = Console.Out;

            using var input = new StringReader(inputText);
            using var output = new StringWriter();

            try
            {
                Console.SetIn(input);
                Console.SetOut(output);

                var shell = new InteractiveShell(parser, () => { });
                int exitCode = await shell.RunAsync();

                return new ShellRunResult(
                    exitCode,
                    output.ToString(),
                    new List<string[]>(),
                    false,
                    false);
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
            }
        }

        private static async Task<string> RunParserCommandAsync(
            Parser parser,
            params string[] args)
        {
            TextReader originalIn = Console.In;
            TextWriter originalOut = Console.Out;

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();

            try
            {
                Console.SetIn(input);
                Console.SetOut(output);
                await parser.InvokeAsync(args);
                return output.ToString();
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
            }
        }

        private static ServiceProvider BuildStatusServices()
        {
            var updates = new Mock<IUpdateService>();
            updates.Setup(service => service.GetServiceStatusAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("Running");
            updates.Setup(service => service.IsRebootRequired())
                .Returns(true);
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
                        Title = "Test Security Update",
                        KBArticle = "KB1234567",
                        Category = UpdateCategory.Security
                    }
                });

            var pause = new Mock<IPauseService>();
            pause.Setup(service => service.GetPauseInfoAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PauseInfo { IsPaused = false });

            return new ServiceCollection()
                .AddSingleton<IUpdateService>(updates.Object)
                .AddSingleton<IPauseService>(pause.Object)
                .BuildServiceProvider();
        }

        private static Parser BuildStatusParser(IServiceProvider services)
        {
            var root = new RootCommand();
            root.AddCommand(new StatusCommand(services).Build());
            return Program.BuildParser(root);
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
