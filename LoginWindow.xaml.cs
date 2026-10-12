using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace LaundryPractice;

public partial class LoginWindow : Window
{
    internal string CaptchaCode = "";
    private bool allowClose;
    private int lookupVersion;
    public LoginWindow()
    {
        InitializeComponent();
        Ui.Restrict(LoginInput, "^[A-Za-z0-9_.-]*$");
        Ui.Restrict(CaptchaInput, "^[A-Za-z0-9]*$");
        if (Security.CaptchaRequired()) ShowCaptcha();
    }

    internal void ShowCaptcha()
    {
        CaptchaCode = Security.GenerateCaptcha();
        CaptchaPicture.Source = Ui.CaptchaImage(CaptchaCode);
        CaptchaInput.Clear(); CaptchaPanel.Visibility = Visibility.Visible;
    }

    private void RefreshCaptcha(object sender, RoutedEventArgs e) => ShowCaptcha();
    private async void LoginChanged(object sender, TextChangedEventArgs e)
    {
        if (CaptchaPanel is null) return;
        int version = ++lookupVersion;
        try
        {
            await Task.Delay(300);
            if (version != lookupVersion) return;
            User? user = await Db.FindUser(LoginInput.Text.Trim());
            if (version == lookupVersion && Security.CaptchaRequired(user) && CaptchaPanel.Visibility != Visibility.Visible) ShowCaptcha();
        }
        catch (Exception) { }
    }

    internal async Task<bool> TryLogin(bool navigate = true)
    {
        LoginButton.IsEnabled = false;
        try
        {
            if (LoginInput.Text.Length < 3 || PasswordInput.Password.Length == 0) throw new ArgumentException("Введите логин и пароль.");
            bool captchaPassed = CaptchaPanel.Visibility == Visibility.Visible && CaptchaCode.Length == 5 && Security.VerifyCaptcha(CaptchaInput.Text, CaptchaCode);
            User user = await Security.Authenticate(LoginInput.Text, PasswordInput.Password, captchaPassed);
            Hint.Text = "";
            if (navigate)
            {
                Settings.Current.Theme = user.Theme; Settings.Save(); Ui.ApplyTheme();
                new MainWindow(user).Show(); allowClose = true; Close();
            }
            return true;
        }
        catch (Exception ex)
        {
            Hint.Text = Ui.ErrorText(ex);
            User? user = null;
            try { user = await Db.FindUser(LoginInput.Text.Trim()); } catch (Exception) { }
            if (Security.CaptchaRequired(user)) ShowCaptcha();
            return false;
        }
        finally { LoginButton.IsEnabled = true; }
    }

    private async void SignIn(object sender, RoutedEventArgs e) => await TryLogin();
    private void Register(object sender, RoutedEventArgs e) => new AccountWindow().ShowDialog();
    private void Exit(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (!Ui.Message(this, "Закрыть приложение?", true)) { e.Cancel = true; return; }
        Application.Current.Shutdown();
    }
}
