using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
namespace LaundryPractice;

public partial class MainWindow : Window
{
    internal readonly User CurrentUser;
    internal string Entity = "orders";
    internal DataTable Data = new();
    private bool loading;
    private bool allowClose;
    private int loadVersion;
    public MainWindow(User user)
    {
        InitializeComponent(); CurrentUser = user;
        UserText.Text = $"{user.FullName} · {(user.IsAdmin ? "Администратор" : "Пользователь")}";
        AdminActions.Visibility = user.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        SectionInput.ItemsSource = user.IsAdmin ? new[] { "Заказы", "Услуги", "Пользователи" } : new[] { "Заказы", "Услуги" };
        SectionInput.SelectedIndex = 0;
        Loaded += async (_, _) => await Reload();
    }
    internal async Task Reload()
    {
        int version = ++loadVersion; loading = true;
        try
        {
            DataTable next = await Db.ReadTable(Entity, CurrentUser); if (version != loadVersion) return; Data = next;
            FilterLabel.Text = Entity == "orders" ? "Статус" : "Активность";
            FilterInput.ItemsSource = Entity == "orders" ? new[] { "Все" }.Concat(Db.Statuses).ToArray() : new[] { "Все", "Активные", "Отключённые" };
            FilterInput.SelectedIndex = 0;
            SortInput.ItemsSource = Data.Columns.Cast<DataColumn>().Select(c => new KeyValuePair<string, string>(c.ColumnName, ReportExporter.Header(c.ColumnName))).ToList();
            SortInput.SelectedIndex = 0; SearchInput.Clear();
            Title = $"{ReportExporter.SectionName(Entity)} — {(CurrentUser.IsAdmin ? "Администратор" : "Пользователь")}";
            loading = false; ApplyFilter();
        }
        catch (Exception ex) { if (version == loadVersion) ResultText.Text = Ui.ErrorText(ex); }
        finally { if (version == loadVersion) loading = false; }
    }
    internal void ApplyFilter()
    {
        if (loading || !IsInitialized || Data.Columns.Count == 0) return;
        string search = SearchInput.Text.Trim(); DataTable filtered = Data.Clone();
        foreach (DataRow row in Data.Rows)
        {
            bool match = row.ItemArray.Any(value => ReportExporter.CellText(value).Contains(search, StringComparison.CurrentCultureIgnoreCase));
            if (FilterInput.SelectedIndex > 0) match &= Entity == "orders" ? row["status"].ToString() == FilterInput.SelectedItem?.ToString() : (bool)row["active"] == (FilterInput.SelectedIndex == 1);
            if (match) filtered.ImportRow(row);
        }
        var view = filtered.DefaultView;
        if (SortInput.SelectedValue is string column && filtered.Columns.Contains(column)) view.Sort = $"[{column}] {(DirectionInput.SelectedIndex == 1 ? "DESC" : "ASC")}";
        Records.ItemsSource = view; ResultText.Text = $"Показано записей: {view.Count} из {Data.Rows.Count}.";
        ChartPanel.Visibility = CurrentUser.IsAdmin && Entity == "orders" ? Visibility.Visible : Visibility.Collapsed;
        ChartBars.Children.Clear();
        if (Entity == "orders" && CurrentUser.IsAdmin)
        {
            int max = Math.Max(1, Db.Statuses.Max(status => filtered.Rows.Cast<DataRow>().Count(r => (string)r["status"] == status)));
            foreach (string status in Db.Statuses)
            {
                int count = filtered.Rows.Cast<DataRow>().Count(r => (string)r["status"] == status);
                var item = new StackPanel { Width = 230, Margin = new Thickness(0, 0, 12, 0) };
                item.Children.Add(new TextBlock { Text = $"{status}: {count}", Margin = new Thickness(0, 0, 0, 5) });
                item.Children.Add(new Border { Height = 18, Width = 200d * count / max, HorizontalAlignment = HorizontalAlignment.Left, Background = (Brush)FindResource("AccentBrush"), CornerRadius = new CornerRadius(3), ToolTip = $"{status}: {count} заказов" }); ChartBars.Children.Add(item);
            }
        }
    }
    private async void SectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurrentUser is null) return;
        Entity = SectionInput.SelectedIndex switch { 1 => "services", 2 => "users", _ => "orders" }; if (IsLoaded) await Reload();
    }
    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();
    private async void Refresh(object sender, RoutedEventArgs e) => await Reload();
    private int SelectedId => Records.SelectedItem is DataRowView row ? (int)row["id"] : 0;
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) { if (EditButton is not null) EditButton.IsEnabled = DeleteButton.IsEnabled = SelectedId > 0 && CurrentUser?.IsAdmin == true; }
    private void GenerateColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.Header = ReportExporter.Header(e.PropertyName);
        if (e.Column is DataGridTextColumn text)
        {
            if (e.PropertyType == typeof(DateTime)) text.Binding = new Binding(e.PropertyName) { StringFormat = "dd.MM.yyyy" };
            if (e.PropertyType == typeof(decimal)) text.Binding = new Binding(e.PropertyName) { StringFormat = e.PropertyName == "quantity" ? "0.###" : "N2" };
        }
        e.Column.MinWidth = 75;
    }
    private async Task EditRecord(int id)
    {
        try { await Db.RequireAdmin(CurrentUser); if (new EditWindow(CurrentUser, Entity, id) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog() == true) await Reload(); }
        catch (Exception ex) { Ui.Message(this, Ui.ErrorText(ex)); }
    }
    private async void Add(object sender, RoutedEventArgs e) => await EditRecord(0);
    private async void Edit(object sender, RoutedEventArgs e) { if (SelectedId > 0) await EditRecord(SelectedId); }
    private async void DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (CurrentUser.IsAdmin && SelectedId > 0) await EditRecord(SelectedId); }
    private async void Delete(object sender, RoutedEventArgs e)
    {
        if (SelectedId == 0) return;
        try
        {
            int id = SelectedId; int related = Entity == "orders" ? 0 : await Db.CountRelated(Entity, id, CurrentUser);
            if (!Ui.Message(this, $"Удалить запись №{id}?" + (related > 0 ? $" Также будут удалены связанные заказы: {related}." : ""), true)) return;
            await Db.Delete(Entity, id, CurrentUser); await Reload(); Ui.Message(this, "Запись удалена.");
        }
        catch (Exception ex) { Ui.Message(this, Ui.ErrorText(ex)); }
    }
    private async void Export(object sender, RoutedEventArgs e)
    {
        try
        {
            await Db.RequireAdmin(CurrentUser); string format = ((Button)sender).Tag.ToString()!;
            var dialog = new SaveFileDialog { Filter = $"{format.ToUpperInvariant()}|*.{format}", DefaultExt = format, FileName = $"{ReportExporter.SectionName(Entity)}_{DateTime.Now:yyyy-MM-dd}.{format}" };
            if (dialog.ShowDialog(this) != true) return;
            ReportExporter.Export(((DataView)Records.ItemsSource).ToTable(), ReportExporter.SectionName(Entity), dialog.FileName, format);
            Ui.Message(this, $"Файл сохранён:\n{dialog.FileName}");
        }
        catch (Exception ex) { Ui.Message(this, Ui.ErrorText(ex)); }
    }
    private void Password(object sender, RoutedEventArgs e) => new AccountWindow(CurrentUser) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    private async void Theme(object sender, RoutedEventArgs e) { try { await Ui.ToggleTheme(CurrentUser); } catch (Exception ex) { Ui.Message(this, Ui.ErrorText(ex)); } }
    private void Logout(object sender, RoutedEventArgs e) { if (!Ui.Message(this, "Выйти из учётной записи?", true)) return; new LoginWindow().Show(); allowClose = true; Close(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (!Ui.Message(this, "Закрыть приложение?", true)) { e.Cancel = true; return; }
        Application.Current.Shutdown();
    }
}
