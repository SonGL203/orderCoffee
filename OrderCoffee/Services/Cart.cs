using System.Security.Claims;
using System.Text.Json;

namespace OrderCoffee.Services;

public class CartItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public int LineTotal => Price * Quantity;
}

public static class SessionExtensions
{
    private const string CartKey = "Cart";
    private const string TableKey = "TableCode";

    public static List<CartItem> GetCart(this ISession s)
    {
        var json = s.GetString(CartKey);
        return json == null ? new() : JsonSerializer.Deserialize<List<CartItem>>(json) ?? new();
    }

    public static void SaveCart(this ISession s, List<CartItem> cart) =>
        s.SetString(CartKey, JsonSerializer.Serialize(cart));

    public static void ClearCart(this ISession s) => s.Remove(CartKey);

    public static string? GetTableCode(this ISession s) => s.GetString(TableKey);
    public static void SetTableCode(this ISession s, string code) => s.SetString(TableKey, code);
    public static void ClearTableCode(this ISession s) => s.Remove(TableKey);

    // Hộp thoại "đăng nhập để được giảm giá": hỏi một lần cho mỗi lần quét QR
    private const string LoginAskedKey = "TableLoginAsked";
    public static bool WasAskedToLogin(this ISession s) => s.GetString(LoginAskedKey) != null;
    public static void SetAskedToLogin(this ISession s) => s.SetString(LoginAskedKey, "1");
    public static void ResetAskedToLogin(this ISession s) => s.Remove(LoginAskedKey);
}

public static class UserExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
