using Tekcom.Gestion.Application.Common.Interfaces;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
