using System.Diagnostics;
using System.Text.Json.Nodes;

namespace authcraft_api.Tests;

public sealed class ProductionDeploymentReportTests
{
    [Fact]
    public void Report_ContainsSpanishSummaryAndReleaseEvidence()
    {
        using var sandbox = new ReportSandbox();
        var result = sandbox.Generate(sandbox.CreateInput());

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Contains("# Informe de intento de publicación Production", result.Report, StringComparison.Ordinal);
        Assert.Contains("Resultado general: Exitoso", result.Report, StringComparison.Ordinal);
        Assert.Contains("123456789", result.Report, StringComparison.Ordinal);
        Assert.Contains(new string('a', 40), result.Report, StringComparison.Ordinal);
        Assert.Contains("production", result.Report, StringComparison.Ordinal);
        Assert.Contains("Aprobación protegida | aprobada", result.Report, StringComparison.Ordinal);
        Assert.Contains("DeployReport | disponible", result.Report, StringComparison.Ordinal);
        Assert.Contains("Script SQL | disponible", result.Report, StringComparison.Ordinal);
        Assert.DoesNotContain("AZURE_SQL", result.Report, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("blocked", "Bloqueado")]
    [InlineData("failed", "Fallido")]
    public void Report_MarksStagesAfterFailureAsNotExecuted(string failureStatus, string expectedResult)
    {
        using var sandbox = new ReportSandbox();
        var input = sandbox.CreateInput();
        input["approval"]!["status"] = "pending";
        input["approval"]!["reference"] = null;
        input["stages"] = new JsonArray
        {
            Stage("preflight", "succeeded"),
            Stage("schema_review", failureStatus, "schema_review_failed")
        };

        var result = sandbox.Generate(input);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Contains($"Resultado general: {expectedResult}", result.Report, StringComparison.Ordinal);
        Assert.Contains("La revisión del esquema no pasó.", result.Report, StringComparison.Ordinal);
        Assert.Contains("Revisa el DeployReport y el SQL generado antes de solicitar una nueva revisión.", result.Report, StringComparison.Ordinal);
        Assert.Contains("Aprobación protegida (approval) | no ejecutado", result.Report, StringComparison.Ordinal);
        Assert.Contains("Publicación del esquema (schema_publish) | no ejecutado", result.Report, StringComparison.Ordinal);
        Assert.Contains("Comprobación de autenticación (authentication_check) | no ejecutado", result.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_RejectsUnknownInputFieldsWithoutEchoingTheirValues()
    {
        using var sandbox = new ReportSandbox();
        var input = sandbox.CreateInput();
        input["unexpected_output"] = "SYNTHETIC_SECRET_MUST_NOT_LEAK person@example.com NATIVE_STDERR_MUST_NOT_LEAK";

        var result = sandbox.Generate(input);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(sandbox.ReportPath));
        Assert.DoesNotContain("SYNTHETIC_SECRET_MUST_NOT_LEAK", result.Output + result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.com", result.Output + result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("NATIVE_STDERR_MUST_NOT_LEAK", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_RejectsFailureCodesOutsideTheAllowlist()
    {
        using var sandbox = new ReportSandbox();
        var input = sandbox.CreateInput();
        input["stages"] = new JsonArray
        {
            Stage("preflight", "blocked", "synthetic-private-error")
        };

        var result = sandbox.Generate(input);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(sandbox.ReportPath));
        Assert.DoesNotContain("synthetic-private-error", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_RejectsFailureCodeThatDoesNotMatchItsStage()
    {
        using var sandbox = new ReportSandbox();
        var input = sandbox.CreateInput();
        input["stages"] = new JsonArray
        {
            Stage("preflight", "blocked", "schema_publish_failed")
        };

        var result = sandbox.Generate(input);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(sandbox.ReportPath));
    }

    [Fact]
    public void Report_RejectsStagesClaimedAfterBlockingFailure()
    {
        using var sandbox = new ReportSandbox();
        var input = sandbox.CreateInput();
        input["stages"] = new JsonArray
        {
            Stage("preflight", "blocked", "preflight_blocked"),
            Stage("schema_review", "succeeded")
        };

        var result = sandbox.Generate(input);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(sandbox.ReportPath));
    }

    [Fact]
    public void Report_RecordsApiFailureAfterSchemaAndLeavesLaterChecksNotExecuted()
    {
        using var sandbox = new ReportSandbox();
        var protectedFiles = sandbox.SnapshotProtectedFiles();
        var input = sandbox.CreateInput();
        input["stages"] = new JsonArray
        {
            Stage("preflight", "succeeded"),
            Stage("schema_review", "succeeded"),
            Stage("approval", "succeeded"),
            Stage("schema_publish", "succeeded"),
            Stage("application_publish", "failed", "application_publish_failed")
        };

        var result = sandbox.Generate(input);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Contains("el esquema pudo quedar aplicado.", result.Report, StringComparison.Ordinal);
        Assert.Contains("Aprobación protegida (approval) | exitoso", result.Report, StringComparison.Ordinal);
        Assert.Contains("Publicación del esquema (schema_publish) | exitoso", result.Report, StringComparison.Ordinal);
        Assert.Contains("Publicación de la API (application_publish) | fallido", result.Report, StringComparison.Ordinal);
        Assert.Contains("Comprobación de salud (health_check) | no ejecutado", result.Report, StringComparison.Ordinal);
        Assert.Contains("Comprobación de autenticación (authentication_check) | no ejecutado", result.Report, StringComparison.Ordinal);
        Assert.Equal(protectedFiles, sandbox.SnapshotProtectedFiles());
    }

    [Fact]
    public void Report_BlocksDeploymentWhenPreviousPackageIsIncompatible()
    {
        using var sandbox = new ReportSandbox();
        var protectedFiles = sandbox.SnapshotProtectedFiles();
        var input = sandbox.CreateInput();
        input["stages"] = new JsonArray
        {
            Stage("preflight", "blocked", "previous_package_incompatible")
        };

        var result = sandbox.Generate(input);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Contains("El paquete de API anterior no es compatible con el esquema resultante.", result.Report, StringComparison.Ordinal);
        Assert.Contains("No publiques una versión incompatible", result.Report, StringComparison.Ordinal);
        Assert.Contains("Publicación del esquema (schema_publish) | no ejecutado", result.Report, StringComparison.Ordinal);
        Assert.Contains("Publicación de la API (application_publish) | no ejecutado", result.Report, StringComparison.Ordinal);
        Assert.Equal(protectedFiles, sandbox.SnapshotProtectedFiles());
    }

    private static JsonObject Stage(string name, string status, string? failureCode = null)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["status"] = status,
            ["failure_code"] = failureCode
        };
    }

    private sealed class ReportSandbox : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _inputPath;
        private readonly string _directoryPath = Path.Combine(Path.GetTempPath(), $"authcraft-report-{Guid.NewGuid():N}");

        public ReportSandbox()
        {
            _repositoryRoot = FindRepositoryRoot();
            Directory.CreateDirectory(_directoryPath);
            _inputPath = Path.Combine(_directoryPath, "input.json");
            ReportPath = Path.Combine(_directoryPath, "production-deployment-report.md");
            File.WriteAllText(Path.Combine(_directoryPath, "previous-api-package.zip"), "synthetic-protected-release-state");
            File.WriteAllText(Path.Combine(_directoryPath, "previous-api-version.txt"), "synthetic-protected-release-state");
            File.WriteAllText(Path.Combine(_directoryPath, "recent-writes.json"), """{"write":"preserve"}""");
        }

        public string ReportPath { get; }

        public string[] SnapshotProtectedFiles()
        {
            var protectedPaths = new[]
            {
                Path.Combine(_directoryPath, "previous-api-package.zip"),
                Path.Combine(_directoryPath, "previous-api-version.txt"),
                Path.Combine(_directoryPath, "recent-writes.json")
            };
            return protectedPaths
                .Select(path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
                .ToArray();
        }

        public JsonObject CreateInput()
        {
            var successfulStages = new[]
            {
                "preflight",
                "schema_review",
                "approval",
                "schema_publish",
                "application_publish",
                "health_check",
                "authentication_check"
            };

            return new JsonObject
            {
                ["schema_version"] = 1,
                ["run_id"] = "123456789",
                ["commit_sha"] = new string('a', 40),
                ["release"] = "2026.11.07.1",
                ["target_environment"] = "production",
                ["started_at"] = "2026-11-07T12:00:00Z",
                ["finished_at"] = "2026-11-07T12:05:00Z",
                ["approval"] = new JsonObject
                {
                    ["status"] = "approved",
                    ["reference"] = "987654321"
                },
                ["evidence"] = new JsonObject
                {
                    ["deploy_report"] = new JsonObject
                    {
                        ["status"] = "available",
                        ["sha256"] = new string('b', 64)
                    },
                    ["deployment_script"] = new JsonObject
                    {
                        ["status"] = "available",
                        ["sha256"] = new string('c', 64)
                    }
                },
                ["stages"] = new JsonArray(successfulStages
                    .Select(stageName => (JsonNode?)Stage(stageName, "succeeded"))
                    .ToArray())
            };
        }

        public ProcessResult Generate(JsonObject input)
        {
            File.WriteAllText(_inputPath, input.ToJsonString());
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = _repositoryRoot
            };
            foreach (var argument in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File",
                         Path.Combine(_repositoryRoot, "scripts", "New-ProductionDeploymentReport.ps1"),
                         "-InputPath", _inputPath,
                         "-OutputPath", ReportPath
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
                throw new TimeoutException("Production deployment report generation exceeded 30 seconds.");
            }

            var output = stdout.GetAwaiter().GetResult();
            var error = stderr.GetAwaiter().GetResult();
            var report = File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : string.Empty;
            return new ProcessResult(process.ExitCode, output, error, report);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error, string Report);
}
