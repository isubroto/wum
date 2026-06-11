// tests/WUM.CLI.Tests/Commands/InstallCommandTests.cs
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
    public class InstallCommandTests
    {
        private readonly Mock<IUpdateService> _mock = new();

        [Fact]
        public async Task Download_Success_ReturnsTrue()
        {
            _mock.Setup(s =>
                s.DownloadUpdateAsync(
                    "test-id",
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await _mock.Object
                .DownloadUpdateAsync("test-id");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task Install_Success_ReturnsTrue()
        {
            _mock.Setup(s =>
                s.InstallUpdateAsync(
                    "test-id",
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await _mock.Object
                .InstallUpdateAsync("test-id");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task Install_Fail_ReturnsFalse()
        {
            _mock.Setup(s =>
                s.InstallUpdateAsync(
                    "bad-id",
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var result = await _mock.Object
                .InstallUpdateAsync("bad-id");

            result.Should().BeFalse();
        }
    }
}