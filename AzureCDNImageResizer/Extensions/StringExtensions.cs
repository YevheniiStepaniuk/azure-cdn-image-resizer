using System;
namespace AzureCDNImageResizer.Extensions
{
    internal static class StringExtensions
    {
        internal static int ToInt(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;

            if (int.TryParse(s, out var i))
                return i;

            return 0;
        }

        internal static string ToSuffix(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            return s[(s.LastIndexOf('.') + 1)..];
        }
    }
}

