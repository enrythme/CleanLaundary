using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LaundryPractice;

public static class Security
{
    public static string HashPassword(string password)
    {
        ValidatePassword(password);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 150000, HashAlgorithmName.SHA256, 32);
        return $"150000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        try
        {
            string[] parts = stored.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int iterations) || iterations < 100000 || iterations > 1000000) return false;
            byte[] salt = Convert.FromBase64String(parts[1]);
            byte[] expected = Convert.FromBase64String(parts[2]);
            if (salt.Length != 16 || expected.Length != 32 || password.Length > 64) return false;
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    public static void ValidatePassword(string value)
    {
        if (value.Length < 8 || value.Length > 64 || !value.Any(char.IsLetter) || !value.Any(char.IsDigit))
            throw new ArgumentException("Пароль: от 8 до 64 символов, хотя бы одна буква и одна цифра.");
    }

    public static void ValidateUser(User user)
    {
        if (!Regex.IsMatch(user.Login, "^[A-Za-z0-9_.-]{3,40}$")) throw new ArgumentException("Логин: от 3 до 40 латинских букв, цифр, точек, дефисов или символов подчёркивания.");
        if (user.FullName.Trim().Length < 2 || user.FullName.Length > 100 || !Regex.IsMatch(user.FullName, @"^[\p{L} '\-]+$"))
            throw new ArgumentException("ФИО: от 2 до 100 символов, только буквы, пробелы, дефисы и апострофы.");
        if (user.Email.Length > 150 || !Regex.IsMatch(user.Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$") || !MailAddress.TryCreate(user.Email, out MailAddress? email) || email.Address != user.Email)
            throw new ArgumentException("Введите корректный адрес электронной почты.");
        if (user.Role is not ("User" or "Admin")) throw new ArgumentException("Выберите роль пользователя.");
    }

    public static void ValidateService(Service service)
    {
        if (service.Name.Trim().Length < 2 || service.Name.Length > 120) throw new ArgumentException("Название услуги: от 2 до 120 символов.");
        if (service.Unit is not ("шт" or "кг")) throw new ArgumentException("Выберите единицу измерения.");
        if (service.Price < 1 || service.Price > 100000 || decimal.Round(service.Price, 2) != service.Price)
            throw new ArgumentException("Цена: от 1 до 100 000 рублей, не более двух знаков после запятой.");
    }

    public static bool CaptchaRequired(User? user = null) => Settings.Current.FailedLogins >= 3 || user?.FailedAttempts >= 3;

    public static string GenerateCaptcha()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 5).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    public static bool VerifyCaptcha(string input, string expected) => expected.Length == 5 && input.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    public static async Task<User> Authenticate(string login, string password, bool captchaPassed)
    {
        User? user = await Db.FindUser(login.Trim());
        if (CaptchaRequired(user) && !captchaPassed) throw new InvalidOperationException("Введите код с изображения капчи.");
        if (user is null || !user.Active || !VerifyPassword(password, user.PasswordHash))
        {
            Settings.Current.FailedLogins = Math.Min(100, Settings.Current.FailedLogins + 1);
            Settings.Save();
            if (user is not null) await Db.SetFailedAttempts(user.Id, false);
            throw new InvalidOperationException("Неверный логин или пароль.");
        }
        await Db.SetFailedAttempts(user.Id, true);
        Settings.Current.FailedLogins = 0;
        Settings.Save();
        return user;
    }
}
