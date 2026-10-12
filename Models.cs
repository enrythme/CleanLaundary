namespace LaundryPractice;

public sealed class User
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "User";
    public string Theme { get; set; } = "Light";
    public int FailedAttempts { get; set; }
    public bool Active { get; set; } = true;
    public bool IsAdmin => Role == "Admin";
}

public sealed class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "шт";
    public decimal Price { get; set; }
    public bool Active { get; set; } = true;
    public override string ToString() => $"{Name} ({Unit}, {Price:N2} ₽)";
}

public sealed class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ServiceId { get; set; }
    public string Branch { get; set; } = "Центральный";
    public DateTime ReceivedDate { get; set; } = DateTime.Today;
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(3);
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public string Status { get; set; } = "Принят";
    public string Description { get; set; } = "";
}
