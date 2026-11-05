using System.Globalization;
using System.Text.RegularExpressions;

namespace Tekcom.Gestion.Domain.Logic;

public enum CoverageState
{
    /// <summary>Tiene póliza o número de afiliación y está vigente.</summary>
    Covered = 0,

    /// <summary>Falta la póliza o el número de afiliación, o el Excel dice PENDIENTE.</summary>
    Pending = 1,

    /// <summary>La vigencia ya terminó.</summary>
    Expired = 2,

    /// <summary>La vigencia termina en menos de 15 días.</summary>
    ExpiringSoon = 3,
}

/// <summary>Decide quién está al día con su seguro (póliza o afiliación al ISSS) y quién falta.</summary>
public static partial class CoverageCalculator
{
    public const int WarningDays = 15;

    [GeneratedRegex(@"(\d{1,2})/(\d{1,2})/(\d{4})")]
    private static partial Regex DatePattern();

    public static CoverageState Evaluate(string? status, string? policyNumber, DateOnly? coverageEnd, DateOnly today)
    {
        var text = (status ?? string.Empty).Trim().ToUpperInvariant();
        if (text.Contains("PENDIENTE", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(policyNumber))
        {
            return CoverageState.Pending;
        }

        if (coverageEnd is { } end)
        {
            if (end < today)
            {
                return CoverageState.Expired;
            }

            if (end <= today.AddDays(WarningDays))
            {
                return CoverageState.ExpiringSoon;
            }
        }

        return CoverageState.Covered;
    }

    /// <summary>Saca la fecha final de textos como "Vigencia: 29/10/2026 - 29/11/2026" (la última fecha que aparezca).</summary>
    public static DateOnly? ParseEnd(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        DateOnly? last = null;
        foreach (Match match in DatePattern().Matches(status))
        {
            if (DateOnly.TryParseExact(match.Value, ["d/M/yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                last = date;
            }
        }

        return last;
    }

    public static bool NeedsAttention(CoverageState state) => state != CoverageState.Covered;
}
