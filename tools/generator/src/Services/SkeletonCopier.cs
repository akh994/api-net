using System.Text.RegularExpressions;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class SkeletonCopier
{
    private readonly string _skeletonPath;

    public SkeletonCopier(string skeletonPath)
    {
        _skeletonPath = skeletonPath;
    }

    public async Task CopyAndRenameAsync(string projectName, string outputDir, bool dryRun)
    {
        var targetDir = Path.Combine(outputDir, projectName);

        if (Directory.Exists(targetDir) && !dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: {targetDir} already exists. Overwriting...[/]");
            Directory.Delete(targetDir, true);
        }

        // Get source directories - exclude obj, bin, .git
        var srcDir = Path.Combine(_skeletonPath, "src");
        var excludeDirs = new[] { "obj", "bin", ".git", "tools" };

        // Copy and rename files
        await CopyDirectoryAsync(srcDir, Path.Combine(targetDir, "src"), projectName, excludeDirs, dryRun);

        // Copy other essential files
        await CopyAndRenameFileAsync(
            Path.Combine(_skeletonPath, "Makefile"),
            Path.Combine(targetDir, "Makefile"),
            projectName, dryRun);

        await CopyAndRenameFileAsync(
            Path.Combine(_skeletonPath, "README.md"),
            Path.Combine(targetDir, "README.md"),
            projectName, dryRun);

        await CopyAndRenameFileAsync(
            Path.Combine(_skeletonPath, "build.sh"),
            Path.Combine(targetDir, "build.sh"),
            projectName, dryRun);

        await CopyAndRenameFileAsync(
            Path.Combine(_skeletonPath, "deploy.sh"),
            Path.Combine(targetDir, "deploy.sh"),
            projectName, dryRun);

        await CopyAndRenameFileAsync(
            Path.Combine(_skeletonPath, "Jenkinsfile"),
            Path.Combine(targetDir, "Jenkinsfile"),
            projectName, dryRun);

        // Copy deployments folder
        if (Directory.Exists(Path.Combine(_skeletonPath, "deployments")))
        {
            await CopyDirectoryAsync(
                Path.Combine(_skeletonPath, "deployments"),
                Path.Combine(targetDir, "deployments"),
                projectName, excludeDirs, dryRun);
        }

        // Copy migrations folder
        if (Directory.Exists(Path.Combine(_skeletonPath, "migrations")))
        {
            await CopyDirectoryAsync(
                Path.Combine(_skeletonPath, "migrations"),
                Path.Combine(targetDir, "migrations"),
                projectName, excludeDirs, dryRun);
        }

        // Create solution file
        if (!dryRun)
        {
            await CreateSolutionFileAsync(targetDir, projectName);
        }
    }

    private async Task CopyDirectoryAsync(string sourceDir, string targetDir, string projectName, string[] excludeDirs, bool dryRun)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);

            // Skip excluded directories
            if (excludeDirs.Contains(dirName))
            {
                continue;
            }

            // Rename directory from SkeletonApi to ProjectName
            var newDirName = RenameIdentifier(dirName, projectName);
            var newTargetDir = Path.Combine(targetDir, newDirName);

            if (!dryRun && !Directory.Exists(newTargetDir))
            {
                Directory.CreateDirectory(newTargetDir);
            }

            // Recurse
            await CopyDirectoryAsync(dir, newTargetDir, projectName, excludeDirs, dryRun);
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var newFileName = RenameIdentifier(fileName, projectName);
            var targetFile = Path.Combine(targetDir, newFileName);

            await CopyAndRenameFileAsync(file, targetFile, projectName, dryRun);
        }
    }

    private async Task CopyAndRenameFileAsync(string sourceFile, string targetFile, string projectName, bool dryRun)
    {
        if (!File.Exists(sourceFile))
        {
            return;
        }

        var extension = Path.GetExtension(sourceFile).ToLower();
        var textExtensions = new[] { ".cs", ".csproj", ".json", ".proto", ".md", ".xml", ".txt", ".sql", ".sh", ".yaml", ".yml", ".props" };

        if (dryRun)
        {
            AnsiConsole.MarkupLine($"[yellow]Would create: {targetFile}[/]");
            return;
        }

        // Ensure directory exists
        var dir = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (textExtensions.Contains(extension) || Path.GetFileName(sourceFile) == "Makefile")
        {
            // Text file - read, replace, write
            var content = await File.ReadAllTextAsync(sourceFile);
            var newContent = ReplaceNamespaces(content, projectName);
            await File.WriteAllTextAsync(targetFile, newContent);
        }
        else
        {
            // Binary file - copy as-is
            File.Copy(sourceFile, targetFile, true);
        }

        AnsiConsole.MarkupLine($"[green]Created: {targetFile}[/]");
    }

    private string RenameIdentifier(string name, string projectName)
    {
        // Replace SkeletonApi with ProjectName
        return name
            .Replace("SkeletonApi", projectName)
            .Replace("skeleton-api-net", projectName.ToLower());
    }

    private string ReplaceNamespaces(string content, string projectName)
    {
        // Replace all occurrences of SkeletonApi with ProjectName
        return content
            .Replace("SkeletonApi", projectName)
            .Replace("skeleton-api-net", projectName.ToLower())
            .Replace("skeleton_api_net", projectName.ToLower().Replace("-", "_"));
    }

    private async Task CreateSolutionFileAsync(string projectDir, string projectName)
    {
        var slnPath = Path.Combine(projectDir, $"{projectName}.sln");

        // Use dotnet CLI to create solution and add projects
        var srcDir = Path.Combine(projectDir, "src");

        if (Directory.Exists(srcDir))
        {
            var csprojFiles = Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories);

            // Detect .NET SDK version — use --format sln for .NET 9+ to avoid .slnx format
            var sdkVersion = await GetDotNetSdkVersionAsync();
            var formatFlag = sdkVersion >= new Version(9, 0) ? " --format sln" : "";

            AnsiConsole.MarkupLine($"[blue]Debug: Detected .NET SDK {sdkVersion}, using format flag: '{formatFlag}'[/]");
            AnsiConsole.MarkupLine($"[blue]Debug: Running: dotnet new sln -n \"{projectName}\"{formatFlag}[/]");

            // Create solution
            var createSln = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"new sln -n \"{projectName}\"{formatFlag}",
                    WorkingDirectory = projectDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            createSln.Start();
            await createSln.WaitForExitAsync();

            // Add projects
            foreach (var csproj in csprojFiles)
            {
                var relativePath = Path.GetRelativePath(projectDir, csproj);
                var addProj = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"sln add \"{relativePath}\"",
                        WorkingDirectory = projectDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                addProj.Start();
                await addProj.WaitForExitAsync();
            }

            AnsiConsole.MarkupLine($"[green]Created solution: {slnPath}[/]");
        }
    }

    private async Task<Version> GetDotNetSdkVersionAsync()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process != null)
        {
            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            // Handle versions like "10.0.103" or "10.0.0-preview.7"
            var versionPart = output.Split('-')[0];
            if (Version.TryParse(versionPart, out var version))
            {
                return version;
            }
        }

        return new Version(8, 0); // safe fallback
    }
}
