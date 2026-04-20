using System.Text.RegularExpressions;
using Spectre.Console;

namespace SkeletonApi.Generator.Services;

public class CodeInjector
{
    private readonly bool _dryRun;
    public string PkgType { get; set; } = "local";
    public string CommonPkgName { get; set; } = "SkeletonApi.Common";
    public string CommonPkgVersion { get; set; } = "*";

    public CodeInjector(bool dryRun)
    {
        _dryRun = dryRun;
    }

    /// <summary>
    /// Auto-detects if a project uses remote common pkg and updates internal settings.
    /// </summary>
    public void AutoDetectRemoteConfig(string projectRoot)
    {
        var srcPath = Path.Combine(projectRoot, "src");
        if (!Directory.Exists(srcPath)) return;

        // 1. If Common project folder doesn't exist, it's likely remote
        var hasLocalCommon = Directory.GetDirectories(srcPath).Any(d => d.EndsWith(".Common"));

        if (!hasLocalCommon)
        {
            PkgType = "remote";

            // 2. Try to find the package name and version from any csproj
            var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
            foreach (var file in csprojFiles)
            {
                var content = File.ReadAllText(file);
                // Look for PackageReference with typical common names or those matching SkeletonApi Pattern
                var match = Regex.Match(content, @"<PackageReference\s+Include=""(.*?Common.*?)""\s+Version=""(.*?)""\s*/>");
                if (match.Success)
                {
                    CommonPkgName = match.Groups[1].Value;
                    CommonPkgVersion = match.Groups[2].Value;
                    break;
                }
            }
            AnsiConsole.MarkupLine($"[cyan]Auto-detected Remote Common: {CommonPkgName} v{CommonPkgVersion}[/]");
        }
    }

    /// <summary>
    /// Replaces placeholders based on remote pkg settings.
    /// </summary>
    public string ReplacePlaceholders(string content, string projectName)
    {
        var isRemote = PkgType.Equals("remote", StringComparison.OrdinalIgnoreCase);
        var result = content;

        if (isRemote)
        {
            // Replace local ProjectReference with PackageReference
            var projectRefPattern = @"<ProjectReference\s+Include=[""'].*SkeletonApi\.Common\.csproj[""']\s*/>";
            var packageReference = $@"<PackageReference Include=""{CommonPkgName}"" Version=""{CommonPkgVersion}"" />";
            result = Regex.Replace(result, projectRefPattern, packageReference);

            // Also handle updating existing PackageReference version if it already exists
            var packageRefPattern = $@"<PackageReference\s+Include=[""']{CommonPkgName}[""']\s+Version=[""'].*?[""']\s*/>";
            result = Regex.Replace(result, packageRefPattern, packageReference);

            // Handle namespaces: SkeletonApi.Common -> CommonPkgName
            result = result.Replace("SkeletonApi.Common", CommonPkgName);
        }

        return result;
    }

    /// <summary>
    /// Safely injects a using statement at the top of the file if not already present.
    /// </summary>
    public void InjectUsing(string filePath, string ns)
    {
        if (!File.Exists(filePath)) return;
        var content = File.ReadAllText(filePath);
        var usingStatement = $"using {ns};";
        if (content.Contains(usingStatement)) return;

        // Try to find the last using statement to insert after it
        var matches = Regex.Matches(content, @"^using\s+[\w\.]+;", RegexOptions.Multiline);
        if (matches.Count > 0)
        {
            var lastMatch = matches[matches.Count - 1];
            content = content.Insert(lastMatch.Index + lastMatch.Length, "\n" + usingStatement);
        }
        else
        {
            content = usingStatement + "\n" + content;
        }
        WriteFile(filePath, content);
    }

