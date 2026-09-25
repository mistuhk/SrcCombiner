
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MiEditor.Infrastructure.API;
using System.Reflection;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Core.Services;
using WG.MiEditor.Core.Services.Drawing.Editing;
using WG.MiEditor.Core.Services.Drawing.Editing.AddFeatures.Manager;
using WG.MiEditor.Core.Services.Drawing.Editing.History;
using WG.MiEditor.Core.Services.Drawing.Editing.Managers;
using WG.MiEditor.Core.Services.Drawing.Editing.MergeFeature.Operation;
using WG.MiEditor.Core.Services.Drawing.Measurement.Manager;
using WG.MiEditor.Core.Services.FeatureGuide;
using WG.MiEditor.Core.Services.MapData;
using WG.MiEditor.Helpers.Abstraction;
using WG.MiEditor.Logging.Abstractions;
using WG.MiEditor.MainApp.ViewModels.Aoi;
using WG.MiEditor.MainApp.ViewModels.Measurement;
using WG.MiEditor.MainApp.ViewModels.Toolbar;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Activation;
using WG.MiEditor.MainApp.ViewModels.Toolbar.Descriptors;
using WG.MiEditor.Models.Drawing;
using WG.MiEditor.Shared.AutoScanning;
using WG.MiEditor.Shared.Configuration.AppSettings;
using WG.MiEditor.Shared.Models.FeatureGuide;
using WG.MiEditor.Shared.Models.Notifications;

namespace WG.MiEditor.MainApp;

public static class AutoScanning
{
    public static IServiceCollection AddServicesByConvention(this IServiceCollection services)
    {
        // Define all the assemblies to scan for services
        var assemblies = new[]
        {
            typeof(MauiProgram).Assembly,
            typeof(IAddFeatureOperationManager).Assembly,
            typeof(ICaseManagementDataProvider).Assembly,
            typeof(ILoggerService).Assembly,
            typeof(IMapHelper).Assembly
        };

        var serviceTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract && type.GetCustomAttribute<RegisterServiceAttribute>() != null)
            .Select(t => new
            {
                ImplementationType = t,
                Attribute = t.GetCustomAttribute<RegisterServiceAttribute>()!
            });

        foreach (var service in serviceTypes)
        {
            var registrationTypes = new List<Type>();
            if (service.Attribute.As != null && service.Attribute.As.Length > 0)
            {
                registrationTypes.AddRange(service.Attribute.As);
            }
            else
            {
                var conventionalInterface = service.ImplementationType.GetInterfaces()
                    .FirstOrDefault(type => type.Name == $"I{service.ImplementationType.Name}");

                if (conventionalInterface != null)
                {
                    registrationTypes.Add(conventionalInterface);
                }
                else
                {
                    registrationTypes.Add(service.ImplementationType);
                }
            }

            foreach (var serviceType in registrationTypes)
            {
                switch (service.Attribute.Lifetime)
                {
                    case ServiceLifetime.Singleton:
                        services.AddSingleton(serviceType, service.ImplementationType);
                        break;

                    case ServiceLifetime.Scoped:
                        services.AddScoped(serviceType, service.ImplementationType);
                        break;

                    case ServiceLifetime.Transient:
                        services.AddTransient(serviceType, service.ImplementationType);
                        break;
                }
            }
        }

        return services;
    }

    public static IServiceCollection AddToolbars(this IServiceCollection services)
    {
        services.AddSingleton(sp => new MeasurementToolGroupViewModel(
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<IToolActivationCoordinator>(),
            sp.GetRequiredService<IToolDescriptorRegistry>(),
            sp.GetRequiredService<IMeasureOperationManager>()));

        services.AddSingleton(sp => new ToolbarControlViewModel(
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<IToolActivationCoordinator>(),
            sp.GetRequiredService<IToolDescriptorRegistry>(),
            sp.GetRequiredService<IEditHistoryService>()));

        services.AddSingleton(sp => new EditingLayerToolbarViewModel(
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<IMergeFeatureService>(),
            sp.GetRequiredService<IEditingOperationsManager>(),
            sp.GetRequiredService<IFeatureLayerService>(),
            sp.GetRequiredService<IEditingUtility>(),
            sp.GetRequiredService<IFeatureGuideService>(),
            sp.GetRequiredService<IMapDataService>(),
            sp.GetRequiredService<IToolActivationCoordinator>(),
            sp.GetRequiredService<IToolDescriptorRegistry>(),
            sp.GetRequiredService<ApplicationSettings>()));

        services.AddSingleton(sp => new AoiToolbarControlViewModel(
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<IToolActivationCoordinator>(),
            sp.GetRequiredService<IToolDescriptorRegistry>()));

        return services;
    }

    public static void AddConfiguration(this MauiAppBuilder builder)
    {
#if DEBUG
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Dev.json");
#else
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
#endif

        using var appSettingsStream = (Stream)File.OpenRead(path);
        using var drawingSymbolsStream = FileSystem.OpenAppPackageFileAsync("Resources/Configs/drawingsymbols.json").GetAwaiter().GetResult();
        using var featureGuidesStream = FileSystem.OpenAppPackageFileAsync("Resources/Configs/featureguides.json").GetAwaiter().GetResult();

        var config = new ConfigurationBuilder()
        .AddJsonStream(appSettingsStream!)
        .AddJsonStream(drawingSymbolsStream!)
        .AddJsonStream(featureGuidesStream)
        .Build();

        builder.Configuration.AddConfiguration(config);

        var featureGuideSettings = config.GetSection("FeatureGuidesSettings")
            .Get<FeatureGuidesSettings>() ?? new FeatureGuidesSettings();

        foreach (var configuration in featureGuideSettings.Guides)
        {
            var featureGuide = new JsonFeatureGuide(configuration);
            builder.Services.AddSingleton<IFeatureGuide>(featureGuide);
        }

        var appSettings = config.GetSection(nameof(ApplicationSettings)).Get<ApplicationSettings>()!;
        builder.Services.AddSingleton(appSettings);

        builder.Services.Configure<InfieldLayerSettings>(
            options => builder.Configuration.GetSection($"{nameof(ApplicationSettings)}:InfieldLayers").Bind(options));

        builder.Services.Configure<NotificationSettings>(
            options => builder.Configuration.GetSection($"{nameof(ApplicationSettings)}:NotificationSettings").Bind(options));

        builder.Services.Configure<InfieldLayerSettings>(
            options => builder.Configuration.GetSection($"{nameof(ApplicationSettings)}:InfieldLayers").Bind(options));

        builder.Services.Configure<SheepMovementBufferSettings>(
            options => builder.Configuration.GetSection($"{nameof(ApplicationSettings)}:{nameof(SheepMovementBufferSettings)}").Bind(options));

        builder.Services.AddKeyedSingleton(InfieldLayerType.Point, (sp, key) => sp.GetRequiredService<IOptions<InfieldLayerSettings>>().Value.Point);
        builder.Services.AddKeyedSingleton(InfieldLayerType.Line, (sp, key) => sp.GetRequiredService<IOptions<InfieldLayerSettings>>().Value.Line);
        builder.Services.AddKeyedSingleton(InfieldLayerType.Poly, (sp, key) => sp.GetRequiredService<IOptions<InfieldLayerSettings>>().Value.Poly);

        builder.Services.Configure<DrawingSymbolsSettings>(
            options => builder.Configuration.GetSection("DrawingSymbols").Bind(options));
    }
}
