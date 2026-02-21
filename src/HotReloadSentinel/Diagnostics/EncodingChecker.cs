namespace HotReloadSentinel.Diagnostics;

/// <summary>
/// Checks .cs files for UTF-8 BOM encoding (required for C# Hot Reload).
/// </summary>
public static class EncodingChecker
{
    static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static DiagnosticCheck Run(string projectDir)
    {
        if (!Directory.Exists(projectDir))
        {
            return new DiagnosticCheck
            {
                Id = "bom_encoding",
                Name = "UTF-8 BOM Encoding",
                Status = CheckStatus.Warn,
                Message = $"Project directory not found: {projectDir}",
                AutoFixable = false,
            };
        }

        var csFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        var missingBom = new List<string>();
        foreach (var file in csFiles)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                var header = new byte[3];
                var read = fs.Read(header, 0, 3);
                if (read < 3 || !header.SequenceEqual(Bom))
                    missingBom.Add(Path.GetRelativePath(projectDir, file));
            }
            catch (IOException)
            {
                // Skip unreadable files
            }
        }

        if (missingBom.Count == 0)
        {
            return new DiagnosticCheck
            {
                Id = "bom_encoding",
                Name = "UTF-8 BOM Encoding",
                Status = CheckStatus.Pass,
                Message = $"All {csFiles.Count} .cs files have UTF-8 BOM.",
                AutoFixable = false,
            };
        }

        return new DiagnosticCheck
        {
            Id = "bom_encoding",
            Name = "UTF-8 BOM Encoding",
            Status = CheckStatus.Warn,
            Message = $"{missingBom.Count} of {csFiles.Count} .cs files missing UTF-8 BOM. Hot Reload may silently fail for these files.",
            AutoFixable = true,
            FixCommand = "hotreload-sentinel fix --check bom_encoding",
            AffectedFiles = missingBom.Take(20).ToList(),
        };
    }

    /// <summary>
    /// Add UTF-8 BOM to files that are missing it.
    /// </summary>
    public static int FixBom(string projectDir)
    {
        var fixed_count = 0;
        var csFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        foreach (var file in csFiles)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                var header = new byte[3];
                var read = fs.Read(header, 0, 3);
                if (read >= 3 && header.SequenceEqual(Bom))
                    continue;

                // Need to add BOM
                fs.Position = 0;
                var content = new byte[fs.Length];
                _ = fs.Read(content, 0, content.Length);
                fs.Close();

                using var writer = new FileStream(file, FileMode.Create, FileAccess.Write);
                writer.Write(Bom);
                writer.Write(content);
                fixed_count++;
            }
            catch (IOException)
            {
                // Skip
            }
        }

        return fixed_count;
    }
}
