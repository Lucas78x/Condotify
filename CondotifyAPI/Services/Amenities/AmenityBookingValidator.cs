using CondotifyAPI.Domain.DTO.Amenities;

namespace CondotifyAPI.Services.Amenities;

public static class AmenityBookingValidator
{
    public static string? ValidateWindow(AmenityDTO amenity, DateTime date, TimeSpan slotStartTime, DateTime nowUtc)
    {
        var earliestAllowed = nowUtc.AddHours(amenity.MinAdvanceNoticeHours);
        var requestedStart = date.Date.Add(slotStartTime);

        if (requestedStart < earliestAllowed)
            return $"Este local exige ao menos {amenity.MinAdvanceNoticeHours} hora(s) de antecedencia.";

        var latestAllowed = nowUtc.Date.AddDays(amenity.MaxAdvanceDays);
        if (date.Date > latestAllowed)
            return $"Este local so pode ser agendado ate {amenity.MaxAdvanceDays} dia(s) no futuro.";

        return null;
    }

    public static bool HasOverlappingSlots(IEnumerable<AmenityScheduleSlotDTO> slots)
    {
        foreach (var day in slots.Where(x => x.Active).GroupBy(x => x.DayOfWeek))
        {
            var ordered = day.OrderBy(x => x.StartTime).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                if (ordered[index].StartTime < ordered[index - 1].EndTime)
                    return true;
            }
        }

        return false;
    }

    public static bool IsDateBlacked(IEnumerable<AmenityBlackoutDTO> blackouts, DateTime date)
    {
        var day = date.Date;
        return blackouts.Any(x => day >= x.StartDate.Date && day <= x.EndDate.Date);
    }

    public static bool HasReachedMonthlyLimit(int? monthlyLimit, int existingCountThisMonth)
    {
        if (monthlyLimit is null or <= 0) return false;
        return existingCountThisMonth >= monthlyLimit.Value;
    }

    public static bool CanCancel(AmenityDTO amenity, DateTime date, TimeSpan slotStartTime, DateTime nowUtc)
    {
        var slotStartsAt = date.Date.Add(slotStartTime);
        return nowUtc <= slotStartsAt.AddHours(-amenity.CancellationCutoffHours);
    }
}
