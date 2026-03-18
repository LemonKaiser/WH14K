using System.Net;

namespace Content.Server._WH40K.DiscordAuth;

public static class WH40KDiscordAuthRefreshFailureClassifier
{
    public static bool RequiresReauthAfterResolveFailure(HttpStatusCode? statusCode)
    {
        return statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden;
    }

    public static bool RequiresReauthAfterRefreshTokenFailure(HttpStatusCode? statusCode)
    {
        return statusCode == HttpStatusCode.BadRequest || statusCode == HttpStatusCode.Unauthorized;
    }
}
