
using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.Messaging;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Maui;
using Esri.ArcGISRuntime.Toolkit.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
using WG.MiEditor.Configuration.AppSettings;
using WG.MiEditor.Helpers;
using WG.MiEditor.MainApp.ViewModels.Notifications;
using WG.MiEditor.Shared.Helpers.Providers;

namespace WG.MiEditor.MainApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit(options =>
            {
                options.SetShouldEnableSnackbarOnWindows(true);
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("calcite-ui-icons-24.ttf", "CalciteIcons");
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
                fonts.AddFont("fa-solid-900.ttf", "FontAwesome");
            })
            .UseArcGISRuntime()
            .UseArcGISToolkit();

        DatePickerHandler.Mapper.AppendToMapping("FixDatePickerWidth", (handler, view) =>
        {
            handler.PlatformView.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            handler.PlatformView.MaxWidth = double.PositiveInfinity;

            handler.PlatformView.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
        });

        //Adding all DI Services to the service container.
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);        
        builder.Services.AddSingleton(Preferences.Default);        

        builder.Services.AddTransient<Func<ToastControlViewModel>>(
            sp => () => sp.GetRequiredService<ToastControlViewModel>());

        builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationSettings>().Layers);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationSettings>().OfflineDataSettings);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationSettings>().Logging);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<ApplicationSettings>().EditHistory);

        builder.Services.AddOptions<ApplicationSettings>();
        builder.Services.AddServicesByConvention();
        builder.Services.AddToolbars();
        builder.AddConfiguration();


#if WINDOWS
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windowsLifecycleBuilder =>
            {
                windowsLifecycleBuilder.OnWindowCreated(window =>
                {
                    window.ExtendsContentIntoTitleBar = false;

                    var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);

                    switch (appWindow.Presenter)
                    {
                        case Microsoft.UI.Windowing.OverlappedPresenter overlappedPresenter:
                            overlappedPresenter.SetBorderAndTitleBar(true, true);
                            overlappedPresenter.Maximize();
                            break;
                    }

                    var app = builder.Build();
                    var router = app.Services.GetRequiredService<IKeyShortcutRouter>();

                    //inject into hook
                    Win32KeyboardHook.SetRouter(router);
                    Win32KeyboardHook.Initialize(handle);
                    InputFocusTracker.Initialize(window);
                    
                    window.Closed += (_, __) => Win32KeyboardHook.Shutdown();
                });
            });
        });

        SwitchHandler.Mapper.AppendToMapping("RemoveText", (handler, view) =>
        {
            var nativeToggleSwitch = handler.PlatformView;
            nativeToggleSwitch.OnContent = null;
            nativeToggleSwitch.OffContent = null;
        });

        SwitchHandler.Mapper.AppendToMapping("ThemeOffStroke", (handler, view) =>
        {
            var toggleSwitch = handler.PlatformView;

            void ApplyOffStroke()
            {
                var dark = toggleSwitch.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
                var stroke = dark
                    ? Windows.UI.Color.FromArgb(0x80, 0xff, 0xff, 0xff)
                    : Windows.UI.Color.FromArgb(0xff, 0x5a, 0x5a, 0x5a);

                var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(stroke);
                toggleSwitch.Resources["ToggleSwitchStrokeOff"] = brush;
                toggleSwitch.Resources["ToggleSwitchStrokeOffPointerOver"] = brush;
                toggleSwitch.Resources["ToggleSwitchStrokeOffPressed"] = brush;

                Microsoft.UI.Xaml.VisualStateManager.GoToState(toggleSwitch, toggleSwitch.IsOn ? "On" : "Off", false);
            }

            ApplyOffStroke();
            toggleSwitch.ActualThemeChanged += (frameworkElement, _) => ApplyOffStroke();
        });

        PickerHandler.Mapper.AppendToMapping("ThemeFlyout", (handler, view) =>
        {
            if (handler.PlatformView is Microsoft.UI.Xaml.Controls.ComboBox comboBox)
            {
                Themes.NativeFlyoutTheming.TrackComboBox(comboBox);
            }
            else
            {
                Themes.NativeFlyoutTheming.Track(handler.PlatformView);
            }
        });

        DatePickerHandler.Mapper.AppendToMapping("ThemeFlyout", (handler, view) =>
        {
            Themes.NativeFlyoutTheming.Track(handler.PlatformView);
        });

        CheckBoxHandler.Mapper.AppendToMapping("ThemeCheckBox", (handler, view) =>
        {
            Themes.NativeFlyoutTheming.Track(handler.PlatformView);
        });
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif


        ArcGISRuntimeEnvironment.ApiKey = "";

        return builder.Build();
    }
}
