using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace LaundryPractice;

public partial class App : Application
{
    static App()
    {
        CultureInfo.DefaultThreadCurrentCulture=CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("ru-RU");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        DispatcherUnhandledException += (_, args) => { Ui.Message(null, Ui.ErrorText(args.Exception)); args.Handled = true; };
        try
        {
            Settings.Load();
            Db.Initialize();
            Ui.ApplyTheme();
            new LoginWindow().Show();
        }
        catch (Exception ex) { Ui.Message(null, Ui.ErrorText(ex)); Shutdown(1); }
    }
}
