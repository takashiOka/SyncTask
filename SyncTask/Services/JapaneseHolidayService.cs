namespace SyncTask.Services;

public sealed class JapaneseHolidayService : IHolidayService
{
    public bool IsHoliday(DateTime date)
    {
        var holidays = BuildHolidays(date.Year);
        return holidays.Contains(date.Date);
    }

    private static HashSet<DateTime> BuildHolidays(int year)
    {
        var holidays = new HashSet<DateTime>
        {
            new(year, 1, 1),
            new(year, 2, 11),
            new(year, 2, 23),
            new(year, 4, 29),
            new(year, 5, 3),
            new(year, 5, 4),
            new(year, 5, 5),
            new(year, 8, 11),
            new(year, 11, 3),
            new(year, 11, 23),
            new(year, 3, GetVernalEquinoxDay(year)),
            new(year, 9, GetAutumnalEquinoxDay(year)),
            GetNthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 2),
            GetNthWeekdayOfMonth(year, 7, DayOfWeek.Monday, 3),
            GetNthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 3),
            GetNthWeekdayOfMonth(year, 10, DayOfWeek.Monday, 2)
        };

        AddSubstituteHolidays(holidays, year);
        AddCitizensHolidays(holidays, year);
        return holidays;
    }

    private static void AddSubstituteHolidays(HashSet<DateTime> holidays, int year)
    {
        var source = holidays.ToList();
        foreach (var holiday in source)
        {
            if (holiday.DayOfWeek != DayOfWeek.Sunday)
            {
                continue;
            }

            var substitute = holiday.AddDays(1);
            while (substitute.Year == year && holidays.Contains(substitute))
            {
                substitute = substitute.AddDays(1);
            }

            if (substitute.Year == year)
            {
                holidays.Add(substitute.Date);
            }
        }
    }

    private static void AddCitizensHolidays(HashSet<DateTime> holidays, int year)
    {
        var date = new DateTime(year, 1, 2);
        var endDate = new DateTime(year, 12, 30);

        while (date <= endDate)
        {
            var prev = date.AddDays(-1);
            var next = date.AddDays(1);

            if (!holidays.Contains(date)
                && holidays.Contains(prev)
                && holidays.Contains(next)
                && date.DayOfWeek != DayOfWeek.Sunday)
            {
                holidays.Add(date);
            }

            date = date.AddDays(1);
        }
    }

    private static DateTime GetNthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int nth)
    {
        var date = new DateTime(year, month, 1);
        while (date.DayOfWeek != dayOfWeek)
        {
            date = date.AddDays(1);
        }

        return date.AddDays((nth - 1) * 7);
    }

    private static int GetVernalEquinoxDay(int year)
    {
        return (int)Math.Floor(20.8431 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4d));
    }

    private static int GetAutumnalEquinoxDay(int year)
    {
        return (int)Math.Floor(23.2488 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4d));
    }
}
