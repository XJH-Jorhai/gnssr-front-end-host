using System.Text;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Core.Services;

public sealed class FileNamingPolicy
{
    public string SanitizeFileNamePrefix(string? fileNamePrefix)
    {
        var fallback = string.IsNullOrWhiteSpace(fileNamePrefix) ? "capture" : fileNamePrefix.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder();

        foreach (var character in fallback)
        {
            if (invalidCharacters.Contains(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                builder.Append('_');
                continue;
            }

            builder.Append(character);
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "capture" : sanitized;
    }

    public string BuildBaseFileName(string? fileNamePrefix, DateTimeOffset timestamp)
    {
        var prefixToken = SanitizeFileNamePrefix(fileNamePrefix);
        return $"{prefixToken}[{timestamp:yyyy.MM.dd HH.mm.ss}]";
    }

    public CaptureFilePaths CreateSessionPaths(string? fileNamePrefix, string outputDirectory, DateTimeOffset? timestamp = null)
    {
        Directory.CreateDirectory(outputDirectory);

        var captureTime = timestamp ?? DateTimeOffset.Now;
        var baseFileName = BuildBaseFileName(fileNamePrefix, captureTime);
        var candidate = baseFileName;
        var sequence = 1;

        while (File.Exists(Path.Combine(outputDirectory, $"{candidate}.bin")) ||
               File.Exists(Path.Combine(outputDirectory, $"{candidate}.json")))
        {
            candidate = $"{baseFileName}_{sequence:00}";
            sequence++;
        }

        return new CaptureFilePaths(
            candidate,
            Path.Combine(outputDirectory, $"{candidate}.bin"),
            Path.Combine(outputDirectory, $"{candidate}.json"));
    }
}
