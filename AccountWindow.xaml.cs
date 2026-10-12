using System.Windows;
namespace LaundryPractice;

public partial class AccountWindow : Window
{
    private readonly User? user;
    public AccountWindow(User? current = null)
    {
        InitializeComponent(); user = current;
        Ui.Restrict(LoginInput, "^[A-Za-z0-9_.-]*$"); Ui.Restrict(NameInput, @"^[\p{L} '\-]*$"); Ui.Restrict(EmailInput, @"^\S*$");
        if (current is not null)
        {
            Title = "Смена пароля"; HeadingText.Text = Title; SaveButton.Content = "Сохранить пароль";
            RegistrationFields.Visibility = Visibility.Collapsed; OldPasswordFields.Visibility = Visibility.Visible;
        }
    }
    internal async Task<bool> TrySave(bool close = true)
    {
        SaveButton.IsEnabled = false;
        try
        {
            if (PasswordInput.Password != RepeatInput.Password) throw new ArgumentException("Пароли не совпадают.");
            if (user is null) await Db.Register(new User { Login = LoginInput.Text.Trim(), FullName = NameInput.Text.Trim(), Email = EmailInput.Text.Trim() }, PasswordInput.Password);
            else await Db.ChangePassword(user, OldPasswordInput.Password, PasswordInput.Password);
            Hint.Text = user is null ? "Регистрация завершена. Теперь можно войти." : "Пароль изменён.";
            if (close) { Ui.Message(this, Hint.Text); DialogResult = true; }
            return true;
        }
        catch (Exception ex) { Hint.Text = Ui.ErrorText(ex); return false; }
        finally { SaveButton.IsEnabled = true; }
    }
    private async void Save(object sender, RoutedEventArgs e) => await TrySave();
}
