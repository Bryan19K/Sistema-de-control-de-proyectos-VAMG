using Tekcom.Gestion.Application.Common.Interfaces;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    private static readonly TimeZoneInfo ElSalvador = ResolveZone();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, ElSalvador).DateTime);

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/El_Salvador");
        }
        catch (TimeZoneNotFoundException)
        {
            // El Salvador no usa horario de verano: UTC-6 todo el año.
            return TimeZoneInfo.CreateCustomTimeZone("El Salvador", TimeSpan.FromHours(-6), "El Salvador", "El Salvador");
        }
    }
}
