namespace Strategist.Auth;

/// <summary>
/// The cross-service permission catalog. The identity server grants
/// permissions to roles as role claims (type <see cref="ClaimType"/>); they
/// ride inside issued JWTs, so any resource service can enforce them offline
/// via the same-named authorization policies registered by
/// <see cref="StrategistAuthExtensions.AddPermissionPolicies"/>
/// (<c>[Authorize(Policy = Permissions.X)]</c>). The Admin role implicitly
/// holds every permission. Tokens pick up permission changes on the next
/// sign-in/refresh.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    /// <summary>Read the user directory beyond the basics.</summary>
    public const string UsersRead = "users.read";
    /// <summary>Create/delete accounts and assign user roles.</summary>
    public const string UsersManage = "users.manage";
    /// <summary>List roles and the permission catalog.</summary>
    public const string RolesRead = "roles.read";
    /// <summary>Create/delete roles and grant their permissions.</summary>
    public const string RolesManage = "roles.manage";

    public static readonly string[] All =
        [UsersRead, UsersManage, RolesRead, RolesManage];
}