    /// <summary>
    /// Safely injects dependency registration code into a specific method in DependencyInjection.cs.
    /// </summary>
    public void InjectDependency(string diPath, string diCode, string targetMethod = "AddInfrastructure")
    {
        if (!File.Exists(diPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: DependencyInjection.cs not found: {diPath}[/]");
            return;
        }

        var content = File.ReadAllText(diPath);

        // Normalize the injected code (8 spaces for DI methods)
        var normalizedCode = NormalizeIndentation(diCode, 8);

        // Idempotency check
        var firstLineText = normalizedCode.Split('\n')[0].Trim();
        if (content.Contains(firstLineText)) return;

        var methodPattern = $@"public\s+static\s+IServiceCollection\s+{targetMethod}\s*\([^)]*\)\s*\{{";
        var methodMatch = Regex.Match(content, methodPattern);

        if (methodMatch.Success)
        {
            var returnPos = content.IndexOf("return services;", methodMatch.Index);
            if (returnPos > 0)
            {
                // Find start of line to avoid double-indentation
                int lineStartPos = returnPos;
                while (lineStartPos > 0 && content[lineStartPos - 1] != '\n') lineStartPos--;

                var updatedContent = content.Insert(lineStartPos, normalizedCode + "\n\n");
                WriteFile(diPath, updatedContent);
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Could not find 'return services;' in {targetMethod} in {diPath}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Could not find method {targetMethod} in {diPath}[/]");
        }
    }

    /// <summary>
    /// Safely injects properties and nested classes into AppOptions.cs.
    /// </summary>
    public void InjectAppOption(string optionsPath, string parentClassName, string propertyCode, string? classDefinitionCode = null)
    {
        if (!File.Exists(optionsPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: AppOptions.cs not found: {optionsPath}[/]");
            return;
        }

        var content = File.ReadAllText(optionsPath);

        // 1. Inject the property into the parent class (4 spaces)
        var normalizedProperty = NormalizeIndentation(propertyCode, 4);
        var firstLineOfProperty = normalizedProperty.Trim();

        if (!content.Contains(firstLineOfProperty))
        {
            var classMatch = Regex.Match(content, $@"public class {parentClassName}\s*\{{", RegexOptions.Singleline);
            if (classMatch.Success)
            {
                int openBracePos = content.IndexOf('{', classMatch.Index);
                var insertPos = openBracePos + 1;
                content = content.Insert(insertPos, "\n" + normalizedProperty + "\n");
            }
        }

        // 2. Inject the class definition (4 spaces if nested)
        if (!string.IsNullOrEmpty(classDefinitionCode))
        {
            var normalizedClass = NormalizeIndentation(classDefinitionCode, 4);
            var firstLineOfClass = normalizedClass.Trim();

            if (!content.Contains(firstLineOfClass))
            {
                var classMatch = Regex.Match(content, $@"public class {parentClassName}\s*\{{", RegexOptions.Singleline);
                if (classMatch.Success)
                {
                    int openBracePos = content.IndexOf('{', classMatch.Index);
                    int closingBracePos = FindClosingBrace(content, openBracePos);

                    if (closingBracePos > 0)
                    {
                        // Find start of line for the closing brace
                        int lineStartPos = closingBracePos;
                        while (lineStartPos > 0 && content[lineStartPos - 1] != '\n') lineStartPos--;

                        content = content.Insert(lineStartPos, "\n" + normalizedClass + "\n");
                    }
                }
            }
        }

        WriteFile(optionsPath, content);
    }

    /// <summary>
    /// Normalizes indentation of a code block.
    /// </summary>
    public string NormalizeIndentation(string code, int indentSize)
    {
        if (string.IsNullOrEmpty(code)) return code;

        // Ensure we handle different line endings by converting to \n first
        var normalizedLineEndings = code.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalizedLineEndings.Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1])) lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0) return "";

