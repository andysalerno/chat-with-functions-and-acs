using Microsoft.Extensions.Configuration;

internal class Config
{
    public static string GetConfigurationValue(string configName)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        return config[configName] ?? throw new InvalidOperationException($"Config not found: {configName}");
    }
}
