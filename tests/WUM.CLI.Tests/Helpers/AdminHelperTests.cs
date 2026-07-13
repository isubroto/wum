using System;
using System.IO;
using FluentAssertions;
using WUM.CLI.Helpers;
using Xunit;

namespace WUM.CLI.Tests.Helpers
{
    public class AdminHelperTests
    {
        [Fact]
        public void RequireAdmin_WhenDenied_ReturnsFalseWithoutTerminatingProcess()
        {
            TextWriter originalOut = Console.Out;
            using var output = new StringWriter();

            try
            {
                Console.SetOut(output);
                using (AdminHelper.BeginAdminCheckOverride(() => false))
                {
                    AdminHelper.RequireAdmin().Should().BeFalse();
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            output.ToString().Should().Contain(
                "Administrator privileges required");
        }

        [Fact]
        public void RequireAdmin_WhenElevated_ReturnsTrueWithoutWarning()
        {
            TextWriter originalOut = Console.Out;
            using var output = new StringWriter();
            bool result;

            try
            {
                Console.SetOut(output);
                using (AdminHelper.BeginAdminCheckOverride(() => true))
                {
                    result = AdminHelper.RequireAdmin();
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            result.Should().BeTrue();
            output.ToString().Should().BeEmpty();
        }
    }
}
