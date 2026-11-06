using System.Diagnostics;

namespace authcraft_api.Tests;

public sealed class StagingPublisherVerificationTests
{
    [Fact]
    public void Workflow_KeepsVerificationOptInAndOutsideArtifactUploadSteps()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "main_authcraft-api-staging.yml")).ReplaceLineEndings("\n");
        var jobBoundary = workflow.IndexOf("\n  verify-staging-publisher:", StringComparison.Ordinal);
        Assert.True(jobBoundary > 0);
        var validation = workflow[..jobBoundary];
        var diagnostic = workflow[jobBoundary..];

        Assert.Contains("default: false", validation, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", validation, StringComparison.Ordinal);
        foreach (var artifact in new[] { "api", "dacpac", "release-manifest" })
        {
            Assert.Contains($"          name: {artifact}\n", validation, StringComparison.Ordinal);
        }
        Assert.Contains("inputs.verify_staging_publisher == true", diagnostic, StringComparison.Ordinal);
        Assert.Contains("github.event_name == 'workflow_dispatch'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("github.ref == 'refs/heads/main'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("needs: validate", diagnostic, StringComparison.Ordinal);
        Assert.Contains("id-token: write", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Test-StagingPublisher.ps1", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("upload-artifact", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("webapps-deploy", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sql-action", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-DatabaseDeployment", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("wrong-ref", false)]
    [InlineData("pull-request", false)]
    [InlineData("wrong-repository", false)]
    [InlineData("not-actions", false)]
    [InlineData("wrong-account", false)]
    [InlineData("wrong-tenant", false)]
    [InlineData("wrong-subscription", false)]
    [InlineData("account-error", false)]
    [InlineData("token-error", false)]
    [InlineData("sql-failure", false)]
    [InlineData("missing-report", false)]
    [InlineData("skipped-test", false)]
    [InlineData("wrong-test", false)]
    public async Task Diagnostic_RequiresExpectedIdentityAndOneExecutedSqlCheck(string scenario, bool succeeds)
    {
        var repository = FindRepositoryRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"authcraft-publisher-wrapper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var scriptPath = Path.Combine(directory, "harness.ps1");
            var logPath = Path.Combine(directory, "commands.log");
            File.WriteAllText(scriptPath, """
                param([string]$Repository, [string]$Scenario)
                $ErrorActionPreference = 'Stop'
                function az {
                    $command = $args -join ' '
                    [IO.File]::AppendAllText($env:AUTHCRAFT_WRAPPER_LOG, "$command`n")
                    $global:LASTEXITCODE = 0
                    if ($command -eq 'account show --output json') {
                        if ($Scenario -eq 'account-error') { $global:LASTEXITCODE = 1; return }
                        $account = @{
                            id = '948a8f49-fd90-40d9-8fa9-12f722768a7a'
                            tenantId = 'b7bd0c07-88dc-4c47-81ba-608fd7607a82'
                            user = @{type = 'servicePrincipal';name = 'a2382f63-d653-47f3-82ca-9f2696f63f0a'}
                        }
                        if ($Scenario -eq 'wrong-account') { $account.user.name = 'unexpected' }
                        if ($Scenario -eq 'wrong-tenant') { $account.tenantId = 'unexpected' }
                        if ($Scenario -eq 'wrong-subscription') { $account.id = 'unexpected' }
                        return ($account | ConvertTo-Json)
                    }
                    if ($command -eq 'account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv') {
                        if ($Scenario -eq 'token-error') { $global:LASTEXITCODE = 1; return }
                        return 'synthetic.test.token'
                    }
                    throw 'Unexpected Azure command'
                }
                function dotnet {
                    [IO.File]::AppendAllText($env:AUTHCRAFT_WRAPPER_LOG, "dotnet test`n")
                    if ($env:AUTHCRAFT_SQL_ACCESS_TOKEN -cne 'synthetic.test.token' -or
                        $env:AUTHCRAFT_VERIFY_STAGING_PUBLISHER -cne 'true') { throw 'Missing process scope' }
                    if ($args -notcontains 'FullyQualifiedName=authcraft_api.Tests.StagingPublisherSqlTests.OidcPublisherHasOnlyApprovedReviewPermissions' -or
                        $args -notcontains '--no-build') { throw 'Wrong test command' }
                    $global:LASTEXITCODE = 0
                    if ($Scenario -eq 'sql-failure') {
                        Write-Output 'sensitive-native-diagnostic'
                        $global:LASTEXITCODE = 1
                        return
                    }
                    $resultIndex = [Array]::IndexOf($args, '--results-directory')
                    $resultPath = Join-Path $args[$resultIndex + 1] 'publisher.trx'
                    if ($Scenario -eq 'missing-report') { return }
                    $executed = '1'; $passed = '1'; $outcome = 'Passed'
                    $testName = 'authcraft_api.Tests.StagingPublisherSqlTests.OidcPublisherHasOnlyApprovedReviewPermissions'
                    if ($Scenario -eq 'skipped-test') { $executed = '0'; $passed = '0'; $outcome = 'NotExecuted' }
                    if ($Scenario -eq 'wrong-test') { $testName = 'UnexpectedTest' }
                    [IO.File]::WriteAllText($resultPath, @"
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Results><UnitTestResult testName="$testName" outcome="$outcome" /></Results>
                  <ResultSummary><Counters total="1" executed="$executed" passed="$passed" failed="0" /></ResultSummary>
                </TestRun>
                "@)
                }
                $env:AUTHCRAFT_SQL_ACCESS_TOKEN = 'original-token'
                $env:AUTHCRAFT_VERIFY_STAGING_PUBLISHER = 'original-opt-in'
                try {
                    & (Join-Path $Repository 'scripts\Test-StagingPublisher.ps1')
                } finally {
                    if ($env:AUTHCRAFT_SQL_ACCESS_TOKEN -cne 'original-token' -or
                        $env:AUTHCRAFT_VERIFY_STAGING_PUBLISHER -cne 'original-opt-in') {
                        throw 'Process environment was not restored'
                    }
                }
                """);
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory
            };
            foreach (var argument in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File", scriptPath,
                         "-Repository", repository, "-Scenario", scenario
                     })
            {
                processInfo.ArgumentList.Add(argument);
            }
            processInfo.Environment["AUTHCRAFT_WRAPPER_LOG"] = logPath;
            processInfo.Environment["TEMP"] = directory;
            processInfo.Environment["TMP"] = directory;
            processInfo.Environment["GITHUB_ACTIONS"] = scenario == "not-actions" ? "false" : "true";
            processInfo.Environment["GITHUB_REPOSITORY"] = scenario == "wrong-repository" ? "synthetic/other" : "leoperetti/authcraft-api";
            processInfo.Environment["GITHUB_REF"] = scenario == "wrong-ref" ? "refs/heads/feature" : "refs/heads/main";
            processInfo.Environment["GITHUB_EVENT_NAME"] = scenario == "pull-request" ? "pull_request" : "workflow_dispatch";
            processInfo.Environment["GITHUB_RUN_ID"] = "12345";
            processInfo.Environment["GITHUB_SHA"] = new string('a', 40);
            using var process = Process.Start(processInfo)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Publisher wrapper test exceeded 30 seconds.");
            }
            var output = await outputTask;
            var error = await errorTask;
            Assert.Equal(succeeds, process.ExitCode == 0);
            Assert.DoesNotContain("sensitive-native-diagnostic", output + error, StringComparison.Ordinal);
            Assert.DoesNotContain("Process environment was not restored", error, StringComparison.Ordinal);
            Assert.Empty(Directory.GetDirectories(directory, "authcraft-publisher-check-*"));
            if (succeeds)
            {
                Assert.Contains("\"deployment_authorized\":false", output, StringComparison.Ordinal);
                Assert.Equal(3, File.ReadAllLines(logPath).Length);
            }
            if (scenario is "wrong-ref" or "pull-request" or "wrong-repository" or "not-actions")
            {
                Assert.False(File.Exists(logPath));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
