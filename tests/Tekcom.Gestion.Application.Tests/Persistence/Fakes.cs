using Tekcom.Gestion.Application.Common.Interfaces;

namespace Tekcom.Gestion.Application.Tests.Persistence;

public sealed class FakeCurrentUser(string? userName = "bryan@tekcom.com") : ICurrentUserService
{
    public string? UserId { get; set; } = userName is null ? null : "user-1";
    public string? UserName { get; set; } = userName;
    public bool IsAuthenticated => UserName is not null;
}

public sealed class FakeDateTime(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
