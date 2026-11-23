using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace SamaTraining.Web.Security;

/// <summary>
/// Gives every validation attribute an explicit message so it can be translated
/// (built-in default messages are not passed through the localizer).
/// </summary>
public class DefaultValidationMessages : IValidationMetadataProvider, IDisplayMetadataProvider
{
    /// <summary>
    /// Fields without [Display(Name = ...)] get a readable, translatable label: "UserName" → "User name".
    /// </summary>
    public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
    {
        if (context.Key.MetadataKind != ModelMetadataKind.Property || context.Key.Name == null) return;
        if (context.DisplayMetadata.DisplayName != null) return;
        if (context.Attributes.OfType<DisplayAttribute>().Any(a => a.Name != null)) return;
        var label = Humanize(context.Key.Name);
        context.DisplayMetadata.DisplayName = () => Services.UiText.Get(label);
    }

    private static string Humanize(string name)
    {
        var words = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var a in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            if (a.ErrorMessage != null || a.ErrorMessageResourceName != null) continue;
            a.ErrorMessage = a switch
            {
                RequiredAttribute => "The {0} field is required.",
                StringLengthAttribute s when s.MinimumLength > 0 => "{0} must be between {2} and {1} characters.",
                StringLengthAttribute => "{0} can be at most {1} characters.",
                RangeAttribute => "{0} must be between {1} and {2}.",
                CompareAttribute => "{0} and {1} do not match.",
                EmailAddressAttribute => "Enter a valid e-mail address.",
                _ => a.ErrorMessage
            };
        }
    }
}
