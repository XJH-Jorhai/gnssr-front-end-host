using System.Text;
using GNSSR.Host.Core.Models;

namespace GNSSR.Host.Core.Services;

public sealed class FileNamingPolicy
{
    public string SanitizeOperatorName(string? operatorName)
    {
        var fallback = string.IsNullOrWhiteSpace(operatorName) ? "Operator" : operatorName.Trim();
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
        return string.IsNullOrWhiteSpace(sanitized) ? "Operator" : sanitized;
    }

    public CaptureFilePaths CreateSessionPaths(string? operatorName, string outputDirectory, DateTimeOffset? timestamp = null)
    {
        Directory.CreateDirectory(outputDirectory);

        var operatorToken = SanitizeOperatorName(operatorName);
        var captureTime = timestamp ?? DateTimeOffset.Now;
        var baseFileName = $"{operatorToken}_{captureTime:yyyyMMdd_HHmmss}";
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
