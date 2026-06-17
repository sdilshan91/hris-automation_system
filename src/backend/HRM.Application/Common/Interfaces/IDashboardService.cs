using HRM.Application.Common.Models;
using HRM.Application.Features.Dashboard.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Role-based KPI dashboard (US-RPT-005). A THIN COMPOSITION layer over the existing per-module services —
/// it does NOT recompute any metric and introduces NO new entity / migration. The role (hr / manager /
/// employee) is derived SERVER-SIDE from <see cref="ICurrentUser"/> (BR-1, FR-8); a client never supplies it.
///
/// <para>Widget DATA is gated by tenant (the EF global query filter + RLS, AC-5) and by role/permission
/// (FR-8). Each per-widget source call degrades gracefully — a widget whose source genuinely has no data is
/// omitted rather than failing the whole dashboard.</para>
///
/// <para>Caching (FR-4): when an <see cref="System.Object"/> IDistributedCache is registered (Redis in prod,
/// the in-memory fallback otherwise) widget values are cached under
/// <c>t:{tenantId}:dashboard:{role}:{userId}:{widgetKey}</c> with a ~3-minute TTL; <c>refresh=true</c>
/// bypasses + refreshes the entries.</para>
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Builds the ordered, role-appropriate widget set for the current user (AC-1/AC-2/AC-3). Set
    /// <paramref name="refresh"/> to bypass the FR-4 cache. Never fails for a missing widget source — it
    /// omits that widget instead. Returns 400 only when no tenant context is resolved.
    /// </summary>
    Task<Result<DashboardResponse>> GetWidgetsAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default);
}
