namespace GovernedAgent.Console.Bff;

public sealed record DemoIdentity(string Id, IReadOnlySet<string> Roles)
{
    public const string UserHeader = "X-Demo-User";
    public const string RolesHeader = "X-Demo-Roles";
    public const string IncidentCommanderRole = "incident-commander";
    public const string GovernanceOperatorRole = "governance-operator";

    public bool IsInRole(string role) => Roles.Contains(role);

    public static IResult? Require(HttpRequest request, params string[] allowedRoles)
    {
        var userValues = request.Headers[UserHeader];
        var roleValues = request.Headers[RolesHeader];
        if (userValues.Count != 1 || roleValues.Count != 1)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Local demo identity required",
                detail: $"Provide exactly one {UserHeader} and {RolesHeader} header.");
        }

        var user = userValues.ToString().Trim();
        var rawRoles = roleValues.ToString();
        if (!IsSafeToken(user, 128))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid local demo identity");
        }

        var roles = rawRoles.Split(',', StringSplitOptions.TrimEntries);
        if (roles.Length is 0 or > 8 ||
            roles.Any(role => !IsSafeToken(role, 64)) ||
            roles.Distinct(StringComparer.Ordinal).Count() != roles.Length)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid local demo roles");
        }

        var identity = new DemoIdentity(user, roles.ToHashSet(StringComparer.Ordinal));
        if (!allowedRoles.Any(identity.IsInRole))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Insufficient local demo role");
        }

        request.HttpContext.Items[typeof(DemoIdentity)] = identity;
        return null;
    }

    public static DemoIdentity Current(HttpContext context) =>
        (DemoIdentity)context.Items[typeof(DemoIdentity)]!;

    private static bool IsSafeToken(string value, int maximumLength) =>
        value.Length is > 0 &&
        value.Length <= maximumLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.' or '@');
}
