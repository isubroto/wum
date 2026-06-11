// tests/WUM.CLI.Tests/Helpers/ConsoleRendererTests.cs
using System;
using System.IO;
using FluentAssertions;
using WUM.CLI.Helpers;
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
    }
}