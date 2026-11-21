using System.Net;
using Ganss.Xss;
using Microsoft.AspNetCore.Html;

namespace SamaTraining.Web.Services;

/// <summary>
/// US-04: workshop agenda written in a rich-text editor. Only safe formatting is kept
/// (headings, bold/italic/underline, lists, links, quotes, alignment, text direction, colours);
/// scripts, event handlers and other markup are removed when saving and again when displaying.
/// </summary>
public static class RichText
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "strike",
                     "ol", "ul", "li", "a", "blockquote", "span", "sub", "sup", "hr" })
        {
            s.AllowedTags.Add(tag);
        }

        s.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "target", "rel", "class", "style", "dir", "data-list" })
        {
            s.AllowedAttributes.Add(attr);
        }

        s.AllowedCssProperties.Clear();
        foreach (var css in new[] { "color", "background-color", "text-align", "direction" })
        {
            s.AllowedCssProperties.Add(css);
        }

        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");

        // Links open in a new tab and don't pass the page to the target site.
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement el && el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                el.SetAttribute("target", "_blank");
                el.SetAttribute("rel", "noopener noreferrer");
            }
        };
        return s;
    }

    /// <summary>True when the text came from the rich-text editor (HTML), false for older plain text.</summary>
    public static bool IsHtml(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.TrimStart().StartsWith('<');

    /// <summary>Cleans editor HTML before saving. Empty editor content ("&lt;p&gt;&lt;br&gt;&lt;/p&gt;") becomes null.</summary>
    public static string? Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        if (!IsHtml(html)) return html.Trim();
        var clean = Sanitizer.Sanitize(html).Trim();
        var textOnly = System.Text.RegularExpressions.Regex.Replace(clean, "<[^>]+>", "").Replace("&nbsp;", " ").Trim();
        return textOnly.Length == 0 && !clean.Contains("<hr", StringComparison.OrdinalIgnoreCase) ? null : clean;
    }

    /// <summary>Safe HTML for display. Plain text from before the editor keeps its line breaks.</summary>
    public static IHtmlContent ToHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return HtmlString.Empty;
        if (IsHtml(value)) return new HtmlString(Sanitizer.Sanitize(value));
        var encoded = WebUtility.HtmlEncode(value.Trim()).Replace("\r\n", "\n").Replace("\n", "<br>");
        return new HtmlString("<p>" + encoded + "</p>");
    }

    /// <summary>The editor's starting HTML: older plain text is converted so its line breaks are kept.</summary>
    public static string ForEditor(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : IsHtml(value) ? Sanitizer.Sanitize(value) : ToHtml(value).ToString()!;
}
