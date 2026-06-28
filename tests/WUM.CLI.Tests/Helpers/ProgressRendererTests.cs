using System;
using System.IO;
using FluentAssertions;
using WUM.CLI.Helpers;
using Xunit;

namespace WUM.CLI.Tests.Helpers
{
    public class ProgressRendererTests
    {
        [Fact]
        public void Update_WritesModernProgressLine()
        {
            string output = Capture(() =>
            {
                using var progress = new ProgressRenderer("    Downloading ", 20);
                progress.Update(26);
            });

            output.Should().Contain("Downloading");
            output.Should().Contain(" 26%");
            output.Should().Contain("━");
            output.Should().Contain("─");
            output.Should().NotContain("[");
            output.Should().NotContain("░");
        }

        [Fact]
        public void Complete_WritesFinalSuccessState()
        {
            string output = Capture(() =>
            {
                using var progress = new ProgressRenderer("Installing", 20);
                progress.Update(80);
                progress.Complete(true);
            });

            output.Should().Contain("✓");
            output.Should().Contain("100%");
            output.Should().Contain("done");
        }

        [Fact]
        public void Complete_WritesFinalFailureState()
        {
            string output = Capture(() =>
            {
                using var progress = new ProgressRenderer("Downloading", 20);
                progress.Update(42);
                progress.Complete(false);
            });

            output.Should().Contain("✗");
            output.Should().Contain(" 42%");
            output.Should().Contain("failed");
        }

        private static string Capture(Action action)
        {
            TextWriter originalOut = Console.Out;
            using var output = new StringWriter();

            try
            {
                Console.SetOut(output);
                action();
                return output.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
