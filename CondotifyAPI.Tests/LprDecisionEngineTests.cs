using CondotifyAPI.Domain.Enums.Equipments;
using CondotifyAPI.Services.Lpr;

namespace CondotifyAPI.Tests;

public class LprDecisionEngineTests
{
    [Fact]
    public void Decide_ReturnsNoRead_WhenConfidenceBelowThreshold()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.4, confidenceThreshold: 0.8, vehicleMatched: true, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.NoRead, action);
    }

    [Fact]
    public void Decide_ReturnsNoRead_WhenPlateWasNotRead()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: false, confidence: 0.0, confidenceThreshold: 0.8, vehicleMatched: false, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.NoRead, action);
    }

    [Fact]
    public void Decide_Opens_WhenMatchedAndAutoOpen()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: true, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.Opened, action);
    }

    [Fact]
    public void Decide_LogsOnly_WhenMatchedAndDetectionOnly()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: true, mode: LprModeEnum.DetectionOnly);

        Assert.Equal(LprAction.DetectedOnly, action);
    }

    [Fact]
    public void Decide_RaisesAlert_WhenNotMatchedAndAutoOpen()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: false, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.AlertRaised, action);
    }

    [Fact]
    public void Decide_LogsOnly_WhenNotMatchedAndDetectionOnly()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: false, mode: LprModeEnum.DetectionOnly);

        Assert.Equal(LprAction.DetectedOnly, action);
    }
}
