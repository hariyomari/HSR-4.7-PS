using System.Text.RegularExpressions;
using March7thHoney.Command;
using March7thHoney.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static readonly Regex RoleNamePattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);
    private static readonly Regex PermissionPattern = new("^[A-Za-z0-9_.*:-]{1,64}$", RegexOptions.Compiled);

    private static void MapAccessRoutes(IEndpointRouteBuilder api)
    {
        api.MapGet("/access", GetAccess);
        api.MapPut("/access/roles/{name}", UpsertRole);
        api.MapDelete("/access/roles/{name}", DeleteRole);
        api.MapPut("/access/default-role", SetDefaultRole);
        api.MapPut("/access/default-permissions", SetDefaultPermissions);
    }

    private static IResult GetAccess()
    {
        return Success(BuildAccessData());
    }

    private static IResult UpsertRole(string name, AdminRoleRequest? request)
    {
        request ??= new AdminRoleRequest();
        name = (name ?? string.Empty).Trim();
        if (!RoleNamePattern.IsMatch(name))
            return Error("Role names may only contain letters, digits, '_' and '-' (1-32 characters)");

        var roles = EnsureMutableRoles();
        var existingKey = FindRoleKey(roles, name);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in (request.permissions ?? []).SelectMany(CommandPermissions.Parse))
        {
            if (!PermissionPattern.IsMatch(token))
                return Error($"'{token}' is not a valid permission");

            if (token.StartsWith(CommandRoles.RoleReferencePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var referenced = token[CommandRoles.RoleReferencePrefix.Length..].Trim();
                if (string.Equals(referenced, existingKey ?? name, StringComparison.OrdinalIgnoreCase))
                    return Error("A role cannot inherit from itself");

                var referencedKey = FindRoleKey(roles, referenced);
                if (referencedKey == null)
                    return Error($"Referenced role '{referenced}' does not exist");

                permissions.Add(CommandRoles.RoleReferencePrefix + referencedKey);
                continue;
            }

            permissions.Add(token);
        }

        roles[existingKey ?? name] = permissions;
        ConfigManager.SaveConfig();
        ResyncAccountPermissions();

        return Success(BuildAccessData());
    }

    private static IResult DeleteRole(string name)
    {
        var roles = EnsureMutableRoles();
        var key = FindRoleKey(roles, (name ?? string.Empty).Trim());
        if (key == null)
            return Error("Role not found", statusCode: StatusCodes.Status404NotFound);

        if (string.Equals(key, NormalizeRole(null), StringComparison.OrdinalIgnoreCase))
            return Error("The default role cannot be deleted");

        if (string.Equals(key, CommandRoles.User, StringComparison.OrdinalIgnoreCase))
            return Error("The built-in User role cannot be deleted");

        var referencedBy = roles
            .Where(pair => !string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            .Where(pair => pair.Value.Any(permission =>
                permission.StartsWith(CommandRoles.RoleReferencePrefix, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(permission[CommandRoles.RoleReferencePrefix.Length..].Trim(), key,
                    StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .ToList();
        if (referencedBy.Count > 0)
            return Error($"Role is inherited by: {string.Join(", ", referencedBy)}");

        roles.Remove(key);
        ConfigManager.SaveConfig();

        // Accounts that held the removed role fall back to the default role.
        foreach (var account in GetAllAccounts())
        {
            if (string.Equals(account.Role, key, StringComparison.OrdinalIgnoreCase))
                account.SetRole(null);
        }

        return Success(BuildAccessData());
    }

    private static IResult SetDefaultRole(AdminDefaultRoleRequest? request)
    {
        request ??= new AdminDefaultRoleRequest();
        var roles = EnsureMutableRoles();
        var key = FindRoleKey(roles, (request.role ?? string.Empty).Trim());
        if (key == null)
            return Error("Role not found", statusCode: StatusCodes.Status404NotFound);

        ConfigManager.Config.ServerOption.DefaultPermissionRole = key;
        ConfigManager.SaveConfig();
        ResyncAccountPermissions();

        return Success(BuildAccessData());
    }

    private static IResult SetDefaultPermissions(AdminDefaultPermissionsRequest? request)
    {
        request ??= new AdminDefaultPermissionsRequest();
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in (request.permissions ?? []).SelectMany(CommandPermissions.Parse))
        {
            if (!PermissionPattern.IsMatch(token) ||
                token.StartsWith(CommandRoles.RoleReferencePrefix, StringComparison.OrdinalIgnoreCase))
                return Error($"'{token}' is not a valid permission");
            permissions.Add(token);
        }

        ConfigManager.Config.ServerOption.DefaultPermissions = permissions;
        ConfigManager.SaveConfig();
        ConfigManager.WarnIfDefaultPermissionsTooBroad();
        ResyncAccountPermissions();

        return Success(BuildAccessData());
    }

    private static AccessData BuildAccessData()
    {
        var serverOption = ConfigManager.Config.ServerOption;
        var configuredRoles = GetConfiguredRoles();
        var roles = configuredRoles
            .Select(role => new AccessRole(
                role.Key,
                role.Value.OrderBy(permission => permission).ToList(),
                CommandRoles
                    .GetPermissions(role.Key, serverOption.DefaultPermissionRole, configuredRoles)
                    .OrderBy(permission => permission)
                    .ToList()))
            .OrderBy(role => GetRoleNames().IndexOf(role.name))
            .ToList();

        return new AccessData(
            roles,
            NormalizeRole(null),
            serverOption.DefaultPermissions.OrderBy(permission => permission).ToList(),
            CommandPermissions.KnownPermissions().OrderBy(permission => permission).ToList());
    }

    private static Dictionary<string, HashSet<string>> EnsureMutableRoles()
    {
        var option = ConfigManager.Config.ServerOption;
        option.PermissionRoles ??= CommandRoles.DefaultRoles();
        return option.PermissionRoles;
    }

    private static string? FindRoleKey(Dictionary<string, HashSet<string>> roles, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var key in roles.Keys)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return key;

        return null;
    }

    // Base permissions are derived from the role tables at runtime, but the cached copy on each account is only
    // rewritten when it is next read; do it now so the accounts list reflects the change immediately.
    private static void ResyncAccountPermissions()
    {
        foreach (var account in GetAllAccounts())
        {
            try
            {
                account.SyncPermissionsWithRole();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    public class AdminRoleRequest
    {
        public List<string>? permissions { get; set; }
    }

    public class AdminDefaultRoleRequest
    {
        public string? role { get; set; }
    }

    public class AdminDefaultPermissionsRequest
    {
        public List<string>? permissions { get; set; }
    }
}
