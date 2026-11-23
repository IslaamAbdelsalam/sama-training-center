using SamaTraining.Web.Data;
using SamaTraining.Web.Services;

namespace SamaTraining.Tests;

/// <summary>Every date on screen and in exports is dd/MM/yyyy.</summary>
public class DisplayFormatTests
{
    [Fact]
    public void Dates_AreDayMonthYear()
    {
        Assert.Equal("05/01/2026", DisplayFormat.D(new DateTime(2026, 1, 5)));
        Assert.Equal("05/01/2026 14:30", DisplayFormat.DT(new DateTime(2026, 1, 5, 14, 30, 0)));
        Assert.Equal("05/01/2026", DisplayFormat.ShortDates(new DateTime(2026, 1, 5, 9, 0, 0), new DateTime(2026, 1, 5, 13, 0, 0)));
        Assert.Equal("30/12/2026 – 02/01/2027", DisplayFormat.ShortDates(new DateTime(2026, 12, 30), new DateTime(2027, 1, 2)));

        var single = new Workshop { StartDateTime = new DateTime(2026, 1, 5, 9, 0, 0), EndDateTime = new DateTime(2026, 1, 5, 13, 0, 0) };
        Assert.Equal("Monday 05/01/2026", DisplayFormat.Dates(single, "en"));
        Assert.EndsWith(" 05/01/2026", DisplayFormat.Dates(single, "ar"));

        var range = new Workshop { StartDateTime = new DateTime(2026, 1, 5, 9, 0, 0), EndDateTime = new DateTime(2026, 1, 7, 13, 0, 0) };
        Assert.Equal("05/01/2026 – 07/01/2026 (3 days)", DisplayFormat.Dates(range, "en"));
        Assert.Equal("05/01/2026 – 07/01/2026 (3 أيام)", DisplayFormat.Dates(range, "ar"));
    }
}
