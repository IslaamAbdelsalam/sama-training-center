using Microsoft.Extensions.Options;

namespace SamaTraining.Web.Services;

/// <summary>A country students can register from: its calling code and how many digits follow it.</summary>
public class PhoneCountry
{
    /// <summary>Calling code without 00 or +, e.g. "249".</summary>
    public string DialCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;

    /// <summary>Number of digits after the calling code (mobile numbers, no leading 0).</summary>
    public int Digits { get; set; }

    /// <summary>Example of the digits after the code, used in hints.</summary>
    public string Example { get; set; } = string.Empty;
}

public enum PhoneCheck
{
    Valid,
    Empty,
    NotDigits,
    UnknownCountry,
    LeadingZero,
    WrongLength
}

public record PhoneResult(PhoneCheck Status, string? Normalized = null, PhoneCountry? Country = null);

/// <summary>
/// Phone rules for registrations.
/// The page shows a fixed "00" the student can't change; they type the country code and number.
/// Stored as digits only: country code + number, e.g. "249912345678".
/// Checked by country code + exact length. The same number can register only once per workshop.
/// </summary>
public class PhoneNumberService
{
    private static readonly List<PhoneCountry> Defaults = new()
    {
        new() { DialCode = "249", Name = "Sudan", NameAr = "السودان", Digits = 9, Example = "912345678" },
        new() { DialCode = "20", Name = "Egypt", NameAr = "مصر", Digits = 10, Example = "1001234567" },
        new() { DialCode = "211", Name = "South Sudan", NameAr = "جنوب السودان", Digits = 9, Example = "912345678" },
        new() { DialCode = "966", Name = "Saudi Arabia", NameAr = "السعودية", Digits = 9, Example = "501234567" },
        new() { DialCode = "971", Name = "UAE", NameAr = "الإمارات", Digits = 9, Example = "501234567" },
        new() { DialCode = "974", Name = "Qatar", NameAr = "قطر", Digits = 8, Example = "33123456" },
        new() { DialCode = "968", Name = "Oman", NameAr = "عُمان", Digits = 8, Example = "91234567" },
        new() { DialCode = "965", Name = "Kuwait", NameAr = "الكويت", Digits = 8, Example = "51234567" },
        new() { DialCode = "973", Name = "Bahrain", NameAr = "البحرين", Digits = 8, Example = "36123456" },
        new() { DialCode = "962", Name = "Jordan", NameAr = "الأردن", Digits = 9, Example = "791234567" },
        new() { DialCode = "218", Name = "Libya", NameAr = "ليبيا", Digits = 9, Example = "912345678" },
        new() { DialCode = "235", Name = "Chad", NameAr = "تشاد", Digits = 8, Example = "66123456" },
        new() { DialCode = "251", Name = "Ethiopia", NameAr = "إثيوبيا", Digits = 9, Example = "911234567" },
        new() { DialCode = "291", Name = "Eritrea", NameAr = "إريتريا", Digits = 7, Example = "7123456" },
        new() { DialCode = "44", Name = "United Kingdom", NameAr = "المملكة المتحدة", Digits = 10, Example = "7400123456" },
        new() { DialCode = "1", Name = "USA / Canada", NameAr = "أمريكا / كندا", Digits = 10, Example = "2025550123" },
    };

    private readonly IReadOnlyList<PhoneCountry> _countries;

    public PhoneNumberService(IOptions<AppSettings> settings)
    {
        var configured = settings.Value.PhoneCountries;
        _countries = (configured.Count > 0 ? configured : Defaults)
            .Where(c => !string.IsNullOrWhiteSpace(c.DialCode) && c.Digits > 0)
            .OrderByDescending(c => c.DialCode.Length) // longest code first
            .ToList();
    }

    public IReadOnlyList<PhoneCountry> Countries => _countries;

    /// <summary>Keeps digits only and removes a leading 00 or + if the student typed or pasted one.</summary>
    public static string Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        if (input.TrimStart().StartsWith("00") || digits.StartsWith("00"))
        {
            digits = digits.TrimStart('0');
        }
        return digits;
    }

    public PhoneResult Check(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new(PhoneCheck.Empty);

        // Anything other than digits, spaces, dashes, brackets or a leading + is a typo (letters etc.).
        var allowed = input.Trim().TrimStart('+');
        if (allowed.Any(c => !char.IsAsciiDigit(c) && c != ' ' && c != '-' && c != '(' && c != ')'))
        {
            return new(PhoneCheck.NotDigits);
        }

        var digits = Clean(input);
        if (digits.Length == 0) return new(PhoneCheck.Empty);

        var country = _countries.FirstOrDefault(c => digits.StartsWith(c.DialCode, StringComparison.Ordinal));
        if (country == null) return new(PhoneCheck.UnknownCountry);

        var national = digits[country.DialCode.Length..];
        if (national.StartsWith('0')) return new(PhoneCheck.LeadingZero, null, country);
        if (national.Length != country.Digits) return new(PhoneCheck.WrongLength, null, country);

        return new(PhoneCheck.Valid, digits, country);
    }

    /// <summary>"00249 912345678" for display and exports.</summary>
    public string Format(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return string.Empty;
        var digits = Clean(stored);
        var country = _countries.FirstOrDefault(c => digits.StartsWith(c.DialCode, StringComparison.Ordinal));
        return country == null ? "00" + digits : $"00{country.DialCode} {digits[country.DialCode.Length..]}";
    }
}
