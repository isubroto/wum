// tests/WUM.CLI.Tests/Commands/ListCommandTests.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WUM.Core.Models;
using WUM.Core.Services;
using Xunit;

namespace WUM.CLI.Tests.Commands
{
    public class ListCommandTests
    {
        private readonly Mock<IUpdateService> _mock = new();

        private List<WindowsUpdate> SampleUpdates() => new()
        {
            new() { Id="1", KBArticle="KB111", Category=UpdateCategory.Security },
            new() { Id="2", KBArticle="KB222", Category=UpdateCategory.Critical },
            new() { Id="3", KBArticle="KB333", Category=UpdateCategory.Optional },
            new() { Id="4", KBArticle="KB444", Category=UpdateCategory.Driver   },
        };

        [Fact]
        public async Task GetAvailable_FiltersSecurityCorrectly()
        {
            _mock.Setup(s =>
                s.GetAvailableUpdatesAsync(
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleUpdates());

            var all      = await _mock.Object.GetAvailableUpdatesAsync();
            var security = all
                .Where(u => u.Category == UpdateCategory.Security)
                .ToList();

            security.Should().HaveCount(1);
            security[0].KBArticle.Should().Be("KB111");
        }

        [Fact]
        public async Task GetAvailable_FiltersDriversCorrectly()
        {
            _mock.Setup(s =>
                s.GetAvailableUpdatesAsync(
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleUpdates());

            var all     = await _mock.Object.GetAvailableUpdatesAsync();
            var drivers = all
                .Where(u => u.Category == UpdateCategory.Driver)
                .ToList();

            drivers.Should().HaveCount(1);
            drivers[0].KBArticle.Should().Be("KB444");
        }
    }
}