using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;

namespace Tekcom.Gestion.Application.Tests.Support;

/// <summary>Administración de usuarios en memoria para pruebas que solo necesitan leer nombres.</summary>
public sealed class FakeUserAdmin(params UserListItem[] users) : IUserAdminService
{
    public Task<List<UserListItem>> ListAsync(CancellationToken ct = default) => Task.FromResult(users.ToList());
    public Task<string> DisplayNameAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(users.FirstOrDefault(u => u.Id == userId)?.FullName ?? string.Empty);
    public Task CreateAsync(string email, string fullName, string password, IEnumerable<string> roles, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetRolesAsync(string userId, IEnumerable<string> roles, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetLockedAsync(string userId, bool locked, string currentUserId, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetPhoneAsync(string userId, string? phone, CancellationToken ct = default) => Task.CompletedTask;
}
