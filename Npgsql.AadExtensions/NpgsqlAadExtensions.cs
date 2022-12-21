using System;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Npgsql.AadExtensions;

public static class NpgsqlAadExtensions
{
    public static TimeSpan SafeTokenLifetime { get; } = TimeSpan.FromMinutes(5);
    public static TimeSpan SafetyMargin { get; } = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<string, AccessToken> StoredTokens = new();

    public static bool UseAadUserFromToken(this NpgsqlDataSourceBuilder mapper, TokenCredential credential,
        string tenantId = null) =>
        mapper.UseAadUserFromTokenAsync(credential, tenantId).Result;

    public static bool UseAadUserFromToken(this NpgsqlDataSourceBuilder mapper, TokenCredential credential,
        IEnumerable<string> claimsToUse, string tenantId = null) =>
        mapper.UseAadUserFromTokenAsync(credential, claimsToUse, tenantId).Result;

    public static Task<bool> UseAadUserFromTokenAsync(this NpgsqlDataSourceBuilder mapper, TokenCredential credential,
        string tenantId = null) =>
        mapper.UseAadUserFromTokenAsync(credential, new[] { "unique_name", "upn", "appid", "oid" }, tenantId);

    public static async Task<bool> UseAadUserFromTokenAsync(this NpgsqlDataSourceBuilder mapper,
        TokenCredential credential,
        IEnumerable<string> claimsToUse, string tenantId = null)
    {
        var token = await credential.GetPgTokenAsync(tenantId);
        var parts = token.Token.Split('.');
        var base64String = parts[1].Replace('-', '+').Replace('_', '/');
        while (base64String.Length % 3 > 0)
            base64String += '=';
        var base64 = Convert.FromBase64String(base64String);
        var content = Encoding.UTF8.GetString(base64);
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        foreach (var claim in claimsToUse)
        {
            if (!payload!.TryGetValue(claim, out var value) || string.IsNullOrEmpty(value.ToString()))
                continue;
            mapper.ConnectionStringBuilder.Username = value.ToString();
            return true;
        }

        return false;
    }

    public static async Task<AccessToken> GetPgTokenAsync(this TokenCredential credential, string tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var context = new TokenRequestContext(new[]
                { "https://ossrdbms-aad.database.windows.net/.default" },
            tenantId: tenantId);
        return await credential.GetTokenAsync(context, cancellationToken);
    }

    public static NpgsqlDataSourceBuilder UseAadPasswordProvider(this NpgsqlDataSourceBuilder mapper,
        TokenCredential credential, string tenantId = null) =>
        mapper.UsePeriodicPasswordProvider(async (conn, cancellationToken) =>
            {
                var key = conn.Host ?? "default";
                if (!StoredTokens.TryGetValue(key, out var storedToken) &&
                    storedToken.ExpiresOn > DateTimeOffset.UtcNow.Add(SafeTokenLifetime + SafetyMargin))
                {
                    return storedToken.Token;
                }

                var result = await credential.GetPgTokenAsync(tenantId, cancellationToken);
                StoredTokens[key] = result;
                return result.Token;
            },
            SafeTokenLifetime.Subtract(SafetyMargin),
            TimeSpan.FromSeconds(5));
}