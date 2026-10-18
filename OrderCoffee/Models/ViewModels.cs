using System.ComponentModel.DataAnnotations;

namespace OrderCoffee.Models;

public class LoginVm
{
    [Required(ErrorMessage = "Nhập email hoặc số điện thoại")]
    [Display(Name = "Email hoặc số điện thoại")]
    public string Login { get; set; } = "";

    [Required(ErrorMessage = "Nhập mật khẩu"), DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = "";

    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterVm
{
    [Required(ErrorMessage = "Nhập họ tên"), MaxLength(100), Display(Name = "Họ tên")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Nhập số điện thoại"), RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại gồm 10 số, bắt đầu bằng 0")]
    [Display(Name = "Số điện thoại")]
    public string Phone { get; set; } = "";

    [EmailAddress(ErrorMessage = "Email không hợp lệ"), Display(Name = "Email (không bắt buộc)")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Nhập mật khẩu"), MinLength(6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự"), DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "Mật khẩu nhập lại không khớp"), DataType(DataType.Password)]
    [Display(Name = "Nhập lại mật khẩu")]
    public string ConfirmPassword { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public class CheckoutVm
{
    [MaxLength(100), Display(Name = "Tên người nhận (không bắt buộc)")]
    public string? CustomerName { get; set; }

    [Required(ErrorMessage = "Nhập số điện thoại"), RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    [Display(Name = "Số điện thoại")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Nhập địa chỉ giao hàng"), MaxLength(200), Display(Name = "Địa chỉ giao hàng")]
    public string Address { get; set; } = "";

    [MaxLength(300), Display(Name = "Ghi chú cho quán")]
    public string? Note { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? VoucherCode { get; set; }
}
