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
    /// <summary>Browse projects, their strategy pipelines and activity.</summary>
    public const string ProjectsRead = "projects.read";
    /// <summary>Create/edit/delete projects and manage their strategy links.</summary>
    public const string ProjectsManage = "projects.manage";
    /// <summary>Obtain fleet access tokens (POST /api/v1/fleet/token) for the
    /// fleetd control plane.</summary>
    public const string FleetAccess = "fleet.access";
    /// <summary>Browse the research plane: backtest jobs and results, history
    /// fetch jobs, the instrument registry and venue catalogs.</summary>
    public const string ResearchRead = "research.read";
    /// <summary>Run backtests, queue history fetches, and manage the
    /// instrument registry (signup, mapping, deletion).</summary>
    public const string ResearchManage = "research.manage";

    public static readonly string[] All =
        [UsersRead, UsersManage, RolesRead, RolesManage, ProjectsRead, ProjectsManage, FleetAccess,
         ResearchRead, ResearchManage];
}
