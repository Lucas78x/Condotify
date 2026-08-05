using CondotifyAPI.Domain.Enums.Equipments;

namespace CondotifyAPI.Services.Lpr;

internal enum LprAction
{
    NoRead,
    Opened,
    DetectedOnly,
    AlertRaised
}

internal static class LprDecisionEngine
{
    internal static LprAction Decide(bool plateWasRead, double confidence, double confidenceThreshold, bool vehicleMatched, LprModeEnum mode)
    {
        if (!plateWasRead || confidence < confidenceThreshold) return LprAction.NoRead;

        return (vehicleMatched, mode) switch
        {
            (true, LprModeEnum.AutoOpen) => LprAction.Opened,
            (true, LprModeEnum.DetectionOnly) => LprAction.DetectedOnly,
            (false, LprModeEnum.AutoOpen) => LprAction.AlertRaised,
            (false, LprModeEnum.DetectionOnly) => LprAction.DetectedOnly,
            _ => LprAction.NoRead
        };
    }
}
