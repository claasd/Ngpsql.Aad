# Extension to Npgsql to use Azure AD Token authentication

## Basics

You shuld be familiar with how AAD authentication for Azure Database for PostgreSQL (flexible server) works:
- https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-azure-ad-authentication
- https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/how-to-connect-with-managed-identity

This extension uses a given `TokenCredential` to create, provide and rotate a access token for Azure Database for PostgreSQL.

## Usage
Install the Extension [CdIts.Npgsql.AadExtensions](https://www.nuget.org/packages/CdIts.Npgsql.AadExtensions) via nuget


Use the `NpgsqlDataSourceBuilder` to register the extension:

```csharp
var builder = new NpgsqlDataSourceBuilder();
....
builder.UseAadPasswordProvider(new DefaultAzureCredential());
builder.Build();
```

If you need to extract the user from the AccessToken (e.g. the object-id of a managed-identity), you can use `UseAadUserFromToken` or `UseAadUserFromTokenAsync`. This will overwrite `builder.ConnectionStringBuilder.Username`. 

```csharp
var credential = new DefaultAzureCredential();
var builder = new NpgsqlDataSourceBuilder();
builder.UseAadUserFromToken(credential);
builder.UseAadPasswordProvider(credential);
builder.Build();
```

You can pass an optional `tenantId`:
```csharp
builder.UseAadPasswordProvider(credential, tenantId);
```

## License
MIT License
