using System.Globalization;
using System.Text;

namespace Tekcom.Gestion.Domain.Logic;

/// <summary>
/// Normaliza nombres de materiales para compararlos: sin acentos, sin mayúsculas, sin símbolos
/// ni espacios repetidos. "SUB-BOX", "sub box" y "Sub_Box" dan la misma clave.
/// </summary>
public static class NameNormalizer
{
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
