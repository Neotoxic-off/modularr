// Ported from Prowlarr NzbDrone.Core.Test/ParserTests/DateTimeUtilFixture.cs and DateTimeRoutinesFixture.cs.
using System.Globalization;
using Modularr.Core.Indexers.Cardigann;

namespace Modularr.Core.Tests.Indexers.Cardigann;

public sealed class DateTimeUtilTests
{
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void FormatsDateInvariant(string culture)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            DateTime now = DateTime.Now;

            Assert.Equal(
                now.ToString("ddd, dd MMM yyyy HH':'mm':'ss z", CultureInfo.InvariantCulture),
                DateTimeUtil.FromUnknown(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).ToString(DateTimeUtil.Rfc1123ZPattern, CultureInfo.InvariantCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("2022-08-08 02:07:39 -02:00", "2006-01-02 15:04:05 -07:00", "yyyy-MM-dd HH:mm:ss zzz", "2022-08-08 04:07:39 +00:00")]
    [InlineData("2022-08-08 02:07:39 -02:00", "yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd HH:mm:ss zzz", "2022-08-08 04:07:39 +00:00")]
    [InlineData("2022-08-08 -02:00", "2006-01-02 -07:00", "yyyy-MM-dd zzz", "2022-08-08 +00:00")]
    [InlineData("2022-08-08 -02:00", "yyyy-MM-dd zzz", "yyyy-MM-dd zzz", "2022-08-08 +00:00")]
    [InlineData("02:07:39 -02:00", "15:04:05 -07:00", "HH:mm:ss zzz", "04:07:39 +00:00")]
    [InlineData("02:07:39 -02:00", "HH:mm:ss zzz", "HH:mm:ss zzz", "04:07:39 +00:00")]
    [InlineData("-02:00", "zzz", "zzz", "+00:00")]
    [InlineData("-02:00", "-07:00", "zzz", "+00:00")]
    public void ParsesDateTimeGoLang(string dateInput, string format, string standardFormat, string expectedDate)
    {
        Assert.Equal(expectedDate, DateTimeUtil.ParseDateTimeGoLang(dateInput, format).ToUniversalTime().ToString(standardFormat, CultureInfo.InvariantCulture));
    }

    public static TheoryData<string, DateTime> FuzzyDates => new()
    {
        { "Member since:  \t10-Feb-2008", new DateTime(2008, 2, 10, 0, 0, 0) },
        { "Last Update: 18:16 11 Feb '08 ", new DateTime(2008, 2, 11, 18, 16, 0) },
        { "date\tTue, Feb 10, 2008 at 11:06 AM", new DateTime(2008, 2, 10, 11, 06, 0) },
        { "see at 12/31/2007 14:16:32", new DateTime(2007, 12, 31, 14, 16, 32) },
        { "sack finish 14:16:32 November 15 2008, 1-144 app", new DateTime(2008, 11, 15, 14, 16, 32) },
        { "Genesis Message - Wed 04 Feb 08 - 19:40", new DateTime(2008, 2, 4, 19, 40, 0) },
        { "The day 07/31/07 14:16:32 is ", new DateTime(2007, 7, 31, 14, 16, 32) },
        { "Shipping is on us until December 24, 2008 within the U.S. ", new DateTime(2008, 12, 24, 0, 0, 0) },
        { " 2008 within the U.S. at 14:16:32", DateTime.Today.AddHours(14).AddMinutes(16).AddSeconds(32) },
        { "5th November, 1994, 8:15:30 pm", new DateTime(1994, 11, 5, 20, 15, 30) },
        { "7 boxes January 31 , 14:16:32.", new DateTime(DateTime.Now.Year, 1, 31, 14, 16, 32) },
        { "the blue sky of Sept  30th  2008 14:16:32", new DateTime(2008, 9, 30, 14, 16, 32) },
        { " e.g. 1997-07-16T19:20:30+01:00", new DateTime(1997, 7, 16, 19, 20, 30) },
        { "Apr 1st, 2008 14:16:32 tufa 6767", new DateTime(2008, 4, 1, 14, 16, 32) },
        { "wait for 07/31/07 14:16:32", new DateTime(2007, 7, 31, 14, 16, 32) },
        { "later 12.31.08 and before 1.01.09", new DateTime(2008, 12, 31, 0, 0, 0) },
        { "Expires: Sept  30th  2008 14:16:32", new DateTime(2008, 9, 30, 14, 16, 32) },
        { "Offer expires Apr 1st, 2007, 14:16:32", new DateTime(2007, 4, 1, 14, 16, 32) },
        { "Expires  14:16:32 January 31.", new DateTime(DateTime.Now.Year, 1, 31, 14, 16, 32) },
        { "Expires  14:16:32 January 31-st.", new DateTime(DateTime.Now.Year, 1, 31, 14, 16, 32) },
        { "Expires 23rd January 2010.", new DateTime(2010, 1, 23, 0, 0, 0) },
        { "Expires January 22nd, 2010.", new DateTime(2010, 1, 22, 0, 0, 0) },
        { "Expires DEC 22, 2010.", new DateTime(2010, 12, 22, 0, 0, 0) },
        { "Version: 1.0.0.692 6/1/2010 2:28:04 AM ", new DateTime(2010, 6, 1, 2, 28, 4) },
        { "Version: 1.0.0.692 04/21/11 12:30am ", new DateTime(2011, 4, 21, 00, 30, 00) },
        { "Version: 1.0.0.692 04/21/11 12:30pm ", new DateTime(2011, 4, 21, 12, 30, 00) },
        { "Version: Thu Aug 06 22:32:15 MDT 2009 ", new DateTime(2009, 8, 6, 22, 32, 15) },
    };

    [Theory]
    [MemberData(nameof(FuzzyDates))]
    public void ParsesFuzzyDate(string text, DateTime expected)
    {
        Assert.True(DateTimeRoutines.TryParseDateOrTime(text, ukDate: false, out DateTime parsed));
        Assert.Equal(expected, parsed);
    }
}