        var minIndent = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.TakeWhile(char.IsWhiteSpace).Count())
            .DefaultIfEmpty(0)
            .Min();

        var indent = new string(' ', indentSize);
        var normalizedLines = lines.Select(l =>
            string.IsNullOrWhiteSpace(l) ? "" : string.Concat(indent, l.AsSpan(minIndent)));

        return string.Join("\n", normalizedLines);
    }

    private int FindClosingBrace(string content, int openBracePos)
    {
        int count = 0;
        for (int i = openBracePos; i < content.Length; i++)
        {
            if (content[i] == '{') count++;
            else if (content[i] == '}')
            {
                count--;
                if (count == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Injects JSON configuration into all appsettings.*.json files in the project.
    /// </summary>
    public void InjectAppSettingToAllConfigs(string projectDir, string sectionName, string jsonBlock)
    {
        var settingsPaths = new[]
        {
            Path.Combine(projectDir, "appsettings.json"),
            Path.Combine(projectDir, "appsettings.Development.json"),
            Path.Combine(projectDir, "appsettings.Production.json"),
            Path.Combine(projectDir, "appsettings.Regress.json"),
            Path.Combine(projectDir, "appsettings.Staging.json")
        };

        foreach (var path in settingsPaths)
        {
            if (File.Exists(path))
            {
                InjectAppSetting(path, sectionName, jsonBlock);
            }
        }
    }

    /// <summary>
    /// Safely injects JSON configuration into appsettings.json.
    /// Supports nested paths like "MessageBroker:RabbitMQ:Topics".
    /// </summary>
    public void InjectAppSetting(string settingsPath, string sectionName, string jsonBlock)
    {
        if (!File.Exists(settingsPath)) return;

        var content = File.ReadAllText(settingsPath);

        // Idempotency check: look for the first key in the jsonBlock
        var matchKey = Regex.Match(jsonBlock, @"""([^""]+)""\s*:");
        if (matchKey.Success && content.Contains($"\"{matchKey.Groups[1].Value}\""))
        {
            return;
        }

        // Handle nested paths
        var parts = sectionName.Split(':');
        int currentPos = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            var pattern = $@"""{part}""\s*:\s*\{{";
            var match = Regex.Match(content.Substring(currentPos), pattern);

            if (match.Success)
            {
                currentPos += match.Index + match.Length;
            }
            else
            {
                // Section is missing. Create it.
                string newSection;
                int insertionPoint;

                if (i == 0)
                {
                    // Root level: insert before the last '}'
                    insertionPoint = content.LastIndexOf('}');
                    if (insertionPoint < 0) return; // Malformed JSON

                    var prevContent = content.Substring(0, insertionPoint).TrimEnd();
                    var needsComma = prevContent.Length > 0 && prevContent.Last() != '{' && prevContent.Last() != ',';
                    var comma = needsComma ? "," : "";
                    newSection = $"{comma}\n  \"{part}\": {{\n  }}\n";
                }
                else
                {
                    // Nested level: we are at currentPos (just after a '{')
                    insertionPoint = currentPos;
                    var afterBrace = content.Substring(currentPos).TrimStart();
                    var needsComma = afterBrace.Length > 0 && afterBrace.First() != '}';
                    var comma = needsComma ? "," : "";
                    newSection = $"\n  \"{part}\": {{\n  }}{comma}";
                }

                content = content.Insert(insertionPoint, newSection);

                // Re-find the pattern in the updated content starting from currentPos
                match = Regex.Match(content.Substring(currentPos), pattern);
                if (match.Success)
                {
                    currentPos += match.Index + match.Length;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Could not create JSON section '{part}' in {settingsPath}[/]");
                    return;
                }
            }
        }

        // Insert at the current position (just after the last nested section's '{')
        var updatedContent = content.Insert(currentPos, jsonBlock);

        // Final cleanup for valid JSON (Brute force but reliable for this generator)
        updatedContent = updatedContent.Replace(",\n  },}", "\n  }\n}");
        updatedContent = updatedContent.Replace(",}", "}");
        updatedContent = updatedContent.Replace("},}", "}\n}");
        updatedContent = updatedContent.Replace("}}", "}\n}");

        WriteFile(settingsPath, updatedContent);
    }

    /// <summary>
    /// Safely injects a method into an interface definition.
    /// </summary>
    public void InjectInterfaceMethod(string filePath, string methodCode)
    {
        InjectToTypeInternal(filePath, methodCode);
    }

    /// <summary>
    /// Safely injects a method into a class definition.
    /// </summary>
    public void InjectClassMethod(string filePath, string methodCode)
    {
        InjectToTypeInternal(filePath, methodCode);
    }

    private void InjectToTypeInternal(string filePath, string methodCode)
    {
        if (!File.Exists(filePath)) return;
        var content = File.ReadAllText(filePath);

        // Idempotency check: look for method signature
        var methodSignature = methodCode.Split('(')[0].Trim();
        if (content.Contains(methodSignature)) return;

        var normalizedCode = NormalizeIndentation(methodCode, 4);

        // Find the last closing brace and insert before it
        int lastBrace = content.LastIndexOf('}');
        if (lastBrace > 0)
        {
            content = content.Insert(lastBrace, normalizedCode + "\n");
            WriteFile(filePath, content);
        }
    }

    public string DetectProjectName(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (!Directory.Exists(fullPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: Output path does not exist: {fullPath}[/]");
            return "";
        }

        var slnFiles = Directory.GetFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Any())
        {
            return Path.GetFileNameWithoutExtension(slnFiles.First());
        }

        var srcPath = Path.Combine(fullPath, "src");
        if (Directory.Exists(srcPath))
        {
            var projectDirs = Directory.GetDirectories(srcPath)
                .Select(Path.GetFileName)
                .Where(name => name != null &&
                               !name.Contains(".Common") && !name.Contains(".Domain") &&
                               !name.Contains(".Application") && !name.Contains(".Infrastructure") &&
                               !name.Contains(".Contracts") && !name.Contains(".Tests"))
                .ToList();

            if (projectDirs.Any())
            {
                return projectDirs.First()!;
            }
        }

        return "";
    }

    private void WriteFile(string path, string content)
    {
        if (_dryRun)
        {
            AnsiConsole.MarkupLine($"[dim]Would write (via CodeInjector): {path}[/]");
            return;
        }

        File.WriteAllText(path, content);
    }
}
