# Order Coffee (ASP.NET Core MVC + Razor + EF Core + PostgreSQL)

## Chạy

```
cd OrderCoffee
dotnet run --launch-profile http
```
Mở http://localhost:5190. App tự chạy migration và nạp dữ liệu mẫu khi khởi động.

**Database**: PostgreSQL `localhost:4455`, database `ordercoffee`, user `postgres`. Mật khẩu nằm trong user-secrets (không ghi vào file):
```
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=4455;Database=ordercoffee;Username=postgres;Password=<mật khẩu>"
```
Sau khi sửa model: `dotnet ef migrations add <Tên>` rồi `dotnet ef database update`.

## Tài khoản test (chỉ dùng khi phát triển)

| Vai trò | Đăng nhập | Mật khẩu |
|---|---|---|
| Admin | admin@coffee.vn | Admin@123 |
| Nhân viên | staff@coffee.vn | Staff@123 |
| Khách (150 điểm) | khach@coffee.vn | Khach@123 |

Voucher mẫu: `WELCOME10` (giảm 10%, 1 lần/người), `GIAM20K` (đơn từ 100K), đổi điểm: `DOI50`, `DOI100`, `DOI200`.

## Test luồng

**Order tại bàn (không cần đăng nhập)**
1. Đăng nhập Admin → Quản trị → *Bàn & QR* → bấm vào QR của một bàn (hoặc mở `/t/<mã bàn>`).
2. Chọn món → Giỏ hàng → *Gửi món cho Bàn*. Gọi thêm sẽ cộng vào cùng bill (Lần gọi #2…).
3. Ở trang *Hóa đơn bàn*: khách có thể đăng nhập → *Gắn bill vào tài khoản* → nhập voucher, rồi *Gọi thanh toán*.
4. Đăng nhập Nhân viên → *Sơ đồ bàn* (tự refresh 10s): bấm *Mang ra* từng món → *Xác nhận đã thanh toán* → bàn về Trống, khách được cộng điểm.

**Order online** (Khách đăng nhập): Menu → Giỏ → Thanh toán (lấy tại quán/giao hàng, voucher, tiền mặt/chuyển khoản) → theo dõi đơn.
Nhân viên → *Đơn online* → Nhận đơn → Pha chế → Sẵn sàng/Giao → Hoàn thành (cộng điểm).

**Điện thoại thật**: chạy `dotnet run --urls http://0.0.0.0:5190`, mở web bằng IP máy tính (vd. http://192.168.1.10:5190) rồi in lại QR ở trang Admin.

## Quy tắc
- 10.000đ thanh toán thực tế = 1 điểm, chỉ cộng khi đơn hoàn thành.
- Hủy đơn sẽ hoàn lại lượt voucher.
- Giá món được lấy lại từ DB lúc đặt; `OrderItem` lưu tên/giá tại thời điểm đặt.
