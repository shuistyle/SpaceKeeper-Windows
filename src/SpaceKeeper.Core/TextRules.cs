// ======================================================================
// TextRules.cs — small safety rules for text
// ======================================================================
//   DesktopNames.Clean – tidies a desktop name before it's saved in Windows
//   PrivacyText.Redact – hides your user folder (and so your Windows user
//                        name) in logs and diagnostics you might share
// Plain string rules with no Windows code, so they're covered by the
// automatic tests in tests/SpaceKeeper.Core.Tests.
// ======================================================================

using System.Globalization;
using System.Text;

namespace SpaceKeeper.Core;

public static class DesktopNames
{
    /// <summary>Longest name allowed (the same as the Mac version).</summary>
    public const int MaxLength = 60;

    /// <summary>
    /// Line breaks, tabs and other control characters become spaces, runs of
    /// spaces become one, the ends are trimmed, and the name is cut to
    /// <see cref="MaxLength"/> characters — without splitting an emoji or
    /// accented letter in half. (Emoji joiners such as in 👩‍💻 are kept.)
    /// </summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
            builder.Append(char.GetUnicodeCategory(ch) == UnicodeCategory.Control ? ' ' : ch);
        var collapsed = string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Count and cut by "text elements" (what a person sees as one character).
        var elements = StringInfo.GetTextElementEnumerator(collapsed);
        var result = new StringBuilder();
        var count = 0;
        while (elements.MoveNext() && count < MaxLength)
        {
            result.Append(elements.GetTextElement());
            count++;
        }
        return result.ToString().TrimEnd();
    }
}

public static class PrivacyText
{
    /// <summary>
    /// Replaces your user folder (e.g. C:\Users\AndrewFlowerdew) with
    /// %USERPROFILE%, so a shared log doesn't reveal your Windows user name.
    /// </summary>
    public static string Redact(string? text, string? userProfile)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (string.IsNullOrEmpty(userProfile)) return text;
        var redacted = text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        // Also the bare user name, e.g. in "C:\Users\AndrewFlowerdew" written another way.
        var userName = userProfile.TrimEnd('\\', '/').Split('\\', '/').Last();
        if (userName.Length >= 3)
            redacted = redacted.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        return redacted;
    }
}
