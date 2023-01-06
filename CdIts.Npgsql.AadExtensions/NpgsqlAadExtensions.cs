using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Npgsql;

namespace CdIts.Npgsql.AadExtensions;

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
        mapper.UseAadUserFromTokenAsync(credential, new[] { "unique_name", "upn", "oid", "sub" }, tenantId);

    public static async Task<bool> UseAadUserFromTokenAsync(this NpgsqlDataSourceBuilder mapper,
        TokenCredential credential,
        IEnumerable<string> claimsToUse, string tenantId = null)
    {
        var accessToken = await credential.GetPgTokenAsync(tenantId);
        var token = new JwtSecurityToken(accessToken.Token);
        foreach (var claimName in claimsToUse)
        {
            var value = token.Claims.FirstOrDefault(claim => claim.Type == claimName)?.Value;
            if(string.IsNullOrEmpty(value))
                continue;
            mapper.ConnectionStringBuilder.Username = value;
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