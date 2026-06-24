// tests/WUM.CLI.Tests/Commands/StatusCommandTests.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WUM.Core.Models;
using WUM.Core.Services;
using Xunit;

namespace WUM.CLI.Tests.Commands
{
    public class StatusCommandTests
    {
        private readonly Mock<IUpdateService> _updatesMock = new();
        private readonly Mock<IPauseService>  _pauseMock   = new();

        [Fact]
        public async Task GetAvailableUpdates_ReturnsUpdates()
        {
            _updatesMock.Setup(s =>
                s.GetAvailableUpdatesAsync(
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<WindowsUpdate>
                {
                    new() {
                        Id       = "test-id",
                        Title    = "Test Security Update",
                        KBArticle = "KB1234567",
                        Category = UpdateCategory.Security
                    }
                });

            var result = await _updatesMock.Object
                .GetAvailableUpdatesAsync();

            result.Should().HaveCount(1);
            result[0].KBArticle.Should().Be("KB1234567");
            result[0].IsSecurityUpdate.Should().BeTrue();
        }

        [Fact]
        public async Task IsRebootRequired_ReturnsCorrectly()
        {
            _updatesMock.Setup(s => s.IsRebootRequired())
                .Returns(true);

            var result = _updatesMock.Object.IsRebootRequired();
            result.Should().BeTrue();
        }

        [Fact]
        public async Task GetPauseInfo_WhenNotPaused_ReturnsFalse()
        {
            _pauseMock.Setup(s => s.GetPauseInfoAsync())
                .ReturnsAsync(new PauseInfo { IsPaused = false });

            var info = await _pauseMock.Object.GetPauseInfoAsync();
            info.IsPaused.Should().BeFalse();
            info.DaysLeft.Should().Be(0);
        }
    }
}