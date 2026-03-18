using System.Net;
using Content.Server._WH40K.DiscordAuth;
using NUnit.Framework;

namespace Content.Tests.Server._WH40K.DiscordAuth;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class WH40KDiscordAuthRefreshFailureClassifierTests
{
    [Test]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public void RequiresReauthAfterResolveFailure_ReturnsTrue_ForAuthStatuses(HttpStatusCode statusCode)
    {
        Assert.That(
            WH40KDiscordAuthRefreshFailureClassifier.RequiresReauthAfterResolveFailure(statusCode),
            Is.True);
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    public void RequiresReauthAfterRefreshTokenFailure_ReturnsTrue_ForAuthStatuses(HttpStatusCode statusCode)
    {
        Assert.That(
            WH40KDiscordAuthRefreshFailureClassifier.RequiresReauthAfterRefreshTokenFailure(statusCode),
            Is.True);
    }

    [Test]
    public void RequiresReauthAfterRefreshTokenFailure_ReturnsFalse_ForGenericFailure()
    {
        Assert.That(
            WH40KDiscordAuthRefreshFailureClassifier.RequiresReauthAfterRefreshTokenFailure(HttpStatusCode.InternalServerError),
            Is.False);
    }
}
