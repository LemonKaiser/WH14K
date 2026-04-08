using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Content.Server._WH40K.DiscordAuth;

public interface IWH40KDiscordAuthApi
{
    string BuildAuthorizeUrl(string clientId, string redirectUri, string requestId, string scope);

    Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>> ExchangeAuthorizationCodeAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        string code,
        CancellationToken cancel = default);

    Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>> RefreshAccessTokenAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken cancel = default);

    Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiUser>> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancel = default);

    Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiGuildMember?>> GetGuildMemberAsync(
        string accessToken,
        string guildId,
        CancellationToken cancel = default);

    Task RevokeTokenAsync(
        string clientId,
        string clientSecret,
        string token,
        CancellationToken cancel = default);
}

public sealed record WH40KDiscordAuthApiResult<T>(bool Success, T? Value, HttpStatusCode? StatusCode, string? Error);

public sealed record WH40KDiscordAuthApiToken(
    string AccessToken,
    string? RefreshToken,
    string TokenType,
    string Scope,
    int ExpiresIn);

public sealed record WH40KDiscordAuthApiUser(
    string Id,
    string Username,
    string? GlobalName,
    string? Avatar);

public sealed record WH40KDiscordAuthApiGuildMember(
    string? Nick,
    IReadOnlyList<string> Roles);

public sealed class WH40KDiscordAuthApi : IWH40KDiscordAuthApi
{
    private const string DiscordAuthorizeUrl = "https://discord.com/oauth2/authorize";
    private const string DiscordTokenUrl = "https://discord.com/api/v10/oauth2/token";
    private const string DiscordRevokeUrl = "https://discord.com/api/v10/oauth2/token/revoke";
    private const string DiscordCurrentUserUrl = "https://discord.com/api/v10/users/@me";
    private const string DiscordCurrentMemberUrlFormat = "https://discord.com/api/v10/users/@me/guilds/{0}/member";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public WH40KDiscordAuthApi(IHttpClientHolder http)
    {
        _http = http.Client;
    }

    public string BuildAuthorizeUrl(string clientId, string redirectUri, string requestId, string scope)
    {
        var redirect = Uri.EscapeDataString(redirectUri);
        var state = Uri.EscapeDataString(requestId);
        var encodedScope = Uri.EscapeDataString(scope);
        var encodedClientId = Uri.EscapeDataString(clientId);
        return $"{DiscordAuthorizeUrl}?response_type=code&client_id={encodedClientId}&scope={encodedScope}&redirect_uri={redirect}&state={state}&prompt=consent";
    }

    public Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>> ExchangeAuthorizationCodeAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        string code,
        CancellationToken cancel = default)
    {
        return ExchangeTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        }, cancel);
    }

    public Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>> RefreshAccessTokenAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken cancel = default)
    {
        return ExchangeTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancel);
    }

    public async Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiUser>> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DiscordCurrentUserUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendWithTimeoutAsync(request, cancel);

            if (!response.IsSuccessStatusCode)
                return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiUser>(false, null, response.StatusCode, $"discord_user_status_{(int) response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<DiscordUserResponse>(JsonOptions, cancel);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.Username))
                return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiUser>(false, null, response.StatusCode, "empty_user_payload");

            var user = new WH40KDiscordAuthApiUser(
                payload.Id.Trim(),
                payload.Username.Trim(),
                string.IsNullOrWhiteSpace(payload.GlobalName) ? null : payload.GlobalName.Trim(),
                string.IsNullOrWhiteSpace(payload.Avatar) ? null : payload.Avatar.Trim());

            return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiUser>(true, user, response.StatusCode, null);
        }
        catch (Exception e)
        {
            return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiUser>(false, null, null, e.Message);
        }
    }

    public async Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiGuildMember?>> GetGuildMemberAsync(
        string accessToken,
        string guildId,
        CancellationToken cancel = default)
    {
        try
        {
            var uri = string.Format(DiscordCurrentMemberUrlFormat, Uri.EscapeDataString(guildId));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await SendWithTimeoutAsync(request, cancel);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiGuildMember?>(true, null, response.StatusCode, null);

            if (!response.IsSuccessStatusCode)
                return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiGuildMember?>(false, null, response.StatusCode, $"discord_member_status_{(int) response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<DiscordGuildMemberResponse>(JsonOptions, cancel);
            var roles = payload?.Roles ?? [];
            var member = new WH40KDiscordAuthApiGuildMember(
                string.IsNullOrWhiteSpace(payload?.Nick) ? null : payload.Nick.Trim(),
                roles);

            return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiGuildMember?>(true, member, response.StatusCode, null);
        }
        catch (Exception e)
        {
            return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiGuildMember?>(false, null, null, e.Message);
        }
    }

    private async Task<WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>> ExchangeTokenAsync(
        Dictionary<string, string> formData,
        CancellationToken cancel)
    {
        try
        {
            using var content = new FormUrlEncodedContent(formData);
            using var response = await PostWithTimeoutAsync(DiscordTokenUrl, content, cancel);

            if (!response.IsSuccessStatusCode)
                return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>(false, null, response.StatusCode, $"discord_token_status_{(int) response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<DiscordTokenResponse>(JsonOptions, cancel);
            if (payload == null || string.IsNullOrWhiteSpace(payload.AccessToken))
                return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>(false, null, response.StatusCode, "empty_token_payload");

            var token = new WH40KDiscordAuthApiToken(
                payload.AccessToken,
                payload.RefreshToken,
                string.IsNullOrWhiteSpace(payload.TokenType) ? "Bearer" : payload.TokenType,
                string.IsNullOrWhiteSpace(payload.Scope) ? string.Empty : payload.Scope,
                payload.ExpiresIn);

            return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>(true, token, response.StatusCode, null);
        }
        catch (Exception e)
        {
            return new WH40KDiscordAuthApiResult<WH40KDiscordAuthApiToken>(false, null, null, e.Message);
        }
    }

    private sealed class DiscordTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        public string Scope { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class DiscordUserResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }

        public string? Avatar { get; set; }
    }

    private sealed class DiscordGuildMemberResponse
    {
        public string? Nick { get; set; }
        public List<string>? Roles { get; set; }
    }

    public async Task RevokeTokenAsync(
        string clientId,
        string clientSecret,
        string token,
        CancellationToken cancel = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["token"] = token,
        });
        using var response = await PostWithTimeoutAsync(DiscordRevokeUrl, content, cancel);
        // Discord returns 200 OK even for invalid tokens; nothing to check.
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage request,
        CancellationToken cancel)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        cts.CancelAfter(RequestTimeout);
        return await _http.SendAsync(request, cts.Token);
    }

    private async Task<HttpResponseMessage> PostWithTimeoutAsync(
        string url,
        HttpContent content,
        CancellationToken cancel)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        cts.CancelAfter(RequestTimeout);
        return await _http.PostAsync(url, content, cts.Token);
    }
}
