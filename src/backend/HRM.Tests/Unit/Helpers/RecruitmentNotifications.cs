using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using NSubstitute;
using NSubstitute.Extensions;

namespace HRM.Tests.Unit.Helpers;

/// <summary>
/// BUG-530: <see cref="IRecruitmentNotificationService"/> now returns a <see cref="Result"/> from every method, so a
/// bare <c>Substitute.For&lt;IRecruitmentNotificationService&gt;()</c> no longer honours the contract — NSubstitute
/// cannot auto-construct the sealed <see cref="Result"/> and hands callers a <c>Task&lt;Result&gt;</c> carrying
/// <c>null</c>. Every test that only needs the seam to be *present* should build it here instead of re-declaring the
/// same <c>ReturnsForAll</c> in a dozen fixtures.
/// </summary>
public static class RecruitmentNotifications
{
    /// <summary>A seam that reports every dispatch as delivered — the default for tests not about delivery.</summary>
    public static IRecruitmentNotificationService Succeeding()
    {
        var notifications = Substitute.For<IRecruitmentNotificationService>();
        notifications.ReturnsForAll(Task.FromResult(Result.Success()));
        return notifications;
    }

    /// <summary>
    /// A seam that NEVER THROWS (the contract holds) but reports every dispatch as failed — the state the whole of
    /// BUG-530 exists to make observable. Callers that must retry are expected to act on this.
    /// </summary>
    public static IRecruitmentNotificationService Failing(string error = "smtp unavailable")
    {
        var notifications = Substitute.For<IRecruitmentNotificationService>();
        notifications.ReturnsForAll(
            Task.FromResult(Result.Failure(error, 502, "notification_dispatch_failed")));
        return notifications;
    }
}
