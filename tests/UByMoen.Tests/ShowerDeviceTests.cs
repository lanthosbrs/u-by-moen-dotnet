using UByMoen.Core;
using UByMoen.Core.Models;

namespace UByMoen.Tests;

public class ShowerDeviceTests
{
    [Theory]
    [InlineData(MoenConstants.ModeOff, false)]
    [InlineData(MoenConstants.ModePausedByPreset, false)]
    [InlineData(MoenConstants.ModeAdjusting, true)]
    [InlineData(MoenConstants.ModeReady, true)]
    [InlineData(MoenConstants.ModePause, true)]
    public void IsOn_ReturnsExpectedValue(string mode, bool expected)
    {
        var device = new ShowerDevice { Mode = mode };
        Assert.Equal(expected, device.IsOn);
    }

    [Theory]
    [InlineData("Master Bath", "Master Bath")]
    [InlineData(null, "Moen Shower ABC123")]
    public void DisplayName_ReturnsExpectedValue(string? name, string expected)
    {
        var device = new ShowerDevice { SerialNumber = "ABC123", Name = name };
        Assert.Equal(expected, device.DisplayName);
    }
}
