using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public static class SourceCombiner
{
    // -----------------------------------------------------------------------
    // Supported source code file extensions
    // -----------------------------------------------------------------------
    private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Web
        ".js", ".jsx", ".ts", ".tsx", ".html", ".htm", ".css", ".scss", ".sass", ".less",
        // Frameworks / templates
        ".vue", ".svelte", ".astro", ".razor", ".cshtml",
        // Backend languages
        ".cs", ".fs", ".vb",                          // .NET
        ".py", ".pyw",                                // Python
        ".java", ".kt", ".kts",                       // JVM
        ".rb", ".rake",                               // Ruby
        ".php",                                       // PHP
        ".go",                                        // Go
        ".rs",                                        // Rust
        ".swift",                                     // Swift
        ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp",   // C / C++
        ".m", ".mm",                                  // Objective-C
        ".scala",                                     // Scala
        ".dart",                                      // Dart / Flutter
        ".lua",                                       // Lua
        ".r",                                         // R
        ".ex", ".exs",                                // Elixir
        ".erl",                                       // Erlang
        ".hs",                                        // Haskell
        ".clj", ".cljs",                              // Clojure
        // Config / data / markup
        ".json", ".jsonc", ".json5",
        ".xml", ".xsd", ".xslt",
        ".yaml", ".yml",
        ".toml", ".ini", ".env",
        ".graphql", ".gql",
        ".proto",
        ".sql",".csv",
        // Shell / scripting
        ".sh", ".bash", ".zsh", ".fish",
        ".ps1", ".psm1", ".psd1",
        ".bat", ".cmd",
        // Docs / markdown
        ".md", ".mdx", ".rst", ".txt",
        // Build / project files
        ".csproj", ".fsproj", ".vbproj", ".sln",
        ".gradle", ".maven",
        ".dockerfile",                                // Dockerfile (no extension handled separately)
        ".makefile",
        ".tf", ".tfvars",                             // Terraform
        ".bicep",                                     // Azure Bicep
    };

    // Files to skip even if their extension matches (common noise)
    private static readonly HashSet<string> ExcludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", ".DS_Store", "Thumbs.db"
    };

    // Directories to skip entirely
    private static readonly HashSet<string> ExcludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", "bin", "obj",
        ".next", ".nuxt", "dist", "build", "out", ".cache",
        ".idea", ".vscode", "__pycache__", ".pytest_cache",
        "coverage", ".nyc_output", "vendor"
    };

    private const string Separator = "================================================================================";

    // -----------------------------------------------------------------------
    /// <summary>
    /// Recursively reads all source code files in <paramref name="sourceDirectory"/>,
    /// combines them into a single text file at <paramref name="outputFilePath"/>,
    /// and returns a summary of how many files were processed.
    /// </summary>
    /// <param name="sourceDirectory">Root directory to scan.</param>
    /// <param name="outputFilePath">Path of the combined output .txt file.</param>
    /// <param name="useRelativePaths">
    ///     If true, file paths shown in the output are relative to <paramref name="sourceDirectory"/>.
    ///     If false, full absolute paths are used.
    /// </param>
    /// <returns>A <see cref="CombineResult"/> with counts and any skipped files.</returns>
    // -----------------------------------------------------------------------
    public static CombineResult CombineSourceFiles(
        string sourceDirectory,
        string outputFilePath,
        bool useRelativePaths = true)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");

        string rootPath = Path.GetFullPath(sourceDirectory);
        var result      = new CombineResult();
        var sb          = new StringBuilder();

        // Header block
        sb.AppendLine(Separator);
        sb.AppendLine($"// COMBINED SOURCE FILE");
        sb.AppendLine($"// Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"// Source    : {rootPath}");
        sb.AppendLine(Separator);
        sb.AppendLine();

        IEnumerable<string> allFiles = GetSourceFiles(rootPath);

        foreach (string filePath in allFiles)
        {
            string displayPath = useRelativePaths
                ? Path.GetRelativePath(rootPath, filePath)
                : filePath;

            try
            {
                string code = File.ReadAllText(filePath, Encoding.UTF8);

                sb.AppendLine(Separator);
                sb.AppendLine($"// File Path = '{displayPath}'");
                sb.AppendLine(Separator);
                sb.AppendLine();
                sb.AppendLine(code);
                sb.AppendLine();

                result.ProcessedFiles.Add(displayPath);
            }
            catch (Exception ex)
            {
                result.SkippedFiles.Add((displayPath, ex.Message));
            }
        }

        // Write everything to the output file (UTF-8 with BOM for broad editor compatibility)
        File.WriteAllText(outputFilePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        result.OutputPath       = outputFilePath;
        result.OutputSizeBytes  = new FileInfo(outputFilePath).Length;

        return result;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Walks the directory tree, skipping excluded folders and returning only
    /// files whose extension (or name) is in the supported set.
    /// </summary>
    private static IEnumerable<string> GetSourceFiles(string rootPath)
    {
        return WalkDirectory(rootPath)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> WalkDirectory(string directory)
    {
        // Yield matching files in the current directory
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string fileName  = Path.GetFileName(file);
            string extension = Path.GetExtension(file);

            if (ExcludedFileNames.Contains(fileName))
                continue;

            // Support extension-less files like "Dockerfile" or "Makefile"
            bool isExtensionlessSourceFile =
                string.IsNullOrEmpty(extension) &&
                (fileName.Equals("Dockerfile",  StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("Makefile",     StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("Jenkinsfile",  StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("Procfile",     StringComparison.OrdinalIgnoreCase) ||
                 fileName.StartsWith(".",        StringComparison.OrdinalIgnoreCase)); // .gitignore, .env etc.

            if (SupportedExtensions.Contains(extension) || isExtensionlessSourceFile)
                yield return file;
        }

        // Recurse into subdirectories, skipping excluded ones
        foreach (string subDir in Directory.EnumerateDirectories(directory))
        {
            string dirName = Path.GetFileName(subDir);
            if (ExcludedDirectories.Contains(dirName))
                continue;

            foreach (string file in WalkDirectory(subDir))
                yield return file;
        }
    }
}

// -----------------------------------------------------------------------
/// <summary>Summary returned after a combine operation.</summary>
// -----------------------------------------------------------------------
public class CombineResult
{
    public string       OutputPath      { get; set; } = string.Empty;
    public long         OutputSizeBytes { get; set; }
    public List<string> ProcessedFiles  { get; set; } = new List<string>();
    public List<(string File, string Reason)> SkippedFiles { get; set; } = new List<(string, string)>();

    public void PrintSummary()
    {
        Console.WriteLine($"\n=== Combine Complete ===");
        Console.WriteLine($"Output file  : {OutputPath}");
        Console.WriteLine($"Output size  : {OutputSizeBytes / 1024.0:F1} KB");
        Console.WriteLine($"Files merged : {ProcessedFiles.Count}");

        if (SkippedFiles.Count > 0)
        {
            Console.WriteLine($"Files skipped: {SkippedFiles.Count}");
            foreach (var (file, reason) in SkippedFiles)
                Console.WriteLine($"  [SKIPPED] {file} — {reason}");
        }

        Console.WriteLine("\nMerged files:");
        foreach (string f in ProcessedFiles)
            Console.WriteLine($"  + {f}");
    }
}