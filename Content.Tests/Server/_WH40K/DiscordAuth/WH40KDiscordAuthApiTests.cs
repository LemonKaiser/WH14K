using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WH40K.DiscordAuth;
using Moq;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests.Server._WH40K.DiscordAuth;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class WH40KDiscordAuthApiTests
{
    [Test]
    public void BuildAuthorizeUrlEscapesParameters()
    {
        var api = CreateApi(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var url = api.BuildAuthorizeUrl(
            "client id",
            "https://example.com/wh40k/discord-auth/callback?x=1",
            "state value",
            "identify guilds.members.read");

        Assert.Multiple(() =>
        {
            Assert.That(url, Does.StartWith("https://discord.com/oauth2/authorize?response_type=code"));
            Assert.That(url, Does.Contain("client_id=client%20id"));
            Assert.That(url, Does.Contain("redirect_uri=https%3A%2F%2Fexample.com%2Fwh40k%2Fdiscord-auth%2Fcallback%3Fx%3D1"));
            Assert.That(url, Does.Contain("state=state%20value"));
            Assert.That(url, Does.Contain("scope=identify%20guilds.members.read"));
        });
    }

    [Test]
    public async Task ExchangeAuthorizationCodeParsesTokenPayload()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "access_token": "access-token",
                  "refresh_token": "refresh-token",
                  "token_type": "Bearer",
                  "scope": "identify guilds.members.read",
                  "expires_in": 3600
                }
                """)
        });
        var api = CreateApi(handler);

        var result = await api.ExchangeAuthorizationCodeAsync(
            "client-id",
            "client-secret",
            "https://example.com/callback",
            "oauth-code");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.AccessToken, Is.EqualTo("access-token"));
            Assert.That(result.Value.RefreshToken, Is.EqualTo("refresh-token"));
            Assert.That(result.Value.Scope, Is.EqualTo("identify guilds.members.read"));
            Assert.That(result.Value.ExpiresIn, Is.EqualTo(3600));
            Assert.That(handler.LastRequest, Is.Not.Null);
            Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.LastRequest.Uri.AbsoluteUri, Is.EqualTo("https://discord.com/api/v10/oauth2/token"));
            Assert.That(handler.LastRequest.Body, Does.Contain("grant_type=authorization_code"));
            Assert.That(handler.LastRequest.Body, Does.Contain("code=oauth-code"));
            Assert.That(handler.LastRequest.Body, Does.Contain("client_id=client-id"));
            Assert.That(handler.LastRequest.Body, Does.Contain("client_secret=client-secret"));
        });
    }

    [Test]
    public async Task RefreshAccessTokenUsesRefreshGrant()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "access_token": "fresh-access",
                  "refresh_token": "fresh-refresh",
                  "token_type": "Bearer",
                  "scope": "identify",
                  "expires_in": 1200
                }
                """)
        });
        var api = CreateApi(handler);

        var result = await api.RefreshAccessTokenAsync("client-id", "client-secret", "stored-refresh");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.AccessToken, Is.EqualTo("fresh-access"));
            Assert.That(handler.LastRequest, Is.Not.Null);
            Assert.That(handler.LastRequest!.Body, Does.Contain("grant_type=refresh_token"));
            Assert.That(handler.LastRequest.Body, Does.Contain("refresh_token=stored-refresh"));
        });
    }

    [Test]
    public async Task GetCurrentUserParsesIdentityPayload()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "id": "1234567890",
                  "username": "demiurge",
                  "global_name": "Arch Demiurge",
                  "avatar": "hash123"
                }
                """)
        });
        var api = CreateApi(handler);

        var result = await api.GetCurrentUserAsync("player-access-token");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Id, Is.EqualTo("1234567890"));
            Assert.That(result.Value.Username, Is.EqualTo("demiurge"));
            Assert.That(result.Value.GlobalName, Is.EqualTo("Arch Demiurge"));
            Assert.That(handler.LastRequest, Is.Not.Null);
            Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(handler.LastRequest.Uri.AbsoluteUri, Is.EqualTo("https://discord.com/api/v10/users/@me"));
            Assert.That(handler.LastRequest.AuthorizationScheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastRequest.AuthorizationParameter, Is.EqualTo("player-access-token"));
        });
    }

    [Test]
    public async Task GetGuildMemberReturnsMissingForNotFound()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var api = CreateApi(handler);

        var result = await api.GetGuildMemberAsync("player-access-token", "guild-123");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(handler.LastRequest, Is.Not.Null);
            Assert.That(handler.LastRequest!.Uri.AbsoluteUri, Is.EqualTo("https://discord.com/api/v10/users/@me/guilds/guild-123/member"));
            Assert.That(handler.LastRequest.AuthorizationScheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastRequest.AuthorizationParameter, Is.EqualTo("player-access-token"));
        });
    }

    [Test]
    public async Task GetGuildMemberReturnsFailureForForbidden()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var api = CreateApi(handler);

        var result = await api.GetGuildMemberAsync("player-access-token", "guild-123");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(result.Error, Is.EqualTo("discord_member_status_403"));
        });
    }

    private static WH40KDiscordAuthApi CreateApi(Func<CapturedRequest, HttpResponseMessage> responseFactory)
    {
        return CreateApi(new CapturingHandler(responseFactory));
    }

    private static WH40KDiscordAuthApi CreateApi(CapturingHandler handler)
    {
        var client = new HttpClient(handler);
        var holder = new Mock<IHttpClientHolder>();
        holder.SetupGet(x => x.Client).Returns(client);
        return new WH40KDiscordAuthApi(holder.Object);
    }

    private static StringContent Json(string text)
    {
        return new StringContent(text, System.Text.Encoding.UTF8, "application/json");
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string AuthorizationScheme,
        string AuthorizationParameter,
        string Body);

    private sealed class CapturingHandler(Func<CapturedRequest, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public CapturedRequest LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            LastRequest = new CapturedRequest(
                request.Method,
                request.RequestUri ?? throw new InvalidOperationException("Missing request URI."),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body);

            return responseFactory(LastRequest);
        }
    }
}
