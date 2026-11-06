using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace authcraft_api.Tests;

public sealed class EnvironmentInventoryTests
{
    [Fact]
    public void Inventory_ReportsExactlyThreeEnvironments_WithoutClaimingSqlVerification()
    {
        var result = RunInventory(Snapshot());
        Assert.Equal(0, result.ExitCode);
        var environments = result.Json["environments"]!.AsArray();
        Assert.Equal(3, environments.Count);
        Assert.All(environments, environment =>
            Assert.Equal("pending", environment!["database_permissions"]!["verification_state"]!.GetValue<string>()));
        Assert.Equal("snapshot", result.Json["source"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("missing_app")]
    [InlineData("crossed_vault")]
    [InlineData("missing_identity")]
    [InlineData("wrong_runtime")]
    [InlineData("production_workflow")]
    [InlineData("crossed_role")]
    public void Inventory_BlocksCriticalDiscrepancies(string discrepancy)
    {
        var snapshot = Snapshot();
        var staging = snapshot["environments"]!["staging"]!;
        switch (discrepancy)
        {
            case "missing_app":
                staging["application"] = null;
                break;
            case "crossed_vault":
                staging["vault_name"] = "authcraft-vault";
                break;
            case "missing_identity":
                staging["runtime_principal"] = null;
                break;
            case "wrong_runtime":
                staging["runtime_environment"] = "Production";
                break;
            case "production_workflow":
                snapshot["production_workflow_active"] = true;
                break;
            case "crossed_role":
                staging["publisher_scopes"] = new JsonArray("authcraft-api");
                break;
        }

        var result = RunInventory(snapshot);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("blocked", result.Json["verification_state"]!.GetValue<string>());
        Assert.NotEmpty(result.Json["blocking_reasons"]!.AsArray());
    }

    [Fact]
    public void Inventory_BlocksInaccessibleMetadata()
    {
        var snapshot = Snapshot();
        snapshot["metadata_errors"] = new JsonArray("Azure metadata unavailable");
        var result = RunInventory(snapshot);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Azure metadata unavailable", result.Json.ToJsonString());
    }

    [Fact]
    public void Inventory_ReportsUnfederatedIdentity_AsUnconfirmedPurpose()
    {
        var snapshot = Snapshot();
        snapshot["identities"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "oidc-msi-a9fa",
            ["federated_subjects"] = new JsonArray(),
            ["arm_roles"] = new JsonArray()
        });
        var result = RunInventory(snapshot);
        Assert.Equal("pending", result.Json["identity_audit"]![1]!["verification_state"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("alternate-publisher-role")]
    [InlineData("alternate-publisher-federation")]
    [InlineData("missing-identity-audit")]
    [InlineData("missing-role-audit")]
    [InlineData("missing-attached-identity-audit")]
    [InlineData("runtime-publisher-role")]
    [InlineData("runtime-unreviewed-role")]
    [InlineData("missing-controls")]
    [InlineData("scm-enabled")]
    [InlineData("ftp-enabled")]
    [InlineData("legacy-sql-secret")]
    [InlineData("invalid-control-type")]
    public void Inventory_BlocksRemainingOrUnverifiedProductionPublicationRoutes(string scenario)
    {
        var snapshot = Snapshot();
        var controls = snapshot["production_publication_controls"]!;
        switch (scenario)
        {
            case "alternate-publisher-role":
            case "alternate-publisher-federation":
                snapshot["environments"]!["production"]!["attached_user_identities"] = new JsonArray("oidc-msi-a9fa");
                snapshot["identities"]!.AsArray().Add(new JsonObject
                {
                    ["name"] = "oidc-msi-a9fa",
                    ["federated_subjects"] = scenario == "alternate-publisher-federation"
                        ? new JsonArray("repo:synthetic:ref:refs/heads/main") : new JsonArray(),
                    ["arm_roles"] = scenario == "alternate-publisher-role"
                        ? new JsonArray(new JsonObject
                        {
                            ["role"] = "Website Contributor",
                            ["scope"] = "/subscriptions/synthetic/resourceGroups/AuthCraft/providers/Microsoft.Web/sites/authcraft-api"
                        }) : new JsonArray()
                });
                break;
            case "missing-identity-audit":
                snapshot["identities"] = new JsonArray();
                break;
            case "missing-role-audit":
                snapshot["identities"]![0]!.AsObject().Remove("arm_roles");
                break;
            case "missing-attached-identity-audit":
                snapshot["environments"]!["production"]!.AsObject().Remove("attached_user_identities");
                break;
            case "runtime-publisher-role":
            case "runtime-unreviewed-role":
                snapshot["environments"]!["production"]!["runtime_arm_roles"] = new JsonArray(new JsonObject
                {
                    ["role"] = scenario == "runtime-publisher-role" ? "Website Contributor" : "Unreviewed custom role",
                    ["scope"] = "/subscriptions/synthetic/resourceGroups/AuthCraft/providers/Microsoft.Web/sites/authcraft-api"
                });
                break;
            case "missing-controls":
                snapshot.AsObject().Remove("production_publication_controls");
                break;
            case "scm-enabled":
                controls["scm_basic_auth_enabled"] = true;
                break;
            case "ftp-enabled":
                controls["ftp_basic_auth_enabled"] = true;
                break;
            case "legacy-sql-secret":
                controls["legacy_sql_secret_present"] = true;
                break;
            case "invalid-control-type":
                controls["scm_basic_auth_enabled"] = "false";
                break;
        }

        var result = RunInventory(snapshot);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("blocked", result.Json["verification_state"]!.GetValue<string>());
        Assert.Contains(result.Json["blocking_reasons"]!.AsArray(), reason =>
            reason!.GetValue<string>().StartsWith("production:", StringComparison.Ordinal));
        Assert.Equal("blocked", result.Json["environments"]![2]!["verification_state"]!.GetValue<string>());
        Assert.Equal("pending", result.Json["environments"]![2]!["database_permissions"]!["verification_state"]!.GetValue<string>());
    }

    private static JsonObject Snapshot()
    {
        return JsonNode.Parse("""
            {
              "environments": {
                "staging": {
                  "application": "authcraft-api-staging",
                  "database": "authcraft-db-staging",
                  "database_server": "authcraftdbbrasil",
                  "vault_name": "authcraft-vault-staging",
                  "runtime_environment": "Staging",
                  "runtime_principal": "staging-runtime",
                  "runtime_vaults": ["authcraft-vault-staging"],
                  "host": "staging.example.test",
                  "publisher_scopes": ["authcraft-api-staging"]
                },
                "production": {
                  "application": "authcraft-api",
                  "database": "authcraft-db",
                  "database_server": "authcraftdbbrasil",
                  "vault_name": "authcraft-vault",
                  "runtime_environment": "Production",
                  "runtime_principal": "production-runtime",
                  "runtime_vaults": ["authcraft-vault"],
                  "host": "production.example.test",
                  "publisher_scopes": [],
                  "attached_user_identities": [],
                  "runtime_arm_roles": [
                    {"role":"Key Vault Reader","scope":"/subscriptions/synthetic/resourceGroups/AuthCraft/providers/Microsoft.KeyVault/vaults/authcraft-vault"},
                    {"role":"Key Vault Secrets User","scope":"/subscriptions/synthetic/resourceGroups/AuthCraft/providers/Microsoft.KeyVault/vaults/authcraft-vault"}
                  ]
                }
              },
              "production_workflow_active": false,
              "metadata_errors": [],
              "identities": [
                {"name":"oidc-msi-aede","federated_subjects":[],"arm_roles":[]}
              ],
              "production_publication_controls": {
                "scm_basic_auth_enabled": false,
                "ftp_basic_auth_enabled": false,
                "legacy_sql_secret_present": false
              }
            }
            """)!.AsObject();
    }

    private static (int ExitCode, JsonNode Json) RunInventory(JsonObject snapshot)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "authcraft-api.slnx")))
        {
            repository = repository.Parent;
        }

        Assert.NotNull(repository);
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"authcraft-inventory-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(snapshotPath, snapshot.ToJsonString());
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repository.FullName
            };
            foreach (var argument in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File",
                         Path.Combine(repository.FullName, "scripts", "Test-EnvironmentInventory.ps1"),
                         "-SnapshotPath", snapshotPath
                     })
            {
                processInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(processInfo)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Inventory validation exceeded 30 seconds.");
            }

            var output = stdout.GetAwaiter().GetResult();
            Assert.True(!string.IsNullOrWhiteSpace(output), stderr.GetAwaiter().GetResult());
            return (process.ExitCode, JsonNode.Parse(output)!);
        }
        finally
        {
            File.Delete(snapshotPath);
        }
    }
}
