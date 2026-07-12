using System;
using System.Net;
using System.Text.RegularExpressions;

namespace DeusaldLocalizerCommon
{
    /// <summary>
    /// Turns a preview string into HTML that renders only a small whitelist of basic
    /// formatting tags (<c>&lt;b&gt; &lt;i&gt; &lt;br&gt; &lt;ins&gt; &lt;del&gt;
    /// &lt;sup&gt; &lt;sub&gt; &lt;small&gt;</c>). Everything else — including tag
    /// attributes, unknown tags and raw <c>&lt; &gt; &amp;</c> — stays escaped, so the
    /// result is safe to feed into a Blazor <c>MarkupString</c>.
    /// </summary>
    public static class PreviewHtmlSanitizer
    {
        // Matches an encoded whitelisted tag with no attributes, e.g. "&lt;b&gt;",
        // "&lt;/small&gt;" or a self-closing "&lt;br /&gt;". Longer tag names come first
        // so alternation prefers "ins" over "i". Attributes are intentionally not allowed:
        // anything with an attribute never matches and remains harmlessly escaped.
        private static readonly Regex _AllowedTag = new Regex(
            @"&lt;(/?)(br|ins|del|sup|sub|small|b|i)\s*/?&gt;",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ToSafeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            string encoded = WebUtility.HtmlEncode(text);

            return _AllowedTag.Replace(encoded, Restore);
        }

        private static string Restore(Match match)
        {
            string tag = match.Groups[2].Value.ToLowerInvariant();
            // <br> is void — always render it self-contained, ignoring any stray slash.
            if (tag == "br") return "<br>";
            return $"<{match.Groups[1].Value}{tag}>";
        }
    }
}
