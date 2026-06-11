using FluentAssertions;
using HRM.Application.Common.Helpers;

namespace HRM.Tests.Unit;

/// <summary>
/// Tests for the lightweight UserAgentParser used by session list display (US-AUTH-009).
/// </summary>
public sealed class UserAgentParserTests
{
    [Theory]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
        "Desktop", "Chrome 125", "Windows 10+")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        "Desktop", "Safari 17", "macOS")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1",
        "Mobile", "Safari 17", "iOS")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.6367.113 Mobile Safari/537.36",
        "Mobile", "Chrome 124", "Android")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.2535.67",
        "Desktop", "Edge 125", "Windows 10+")]
    [InlineData(
        "Mozilla/5.0 (X11; Linux x86_64; rv:126.0) Gecko/20100101 Firefox/126.0",
        "Desktop", "Firefox 126", "Linux")]
    public void Parse_CommonUserAgents_ExtractsCorrectInfo(string ua, string expectedDevice, string expectedBrowser, string expectedOs)
    {
        var (device, browser, os) = UserAgentParser.Parse(ua);

        device.Should().Be(expectedDevice);
        browser.Should().Be(expectedBrowser);
        os.Should().Be(expectedOs);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsUnknown()
    {
        var (device, browser, os) = UserAgentParser.Parse(null);
        device.Should().Be("Unknown");
        browser.Should().Be("Unknown");
        os.Should().Be("Unknown");

        var (device2, browser2, os2) = UserAgentParser.Parse("");
        device2.Should().Be("Unknown");
        browser2.Should().Be("Unknown");
        os2.Should().Be("Unknown");
    }

    [Fact]
    public void Parse_iPad_DetectsAsTablet()
    {
        var ua = "Mozilla/5.0 (iPad; CPU OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1";
        var (device, _, os) = UserAgentParser.Parse(ua);

        device.Should().Be("Tablet");
        os.Should().Be("iOS");
    }
}
