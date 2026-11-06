using System.Diagnostics;

namespace authcraft_api.Tests;

public sealed class StagingReleaseSmokeTests
{
    [Fact]
    public void StagingSmoke_ResolvesFixedHostAndChecksHealthLoginAndRoles()
    {
        using var sandbox = new StagingSmokeSandbox();
        var result = sandbox.RunSmoke();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("staging smoke passed", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sandbox.AccessToken, result.Output + result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(sandbox.Password, result.Output + result.Error, StringComparison.Ordinal);
        Assert.Contains("/api/authentication/login", result.Output, StringComparison.Ordinal);
        Assert.Contains("/api/roles", result.Output, StringComparison.Ordinal);
        Assert.Equal(4, File.ReadAllLines(sandbox.HttpLogPath).Length);
    }

    [Theory]
    [InlineData("wrong-version")]
    [InlineData("wrong-tenant")]
    [InlineData("empty-roles")]
    public void StagingSmoke_RejectsHealthOrTenantMismatch(string failure)
    {
        using var sandbox = new StagingSmokeSandbox();
        var result = sandbox.RunSmoke(failure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(sandbox.AccessToken, result.Output + result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(sandbox.Password, result.Output + result.Error, StringComparison.Ordinal);
        if (failure == "wrong-tenant")
        {
            Assert.Contains("another tenant", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/api/roles", File.ReadAllLines(sandbox.HttpLogPath));
        }
        else if (failure == "wrong-version")
        {
            Assert.Equal(4, File.ReadAllLines(sandbox.HttpLogPath).Length);
        }
    }

    [Fact]
    public void StagingSmoke_RejectsMissingCredentialsBeforeNetworkRequests()
    {
        using var sandbox = new StagingSmokeSandbox();
        var result = sandbox.RunSmoke(missingPassword: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("health/", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(sandbox.HttpLogPath));
    }

    private sealed class StagingSmokeSandbox : IDisposable
    {
        private readonly string _scriptPath;
        private readonly string _harnessPath;
        private readonly Dictionary<string, string?> _originalEnvironment = new()
        {
            ["AUTHCRAFT_STAGING_SMOKE_EXPECTED_VERSION"] = Environment.GetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_EXPECTED_VERSION"),
            ["AUTHCRAFT_STAGING_SMOKE_IDENTIFIER"] = Environment.GetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_IDENTIFIER"),
            ["AUTHCRAFT_STAGING_SMOKE_PASSWORD"] = Environment.GetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_PASSWORD"),
            ["AUTHCRAFT_STAGING_SMOKE_CLIENT_ID"] = Environment.GetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_CLIENT_ID"),
            ["AUTHCRAFT_STAGING_SMOKE_TENANT_CLIENT_ID"] = Environment.GetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_TENANT_CLIENT_ID"),
            ["AUTHCRAFT_STAGING_SMOKE_FAILURE"] = Environment.GetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_FAILURE"),
            ["AUTHCRAFT_STAGING_SMOKE_HTTP_LOG"] = Environment.GetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_HTTP_LOG")
        };

        public StagingSmokeSandbox()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"authcraft-staging-smoke-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            _scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "Test-StagingRelease.ps1");
            _harnessPath = Path.Combine(DirectoryPath, "run-smoke.ps1");
            HttpLogPath = Path.Combine(DirectoryPath, "http-requests.log");
            AccessToken = CreateToken("staging-tenant");
            WrongTenantAccessToken = CreateToken("other-tenant");
            Password = $"Synthetic-{Guid.NewGuid():N}";
            File.WriteAllText(_harnessPath, $$"""
                function az {
                    if ($args -join ' ' -ne 'webapp show --resource-group AuthCraft --name authcraft-api-staging --query defaultHostName --output tsv') {
                        throw 'Unexpected Azure metadata request.'
                    }
                    return 'authcraft-api-staging-test.azurewebsites.net'
                }

                function Invoke-RestMethod {
                    param($Uri, $Method, $TimeoutSec, $Body, $ContentType, $Headers)
                    $script:requestUris.Add($Uri)
                    Add-Content -LiteralPath $env:AUTHCRAFT_STAGING_SMOKE_HTTP_LOG -Value $Uri
                    if ($Uri.EndsWith('/health/live') -or $Uri.EndsWith('/health/ready')) {
                        if ($env:AUTHCRAFT_STAGING_SMOKE_FAILURE -eq 'wrong-version') {
                            return @{ status = 'healthy'; version = 'other-commit' }
                        }
                        return @{ status = 'healthy'; version = $env:AUTHCRAFT_STAGING_SMOKE_EXPECTED_VERSION }
                    }
                    if ($Uri.EndsWith('/api/authentication/login')) {
                        $login = $Body | ConvertFrom-Json
                        if ($login.identifier -ne $env:AUTHCRAFT_STAGING_SMOKE_IDENTIFIER -or
                            $login.password -ne $env:AUTHCRAFT_STAGING_SMOKE_PASSWORD -or
                            $login.clientId -ne $env:AUTHCRAFT_STAGING_SMOKE_CLIENT_ID) {
                            throw 'Unexpected login request.'
                        }
                        if ($env:AUTHCRAFT_STAGING_SMOKE_FAILURE -eq 'wrong-tenant') {
                            return @{ AccessToken = '{{WrongTenantAccessToken}}'; TokenType = 'Bearer' }
                        }
                        return @{ AccessToken = '{{AccessToken}}'; TokenType = 'Bearer' }
                    }
                    if ($Uri.EndsWith('/api/roles')) {
                        if ($Headers.Authorization -ne 'Bearer {{AccessToken}}') {
                            throw 'Unexpected role authorization.'
                        }
                        if ($env:AUTHCRAFT_STAGING_SMOKE_FAILURE -eq 'wrong-tenant') {
                            throw 'The smoke script must reject a mismatched tenant before querying roles.'
                        }
                        if ($env:AUTHCRAFT_STAGING_SMOKE_FAILURE -eq 'empty-roles') {
                            return @()
                        }
                        return @('owner', 'user')
                    }
                    throw 'Unexpected HTTP request.'
                }

                $script:requestUris = [System.Collections.Generic.List[string]]::new()
                . '{{_scriptPath}}' -ExpectedVersion $env:AUTHCRAFT_STAGING_SMOKE_EXPECTED_VERSION -MaxAttempts 2 -RetryDelaySeconds 0
                $script:requestUris | ForEach-Object { Write-Output $_ }
                """);
        }

        public string DirectoryPath { get; }
        public string HttpLogPath { get; }
        public string AccessToken { get; }
        public string Password { get; }
        public string WrongTenantAccessToken { get; }

        public ProcessResult RunSmoke(string failure = "", bool missingPassword = false)
        {
            Environment.SetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_EXPECTED_VERSION", new string('a', 40));
            Environment.SetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_IDENTIFIER", "synthetic@example.test");
            Environment.SetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_PASSWORD", missingPassword ? null : Password);
            Environment.SetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_CLIENT_ID", "staging-tenant");
            Environment.SetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_TENANT_CLIENT_ID", "staging-tenant");
            Environment.SetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_FAILURE", failure);
            Environment.SetEnvironmentVariable("AUTHCRAFT_STAGING_SMOKE_HTTP_LOG", HttpLogPath);
            try
            {
                var processInfo = new ProcessStartInfo("powershell.exe")
                {
                    WorkingDirectory = FindRepositoryRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", _harnessPath })
                {
                    processInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(processInfo)!;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new ProcessResult(process.ExitCode, output, error);
            }
            finally
            {
                foreach (var (name, value) in _originalEnvironment)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        private static string CreateToken(string tenant)
        {
            var payload = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { tenant })))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return $"header.{payload}.signature";
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "authcraft-api.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
