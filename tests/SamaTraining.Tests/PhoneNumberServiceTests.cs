using Microsoft.Extensions.Options;
using SamaTraining.Web.Services;

namespace SamaTraining.Tests;

public class PhoneNumberServiceTests
{
    private readonly PhoneNumberService _phones = new(Options.Create(new AppSettings()));

    [Theory]
    [InlineData("249912345678", "249912345678")]      // Sudan: 249 + 9
    [InlineData("00249912345678", "249912345678")]    // typed 00
    [InlineData("+249 91 234 5678", "249912345678")]  // pasted with + and spaces
    [InlineData("201001234567", "201001234567")]      // Egypt: 20 + 10
    [InlineData("97433123456", "97433123456")]        // Qatar: 974 + 8
    [InlineData("966501234567", "966501234567")]      // Saudi: 966 + 9
    [InlineData("211912345678", "211912345678")]      // South Sudan, not confused with Egypt "20"
    public void ValidNumbers_AreNormalizedToDigits(string input, string expected)
    {
        var r = _phones.Check(input);
        Assert.Equal(PhoneCheck.Valid, r.Status);
        Assert.Equal(expected, r.Normalized);
    }

    [Theory]
    [InlineData("", PhoneCheck.Empty)]
    [InlineData("24991234567", PhoneCheck.WrongLength)]    // one digit short
    [InlineData("2499123456789", PhoneCheck.WrongLength)]  // one digit long
    [InlineData("2490912345678", PhoneCheck.LeadingZero)]  // 0 after the code
    [InlineData("0912345678", PhoneCheck.UnknownCountry)]  // local number without code
    [InlineData("249-91a234567", PhoneCheck.NotDigits)]
    public void InvalidNumbers_AreRejected(string input, PhoneCheck expected)
    {
        Assert.Equal(expected, _phones.Check(input).Status);
    }

    [Fact]
    public void Format_ShowsFixed00AndCode()
    {
        Assert.Equal("00249 912345678", _phones.Format("249912345678"));
        Assert.Equal("0020 1001234567", _phones.Format("201001234567"));
    }
}
