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
using WUM.Core.Models;
using WUM.Core.Services;
using Xunit;

namespace WUM.CLI.Tests.Commands
{
    public class ListCommandBehaviorTests
    {
        [Fact]
        public async Task HiddenOption_IncludesVisibleAndHiddenUpdates()
        {
            var updates = new Mock<IUpdateService>();
            updates.Setup(service => service.GetAvailableUpdatesAsync(
                    true,
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WindowsUpdate>
                {
                    new()
                    {
                        Id = "visible-update",
                        Title = "Visible Update",
                        KBArticle = "KB1111111",
                        IsHidden = false
                    },
                    new()
                    {
                        Id = "hidden-update",
                        Title = "Hidden Update",
                        KBArticle = "KB2222222",
                        IsHidden = true
                    }
                });

            using var services = new ServiceCollection()
                .AddSingleton<IUpdateService>(updates.Object)
                .BuildServiceProvider();
            var root = new RootCommand();
            root.AddCommand(new ListCommand(services).Build());
            var parser = Program.BuildParser(root);

            string output = await InvokeAsync(
                () => parser.InvokeAsync(
                    new[] { "list", "--hidden", "--json" }));

            output.Should().Contain("Visible Update");
            output.Should().Contain("Hidden Update");
            updates.Verify(service => service.GetAvailableUpdatesAsync(
                    true,
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static async Task<string> InvokeAsync(
            Func<Task<int>> invocation)
        {
            TextWriter originalOut = Console.Out;
            using var output = new StringWriter();

            try
            {
                Console.SetOut(output);
                await invocation();
                return output.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
