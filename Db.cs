using System.Data;
using System.IO;
using System.Text.Json.Nodes;
using Npgsql;

namespace LaundryPractice;

public static class Db
{
    public static readonly string[] Branches = ["Центральный", "Северный", "Южный"];
    public static readonly string[] Statuses = ["Принят", "В работе", "Готов", "Выдан"];
    private static string connectionString = "";

    public static void Initialize()
    {
        connectionString = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))?["ConnectionString"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Заполните appsettings.json.");
    }

    public static async Task<NpgsqlConnection> OpenContext()
    {
        var context = new NpgsqlConnection(connectionString);
        try { await context.OpenAsync(); return context; }
        catch { await context.DisposeAsync(); throw; }
    }

    private static async Task<DataTable> Query(string sql, params (string Name, object Value)[] values)
    {
        await using var context = await OpenContext();
        await using var command = new NpgsqlCommand(sql, context);
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new DataTable();
        result.Load(reader);
        return result;
    }

    private static async Task<int> Execute(string sql, params (string Name, object Value)[] values)
    {
        await using var context = await OpenContext();
        await using var command = new NpgsqlCommand(sql, context);
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        return await command.ExecuteNonQueryAsync();
    }

    private static User ReadUser(DataRow r) => new()
    {
        Id = (int)r["id"],
        Login = (string)r["login"],
        FullName = (string)r["full_name"],
        Email = (string)r["email"],
        PasswordHash = (string)r["password_hash"],
        Role = (string)r["role"],
        Theme = (string)r["theme"],
        FailedAttempts = (int)r["failed_attempts"],
        Active = (bool)r["active"]
    };

    public static async Task<User?> FindUser(string login)
    {
        DataTable table = await Query("SELECT * FROM users WHERE login=@login", ("login", login));
        return table.Rows.Count == 0 ? null : ReadUser(table.Rows[0]);
    }

    public static async Task<User> GetUser(int id)
    {
        DataTable table = await Query("SELECT * FROM users WHERE id=@id", ("id", id));
        if (table.Rows.Count == 0) throw new InvalidOperationException("Учётная запись не найдена.");
        return ReadUser(table.Rows[0]);
    }

    public static async Task RequireAdmin(User actor)
    {
        User current = await GetUser(actor.Id);
        if (!current.IsAdmin || !current.Active) throw new InvalidOperationException("Действие доступно только администратору.");
    }

    public static Task SetFailedAttempts(int id, bool reset) => Execute(reset
        ? "UPDATE users SET failed_attempts=0 WHERE id=@id"
        : "UPDATE users SET failed_attempts=LEAST(100,failed_attempts+1) WHERE id=@id", ("id", id));

    public static async Task Register(User user, string password)
    {
        user.Role = "User";
        user.Active = true;
        Security.ValidateUser(user);
        string hash = Security.HashPassword(password);
        await Execute("INSERT INTO users(login,full_name,email,password_hash,role,theme) VALUES(@login,@name,@email,@hash,'User',@theme)",
            ("login", user.Login), ("name", user.FullName.Trim()), ("email", user.Email), ("hash", hash), ("theme", Settings.Current.Theme));
    }

    public static async Task ChangePassword(User user, string oldPassword, string newPassword)
    {
        User current = await GetUser(user.Id);
        if (!current.Active || !Security.VerifyPassword(oldPassword, current.PasswordHash)) throw new InvalidOperationException("Текущий пароль неверен.");
        string hash = Security.HashPassword(newPassword);
        int updated = await Execute("UPDATE users SET password_hash=@new,failed_attempts=0 WHERE id=@id AND password_hash=@old",
            ("new", hash), ("id", user.Id), ("old", current.PasswordHash));
        if (updated != 1) throw new InvalidOperationException("Пароль уже изменён. Войдите заново.");
    }

    public static Task SaveTheme(User user, string theme) => Execute("UPDATE users SET theme=@theme WHERE id=@id", ("theme", theme), ("id", user.Id));

    public static async Task<DataTable> ReadTable(string entity, User user)
    {
        User current = await GetUser(user.Id);
        if (!current.Active) throw new InvalidOperationException("Учётная запись отключена.");
        return entity switch
        {
            "orders" => await Query("SELECT o.id,u.full_name AS client,s.name AS service,o.branch,o.received_date,o.due_date,o.quantity,o.unit_price,o.total,o.status,o.description FROM orders o JOIN users u ON u.id=o.user_id JOIN services s ON s.id=o.service_id WHERE @admin OR o.user_id=@id ORDER BY o.id DESC", ("admin", current.IsAdmin), ("id", current.Id)),
            "services" => await Query("SELECT id,name,unit,price,active FROM services WHERE @admin OR active ORDER BY id", ("admin", current.IsAdmin)),
            "users" when current.IsAdmin => await Query("SELECT id,login,full_name,email,CASE role WHEN 'Admin' THEN 'Администратор' ELSE 'Пользователь' END AS role,active FROM users ORDER BY id"),
            _ => throw new InvalidOperationException("Раздел недоступен.")
        };
    }

    public static async Task<List<User>> GetClients(User actor)
    {
        await RequireAdmin(actor);
        DataTable table = await Query("SELECT * FROM users WHERE active ORDER BY full_name");
        return table.Rows.Cast<DataRow>().Select(ReadUser).ToList();
    }

    public static async Task<List<Service>> GetServices()
    {
        DataTable table = await Query("SELECT * FROM services WHERE active ORDER BY name");
        return table.Rows.Cast<DataRow>().Select(r => new Service { Id = (int)r["id"], Name = (string)r["name"], Unit = (string)r["unit"], Price = (decimal)r["price"], Active = (bool)r["active"] }).ToList();
    }

    public static async Task<Service> GetService(int id)
    {
        DataTable table = await Query("SELECT * FROM services WHERE id=@id", ("id", id));
        if (table.Rows.Count == 0) throw new InvalidOperationException("Услуга не найдена.");
        DataRow r = table.Rows[0];
        return new Service { Id = id, Name = (string)r["name"], Unit = (string)r["unit"], Price = (decimal)r["price"], Active = (bool)r["active"] };
    }

    public static async Task<Order> GetOrder(int id, User actor)
    {
        await RequireAdmin(actor);
        DataTable table = await Query("SELECT * FROM orders WHERE id=@id", ("id", id));
        if (table.Rows.Count == 0) throw new InvalidOperationException("Заказ не найден.");
        DataRow r = table.Rows[0];
        return new Order { Id = id, UserId = (int)r["user_id"], ServiceId = (int)r["service_id"], Branch = (string)r["branch"], ReceivedDate = (DateTime)r["received_date"], DueDate = (DateTime)r["due_date"], Quantity = (decimal)r["quantity"], UnitPrice = (decimal)r["unit_price"], Status = (string)r["status"], Description = (string)r["description"] };
    }

    public static async Task SaveService(User actor, Service service)
    {
        await RequireAdmin(actor);
        Security.ValidateService(service);
        await Execute(service.Id == 0
            ? "INSERT INTO services(name,unit,price,active) VALUES(@name,@unit,@price,@active)"
            : "UPDATE services SET name=@name,unit=@unit,price=@price,active=@active WHERE id=@id",
            ("name", service.Name.Trim()), ("unit", service.Unit), ("price", service.Price), ("active", service.Active), ("id", service.Id));
    }

    public static async Task SaveUser(User actor, User user, string password)
    {
        await RequireAdmin(actor);
        Security.ValidateUser(user);
        if (user.Id == actor.Id && (!user.Active || !user.IsAdmin)) throw new InvalidOperationException("Нельзя отключить свою учётную запись или изменить собственную роль.");
        if (user.Id > 0)
        {
            User old = await GetUser(user.Id);
            DataTable admins = await Query("SELECT count(*) AS count FROM users WHERE role='Admin' AND active");
            if (old.IsAdmin && old.Active && (!user.Active || !user.IsAdmin) && (long)admins.Rows[0]["count"] <= 1)
                throw new InvalidOperationException("В системе должен остаться активный администратор.");
        }
        string hash = user.Id == 0 ? Security.HashPassword(password) : (await GetUser(user.Id)).PasswordHash;
        if (user.Id > 0 && password.Length > 0) hash = Security.HashPassword(password);
        await Execute(user.Id == 0
            ? "INSERT INTO users(login,full_name,email,password_hash,role,active) VALUES(@login,@name,@email,@hash,@role,@active)"
            : "UPDATE users SET login=@login,full_name=@name,email=@email,password_hash=@hash,role=@role,active=@active WHERE id=@id",
            ("login", user.Login), ("name", user.FullName.Trim()), ("email", user.Email), ("hash", hash), ("role", user.Role), ("active", user.Active), ("id", user.Id));
    }

    public static async Task SaveOrder(User actor, Order order)
    {
        await RequireAdmin(actor);
        Service service = await GetService(order.ServiceId);
        User client = await GetUser(order.UserId);
        if (!service.Active || !client.Active) throw new ArgumentException("Выберите активного пользователя и активную услугу.");
        if (!Branches.Contains(order.Branch) || !Statuses.Contains(order.Status)) throw new ArgumentException("Выберите филиал и статус.");
        if (order.Quantity < 0.001m || order.Quantity > 100 || decimal.Round(order.Quantity, 3) != order.Quantity || (service.Unit == "шт" && order.Quantity != decimal.Truncate(order.Quantity)))
            throw new ArgumentException("Количество: от 0,001 до 100. Для штук используйте целое число, для килограммов — до трёх знаков после запятой.");
        if (order.Description.Trim().Length < 2 || order.Description.Length > 250) throw new ArgumentException("Описание вещи: от 2 до 250 символов.");
        if (order.ReceivedDate.Date < DateTime.Today.AddYears(-10) || order.ReceivedDate.Date > DateTime.Today || order.DueDate.Date < order.ReceivedDate.Date || order.DueDate.Date > order.ReceivedDate.Date.AddDays(365))
            throw new ArgumentException("Дата приёма не должна быть в будущем. Срок готовности: от даты приёма до одного года после неё.");
        decimal price = order.Id == 0 || (await GetOrder(order.Id, actor)).ServiceId != order.ServiceId ? service.Price : order.UnitPrice;
        if (price < 1 || price > 100000 || decimal.Round(price, 2) != price) throw new ArgumentException("Цена заказа должна быть от 1 до 100 000 рублей.");
        await Execute(order.Id == 0
            ? "INSERT INTO orders(user_id,service_id,branch,received_date,due_date,quantity,unit_price,status,description) VALUES(@user,@service,@branch,@received,@due,@quantity,@price,@status,@description)"
            : "UPDATE orders SET user_id=@user,service_id=@service,branch=@branch,received_date=@received,due_date=@due,quantity=@quantity,unit_price=@price,status=@status,description=@description WHERE id=@id",
            ("user", order.UserId), ("service", order.ServiceId), ("branch", order.Branch), ("received", DateOnly.FromDateTime(order.ReceivedDate)), ("due", DateOnly.FromDateTime(order.DueDate)), ("quantity", order.Quantity), ("price", price), ("status", order.Status), ("description", order.Description.Trim()), ("id", order.Id));
    }

    public static async Task<int> CountRelated(string entity, int id, User actor)
    {
        await RequireAdmin(actor);
        string column = entity switch { "users" => "user_id", "services" => "service_id", _ => "id" };
        DataTable table = await Query($"SELECT count(*) AS count FROM orders WHERE {column}=@id", ("id", id));
        return Convert.ToInt32(table.Rows[0]["count"]);
    }

    public static async Task Delete(string entity, int id, User actor)
    {
        await RequireAdmin(actor);
        if (entity is not ("users" or "services" or "orders")) throw new ArgumentException("Неизвестная таблица.");
        if (entity == "users" && id == actor.Id) throw new InvalidOperationException("Нельзя удалить собственную учётную запись.");
        await Execute($"DELETE FROM {entity} WHERE id=@id", ("id", id));
    }
}
