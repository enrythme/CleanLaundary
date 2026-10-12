using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Npgsql;

namespace LaundryPractice;

public sealed class Ui : DependencyObject
{
    public static readonly DependencyProperty TrackPasswordProperty = DependencyProperty.RegisterAttached("TrackPassword", typeof(bool), typeof(Ui), new PropertyMetadata(false, (d, e) =>
    {
        if (d is PasswordBox box && (bool)e.NewValue) box.PasswordChanged += (_, _) => box.SetValue(IsPasswordEmptyProperty, box.Password.Length == 0);
    }));
    public static readonly DependencyProperty IsPasswordEmptyProperty = DependencyProperty.RegisterAttached("IsPasswordEmpty", typeof(bool), typeof(Ui), new PropertyMetadata(true));
    public static bool GetTrackPassword(DependencyObject d) => (bool)d.GetValue(TrackPasswordProperty);
    public static void SetTrackPassword(DependencyObject d, bool value) => d.SetValue(TrackPasswordProperty, value);
    public static bool GetIsPasswordEmpty(DependencyObject d) => (bool)d.GetValue(IsPasswordEmptyProperty);
    public static void SetIsPasswordEmpty(DependencyObject d, bool value) => d.SetValue(IsPasswordEmptyProperty, value);

    public static void ApplyTheme() => Application.Current.Resources.MergedDictionaries[0].Source = new Uri($"pack://application:,,,/res/{Settings.Current.Theme}.xaml", UriKind.Absolute);

    public static async Task ToggleTheme(User? user = null)
    {
        Settings.Current.Theme = Settings.Current.Theme == "Light" ? "Dark" : "Light";
        ApplyTheme();
        Settings.Save();
        if (user is not null) await Db.SaveTheme(user, Settings.Current.Theme);
    }

    public static string ErrorText(Exception ex) => ex switch
    {
        PostgresException { SqlState: "23505" } => "Такой логин, адрес почты или название услуги уже существует.",
        PostgresException { SqlState: "23503" } => "Связанная запись уже удалена. Обновите таблицу.",
        PostgresException { SqlState: "23514" } => "Значение не соответствует ограничениям базы данных.",
        NpgsqlException => "Не удалось подключиться к PostgreSQL. Проверьте запуск сервера и строку подключения в appsettings.json.",
        System.IO.IOException => "Не удалось прочитать или сохранить файл. Проверьте путь и доступ к папке.",
        UnauthorizedAccessException => "Нет доступа к выбранной папке или файлу.",
        _ => ex.Message
    };

    public static bool Message(Window? owner, string message, bool confirm = false)
    {
        var dialog = new DialogWindow(message, confirm);
        if (owner?.IsVisible == true) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        return dialog.ShowDialog() == true;
    }

    public static void Restrict(TextBox box, string pattern)
    {
        bool Valid(string text) => Regex.IsMatch(text, pattern);
        box.PreviewTextInput += (_, e) => e.Handled = !Valid(box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, e.Text));
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            string? text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (text is null || !Valid(box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, text))) e.CancelCommand();
        });
    }

    public static decimal Number(string text)
    {
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number)) throw new ArgumentException("Введите число без пробелов и разделителей тысяч.");
        return number;
    }

    public static BitmapSource CaptchaImage(string text)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(234, 235, 237)), null, new Rect(0, 0, 220, 64));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(1, 152, 175)), 1);
            for (int i = 0; i < 10; i++) dc.DrawLine(pen, new Point(Random.Shared.Next(220), Random.Shared.Next(64)), new Point(Random.Shared.Next(220), Random.Shared.Next(64)));
            for (int i = 0; i < text.Length; i++)
            {
                dc.PushTransform(new RotateTransform(Random.Shared.Next(-12, 13), 25 + i * 39, 32));
                var typeface = new Typeface((FontFamily)Application.Current.FindResource("AppFont"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                dc.DrawText(new FormattedText(text[i].ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 28, new SolidColorBrush(Color.FromRgb(35, 62, 118)), 1), new Point(14 + i * 39, 12));
                dc.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap(220, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}

public sealed class DialogWindow : Window
{
    public DialogWindow(string message, bool confirm)
    {
        Style = (Style)FindResource(typeof(Window));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = confirm ? "Подтверждение" : "Сообщение";
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = confirm ? "Подтвердить" : "ОК", IsDefault = true, ToolTip = "Закрыть сообщение" };
        ok.Click += (_, _) => DialogResult = true; buttons.Children.Add(ok);
        if (confirm) { var cancel = new Button { Content = "Отмена", IsCancel = true, Style = (Style)FindResource("SecondaryButton"), ToolTip = "Отменить действие" }; buttons.Children.Add(cancel); }
        panel.Children.Add(buttons); Content = panel;
    }
}
