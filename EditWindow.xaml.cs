using System.Windows;
namespace LaundryPractice;

public partial class EditWindow : Window
{
    private readonly User actor;
    private readonly string entity;
    private readonly int id;
    private Order? originalOrder;
    private bool ready;
    public EditWindow(User actor, string entity, int id = 0)
    {
        InitializeComponent(); this.actor = actor; this.entity = entity; this.id = id;
        Title = $"{(id == 0 ? "Добавление" : "Редактирование")} — {ReportExporter.SectionName(entity)}"; HeadingText.Text = Title;
        Ui.Restrict(LoginInput, "^[A-Za-z0-9_.-]*$"); Ui.Restrict(FullNameInput, @"^[\p{L} '\-]*$"); Ui.Restrict(EmailInput, @"^\S*$");
        Ui.Restrict(PriceInput, @"^\d{0,6}([.,]\d{0,2})?$"); Ui.Restrict(QuantityInput, @"^\d{0,3}([.,]\d{0,3})?$");
        ReceivedInput.DateValidationError += (_, e) => { e.ThrowException = false; Hint.Text = "Введите дату в формате дд.мм.гггг."; };
        DueInput.DateValidationError += (_, e) => { e.ThrowException = false; Hint.Text = "Введите дату в формате дд.мм.гггг."; };
        Loaded += async (_, _) => { try { await LoadRecord(); } catch (Exception ex) { Hint.Text = Ui.ErrorText(ex); SaveButton.IsEnabled = false; } };
    }
    internal async Task LoadRecord()
    {
        ready = false; SaveButton.IsEnabled = false; await Db.RequireAdmin(actor);
        if (entity == "users")
        {
            UserFields.Visibility = Visibility.Visible; RoleInput.SelectedIndex = 0;
            if (id > 0) { User u = await Db.GetUser(id); LoginInput.Text = u.Login; FullNameInput.Text = u.FullName; EmailInput.Text = u.Email; RoleInput.SelectedIndex = u.IsAdmin ? 1 : 0; ActiveInput.IsChecked = u.Active; }
        }
        else if (entity == "services")
        {
            ServiceFields.Visibility = Visibility.Visible; UnitInput.SelectedIndex = 0;
            if (id > 0) { Service s = await Db.GetService(id); ServiceNameInput.Text = s.Name; UnitInput.SelectedIndex = s.Unit == "кг" ? 1 : 0; PriceInput.Text = s.Price.ToString("0.00"); ActiveInput.IsChecked = s.Active; }
        }
        else if (entity == "orders")
        {
            OrderFields.Visibility = Visibility.Visible; ActiveInput.Visibility = Visibility.Collapsed;
            ClientInput.ItemsSource = await Db.GetClients(actor); ServiceInput.ItemsSource = await Db.GetServices();
            BranchInput.ItemsSource = Db.Branches; StatusInput.ItemsSource = Db.Statuses;
            ReceivedInput.DisplayDateStart = DateTime.Today.AddYears(-10); ReceivedInput.DisplayDateEnd = DateTime.Today;
            originalOrder = id > 0 ? await Db.GetOrder(id, actor) : new Order();
            ClientInput.SelectedValue = originalOrder.UserId; ServiceInput.SelectedItem = ((List<Service>)ServiceInput.ItemsSource).FirstOrDefault(s => s.Id == originalOrder.ServiceId);
            BranchInput.SelectedItem = originalOrder.Branch; StatusInput.SelectedItem = originalOrder.Status;
            ReceivedInput.SelectedDate = originalOrder.ReceivedDate; DueInput.SelectedDate = originalOrder.DueDate;
            QuantityInput.Text = originalOrder.Quantity.ToString("0.###"); DescriptionInput.Text = originalOrder.Description;
            if (id > 0 && (ClientInput.SelectedItem is null || ServiceInput.SelectedItem is null)) Hint.Text = "Клиент или услуга отключены. Для изменения выберите активные записи.";
        }
        else throw new ArgumentException("Неизвестная таблица.");
        ready = true; SaveButton.IsEnabled = true; UpdatePrice();
    }
    private void PriceChanged(object sender, RoutedEventArgs e) => UpdatePrice();
    private void UpdatePrice()
    {
        if (!ready || ServiceInput.SelectedItem is not Service service) return;
        decimal price = id > 0 && originalOrder?.ServiceId == service.Id ? originalOrder.UnitPrice : service.Price;
        try { TotalText.Text = $"Цена: {price:N2} ₽/{service.Unit}. Итого: {price * Ui.Number(QuantityInput.Text):N2} ₽"; }
        catch (ArgumentException) { TotalText.Text = "Введите количество для расчёта суммы."; }
    }
    internal async Task<bool> TrySave(bool close = true)
    {
        SaveButton.IsEnabled = false;
        try
        {
            if (entity == "users") await Db.SaveUser(actor, new User { Id = id, Login = LoginInput.Text.Trim(), FullName = FullNameInput.Text.Trim(), Email = EmailInput.Text.Trim(), Role = RoleInput.SelectedIndex == 1 ? "Admin" : "User", Active = ActiveInput.IsChecked == true }, PasswordInput.Password);
            else if (entity == "services") await Db.SaveService(actor, new Service { Id = id, Name = ServiceNameInput.Text.Trim(), Unit = UnitInput.SelectedIndex == 1 ? "кг" : "шт", Price = Ui.Number(PriceInput.Text), Active = ActiveInput.IsChecked == true });
            else
            {
                if (ClientInput.SelectedItem is not User client || ServiceInput.SelectedItem is not Service service || ReceivedInput.SelectedDate is null || DueInput.SelectedDate is null) throw new ArgumentException("Выберите клиента, услугу и обе даты.");
                await Db.SaveOrder(actor, new Order { Id = id, UserId = client.Id, ServiceId = service.Id, Branch = BranchInput.SelectedItem as string ?? "", Status = StatusInput.SelectedItem as string ?? "", ReceivedDate = ReceivedInput.SelectedDate.Value, DueDate = DueInput.SelectedDate.Value, Quantity = Ui.Number(QuantityInput.Text), Description = DescriptionInput.Text.Trim(), UnitPrice = originalOrder?.UnitPrice ?? 0 });
            }
            Hint.Text = "Запись сохранена."; if (close) { Ui.Message(this, Hint.Text); DialogResult = true; }
            return true;
        }
        catch (Exception ex) { Hint.Text = Ui.ErrorText(ex); return false; }
        finally { SaveButton.IsEnabled = true; }
    }
    private async void Save(object sender, RoutedEventArgs e) => await TrySave();
}
