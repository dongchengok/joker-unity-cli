using Joker.UnityCli.Commands;
using Joker.UnityCli.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Joker.UnityCli;

class Program
{
    static int Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProjectDetector, ProjectDetector>();
        services.AddSingleton<IUnityLocator, UnityLocator>();
        services.AddSingleton<IAssetService, AssetService>();
        services.AddSingleton<IBuildService, BuildService>();
        services.AddSingleton<IExecService, ExecService>();
        services.AddSingleton<ILogService, LogService>();

        var registrar = new DependencyInjectionTypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("joker-unity");
            config.AddCommand<InfoCommand>("info")
                .WithDescription("Display Unity project information");
            config.AddCommand<BuildCommand>("build")
                .WithDescription("Build the Unity project");
            config.AddCommand<AssetsCommand>("assets")
                .WithDescription("List or search project assets");
            config.AddCommand<ExecCommand>("exec")
                .WithDescription("Execute C# code in Unity Editor");
            config.AddCommand<LogsCommand>("logs")
                .WithDescription("Show Unity Editor log entries");
        });

        return app.Run(args);
    }
}
