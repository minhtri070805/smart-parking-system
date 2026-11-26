using Microsoft.Extensions.Configuration;

namespace Infrastructure.Features.PayOS.Configurations;

public static class DotEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddDotEnvFile(this IConfigurationBuilder configuration, string path)
    {
        if (!File.Exists(path)) return configuration;

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var environmentKey = line[..separator].Trim();
            if (environmentKey.Length == 0 || Environment.GetEnvironmentVariable(environmentKey) is not null) continue;

            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            if (value.Length == 0) continue;

            values[environmentKey.Replace("__", ":", StringComparison.Ordinal)] = value;
        }

        configuration.AddInMemoryCollection(values);
        return configuration;
    }
}
