using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Identity;
using Tekcom.Gestion.Infrastructure.Identity;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class UserAdminService(UserManager<ApplicationUser> users) : IUserAdminService
{
    public async Task<List<UserListItem>> ListAsync(CancellationToken ct = default)
    {
        var list = await users.Users.OrderBy(u => u.Email).ToListAsync(ct);
        var result = new List<UserListItem>();
        foreach (var user in list)
        {
            var roles = await users.GetRolesAsync(user);
            result.Add(new UserListItem(user.Id, user.Email ?? user.UserName ?? string.Empty, user.FullName,
                roles.OrderBy(r => r).ToList(), await users.IsLockedOutAsync(user), user.PhoneNumber));
        }

        return result;
    }

    public async Task SetPhoneAsync(string userId, string? phone, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new BusinessRuleException("El usuario no existe.");
        var clean = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (clean is not null && clean.Count(char.IsDigit) < 8)
        {
            throw new BusinessRuleException("El teléfono debe tener al menos 8 dígitos.");
        }

        Ensure(await users.SetPhoneNumberAsync(user, clean));
    }

    public async Task<string> DisplayNameAsync(string userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(user.FullName) ? user.Email ?? user.UserName ?? string.Empty : user.FullName;
    }

    public async Task CreateAsync(string email, string fullName, string password, IEnumerable<string> roles, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim();
        if (email.Length == 0)
        {
            throw new BusinessRuleException("El correo es obligatorio.");
        }

        var roleList = ValidateRoles(roles);
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = (fullName ?? string.Empty).Trim(),
        };

        Ensure(await users.CreateAsync(user, password ?? string.Empty));
        if (roleList.Count > 0)
        {
            Ensure(await users.AddToRolesAsync(user, roleList));
        }
    }

    public async Task SetRolesAsync(string userId, IEnumerable<string> roles, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new BusinessRuleException("El usuario no existe.");
        var wanted = ValidateRoles(roles);
        var current = await users.GetRolesAsync(user);

        if (current.Contains(AppRoles.Admin) && !wanted.Contains(AppRoles.Admin))
        {
            await EnsureAnotherAdminAsync(user);
        }

        Ensure(await users.RemoveFromRolesAsync(user, current.Except(wanted)));
        Ensure(await users.AddToRolesAsync(user, wanted.Except(current)));
    }

    public async Task ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new BusinessRuleException("El usuario no existe.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Ensure(await users.ResetPasswordAsync(user, token, newPassword ?? string.Empty));
    }

    public async Task SetLockedAsync(string userId, bool locked, string currentUserId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new BusinessRuleException("El usuario no existe.");
        if (locked)
        {
            if (user.Id == currentUserId)
            {
                throw new BusinessRuleException("No puede bloquear su propio usuario.");
            }

            if (await users.IsInRoleAsync(user, AppRoles.Admin))
            {
                await EnsureAnotherAdminAsync(user);
            }
        }

        Ensure(await users.SetLockoutEnabledAsync(user, true));
        Ensure(await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null));
    }

    private async Task EnsureAnotherAdminAsync(ApplicationUser except)
    {
        var admins = await users.GetUsersInRoleAsync(AppRoles.Admin);
        var others = 0;
        foreach (var admin in admins.Where(a => a.Id != except.Id))
        {
            if (!await users.IsLockedOutAsync(admin))
            {
                others++;
            }
        }

        if (others == 0)
        {
            throw new BusinessRuleException("Debe quedar al menos un administrador activo.");
        }
    }

    private static List<string> ValidateRoles(IEnumerable<string> roles)
    {
        var list = (roles ?? []).Distinct().ToList();
        var invalid = list.FirstOrDefault(r => !AppRoles.All.Contains(r));
        if (invalid is not null)
        {
            throw new BusinessRuleException($"El rol '{invalid}' no existe.");
        }

        return list;
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new BusinessRuleException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
