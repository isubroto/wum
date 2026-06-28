using System;
using FluentAssertions;
using WUM.CLI.Interactive;
using Xunit;

namespace WUM.CLI.Tests
{
    public class CommandLineTokenizerTests
    {
        [Fact]
        public void Tokenize_ReturnsEmptyArray_ForEmptyInput()
        {
            CommandLineTokenizer.Tokenize("")
                .Should().BeEmpty();
        }

        [Fact]
        public void Tokenize_SplitsSimpleOptions()
        {
            CommandLineTokenizer.Tokenize("list --installed --json")
                .Should().Equal("list", "--installed", "--json");
        }

        [Fact]
        public void Tokenize_PreservesDoubleQuotedArgument()
        {
            CommandLineTokenizer.Tokenize("search \"security update\"")
                .Should().Equal("search", "security update");
        }

        [Fact]
        public void Tokenize_PreservesSingleQuotedArgument()
        {
            CommandLineTokenizer.Tokenize("search 'driver update'")
                .Should().Equal("search", "driver update");
        }

        [Fact]
        public void Tokenize_PreservesSubcommandsAndArguments()
        {
            CommandLineTokenizer.Tokenize("settings set active-hours 9-18")
                .Should().Equal("settings", "set", "active-hours", "9-18");
        }

        [Fact]
        public void Tokenize_PreservesMultipleKbArguments()
        {
            CommandLineTokenizer.Tokenize("install KB5034441 KB5035853 --dry-run")
                .Should().Equal("install", "KB5034441", "KB5035853", "--dry-run");
        }

        [Fact]
        public void Tokenize_PreservesEmptyQuotedArgument()
        {
            CommandLineTokenizer.Tokenize("settings set note \"\"")
                .Should().Equal("settings", "set", "note", "");
        }

        [Fact]
        public void Tokenize_AllowsEscapedQuoteInsideQuotedArgument()
        {
            CommandLineTokenizer.Tokenize("search \"security \\\"update\\\"\"")
                .Should().Equal("search", "security \"update\"");
        }

        [Fact]
        public void Tokenize_ThrowsFormatException_ForUnmatchedQuote()
        {
            Action act = () => CommandLineTokenizer.Tokenize("search \"security update");

            act.Should().Throw<FormatException>()
                .WithMessage("Unmatched quote*");
        }
    }
}
