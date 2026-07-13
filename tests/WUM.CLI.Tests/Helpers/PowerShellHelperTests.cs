using System.Threading.Tasks;
using FluentAssertions;
using WUM.Core.Helpers;
using Xunit;

namespace WUM.CLI.Tests.Helpers
{
    public class PowerShellHelperTests
    {
        [Fact]
        public async Task RunScriptAsync_TransportsScriptBeyondCommandLineLimit()
        {
            string script = "#" + new string('x', 40_000) +
                "\nWrite-Output 'transport-ok'";

            var result = await PowerShellHelper.RunScriptAsync(script);

            result.Success.Should().BeTrue(result.Error);
            result.Output.Should().Be("transport-ok");
        }
    }
}
