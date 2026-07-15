using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;

namespace Wedge.Server.Services;

[Injectable(InjectionType.Singleton)]
public class ConfigService
{
    public WedgeConfig Config { get; }

    public ConfigService(ModHelper modHelper, ISptLogger<ConfigService> logger)
    {
        WedgeConfig? config = null;
        try
        {
            var modDir = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            config = modHelper.GetJsonDataFromFile<WedgeConfig>(modDir, "config.jsonc");
        }
        catch (Exception ex)
        {
            logger.Error($"[Wedge] failed to load config.jsonc, using defaults: {ex}");
        }

        Config = config ?? new WedgeConfig();
    }
}
