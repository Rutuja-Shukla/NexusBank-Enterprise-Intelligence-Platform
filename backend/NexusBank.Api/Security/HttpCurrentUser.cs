using System.Security.Claims;
using NexusBank.Application.Common.Interfaces;

namespace NexusBank.Api.Security;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? Id
    {
        get
        {
            var value = Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string Email => Principal?.FindFirstValue("email") ?? "anonymous";

    public string DisplayName => Principal?.FindFirstValue(ClaimTypes.Name) ?? "Anonymous";

    public string Role => Principal?.FindFirstValue("role") ?? "anonymous";

    public IReadOnlySet<string> Permissions =>
        Principal?.FindAll("permission").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission) || Role == "administrator";
}
