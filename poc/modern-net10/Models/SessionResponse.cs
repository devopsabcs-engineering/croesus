using System.Security.Claims;

namespace Croesus.ModernBff.Models;

internal sealed record SessionResponse(
    bool IsAuthenticated,
    string? DisplayName,
    string? TenantId)
{
    public static SessionResponse FromPrincipal(ClaimsPrincipal principal) =>
        new(
            principal.Identity?.IsAuthenticated == true,
            principal.FindFirstValue("name") ?? principal.Identity?.Name,
            NormalizeTenantId(principal.FindFirstValue("tid")));

    private static string? NormalizeTenantId(string? tenantId) =>
        Guid.TryParse(tenantId, out var parsedTenantId) && parsedTenantId != Guid.Empty
            ? parsedTenantId.ToString("D")
            : null;
}
