# Luồng test tự động cho các tính năng mới: cửa hàng gần nhất, yêu thích, đặt lại, đánh giá, món đề xuất.
# Cách chạy (app đang chạy ở BaseUrl, Postgres đang bật):
#   $env:PGPASSWORD = "<mật khẩu postgres>"
#   powershell -ExecutionPolicy Bypass -File scripts\test-flow.ps1
# Script tự tạo 1 tài khoản test mới (tên "Test Flow") mỗi lần chạy.
param(
    [string]$BaseUrl = "http://localhost:5190",
    [string]$PgHost = "localhost",
    [int]$PgPort = 4455,
    [string]$PgUser = "postgres",
    [string]$PgDb = "ordercoffee"
)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$script:pass = 0; $script:fail = 0
function Check($name, $ok, $detail = "") {
    if ($ok) { $script:pass++; Write-Host "  [PASS] $name" -ForegroundColor Green }
    else { $script:fail++; Write-Host "  [FAIL] $name $detail" -ForegroundColor Red }
}
function Token($html) { [regex]::Match($html, '__RequestVerificationToken[^>]*value="([^"]+)"').Groups[1].Value }
function Get-Page($path) { Invoke-WebRequest "$BaseUrl$path" -WebSession $s -UseBasicParsing }
function Post-Form($path, $fields, $tokenFrom) {
    $fields["__RequestVerificationToken"] = Token (Get-Page $tokenFrom).Content
    Invoke-WebRequest "$BaseUrl$path" -Method Post -Body $fields -WebSession $s -UseBasicParsing -MaximumRedirection 5
}
function Sql($q) { & psql -h $PgHost -p $PgPort -U $PgUser -d $PgDb -t -A -c ($q -replace '"', '\"') }

if (-not $env:PGPASSWORD) { throw "Hãy đặt `$env:PGPASSWORD trước khi chạy." }
if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    $pg = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" | Select-Object -Last 1
    if (-not $pg) { throw "Không tìm thấy psql." }
    $env:Path += ";" + $pg.DirectoryName
}

Write-Host "`n== 1. Cửa hàng gần nhất (công khai, không đăng nhập) =="
$anon = Invoke-WebRequest "$BaseUrl/stores" -UseBasicParsing
$storeCount = ([regex]::Matches($anon.Content, 'store-item"')).Count
Check "GET /stores trả 200" ($anon.StatusCode -eq 200)
Check "Có cửa hàng trong danh sách ($storeCount)" ($storeCount -gt 0)
Check "Có nút tìm gần tôi" ($anon.Content -match "btn-locate")
Check "Số cửa hàng trong DB khớp" ([int](Sql 'select count(*) from "Stores"') -eq $storeCount)

Write-Host "`n== 2. Đăng ký khách mới =="
$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$phone = "09" + (Get-Random -Minimum 10000000 -Maximum 99999999)
$r = Post-Form "/Account/Register" @{ FullName = "Test Flow"; Phone = $phone; Password = "Test1234!"; ConfirmPassword = "Test1234!" } "/Account/Register"
Check "Đăng ký xong, đã đăng nhập" ($r.Content -match "Test Flow")

Write-Host "`n== 3. Món yêu thích =="
$menu = Get-Page "/menu"
$pid1 = [regex]::Match($menu.Content, 'data-fav="(\d+)"').Groups[1].Value
Check "Menu có nút ♥ cho khách đăng nhập" ($pid1 -ne "")
$t = Token $menu.Content
$j = Invoke-RestMethod "$BaseUrl/favorites/toggle" -Method Post -Body @{ productId = $pid1; __RequestVerificationToken = $t } -WebSession $s -Headers @{ "X-Requested-With" = "XMLHttpRequest" }
Check "Bấm ♥ lần 1 => đã thích" ($j.fav -eq $true)
Check "Trang /favorites có món vừa thích" ((Get-Page "/favorites").Content -match "data-fav=`"$pid1`"")
$j = Invoke-RestMethod "$BaseUrl/favorites/toggle" -Method Post -Body @{ productId = $pid1; __RequestVerificationToken = $t } -WebSession $s -Headers @{ "X-Requested-With" = "XMLHttpRequest" }
Check "Bấm ♥ lần 2 => bỏ thích" ($j.fav -eq $false)
$null = Invoke-RestMethod "$BaseUrl/favorites/toggle" -Method Post -Body @{ productId = $pid1; __RequestVerificationToken = $t } -WebSession $s -Headers @{ "X-Requested-With" = "XMLHttpRequest" }
Check "Khách chưa đăng nhập không vào được /favorites" ((Invoke-WebRequest "$BaseUrl/favorites" -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue).StatusCode -in 302, $null)

Write-Host "`n== 4. Đặt hàng =="
$prodId = Sql 'select "Id" from "Products" where "IsAvailable" order by "Id" limit 1'
$null = Invoke-RestMethod "$BaseUrl/Cart/Add" -Method Post -Body @{ productId = $prodId; quantity = 2; __RequestVerificationToken = $t } -WebSession $s -Headers @{ "X-Requested-With" = "XMLHttpRequest" }
$r = Post-Form "/Orders/Checkout" @{ CustomerName = "Test Flow"; Phone = $phone; Address = "1 Test Street"; PaymentMethod = "Cash" } "/Orders/Checkout"
$code = [regex]::Match($r.BaseResponse.ResponseUri.AbsolutePath, "/Orders/Details/(\w+)").Groups[1].Value
Check "Đặt hàng thành công (mã $code)" ($code -ne "")
Check "Đơn chưa hoàn thành => chưa có form đánh giá" (-not ((Get-Page "/Orders/Details/$code").Content -match "Đánh giá món"))

Write-Host "`n== 5. Đánh giá món =="
$null = Sql "update ""Orders"" set ""Status""=7 where ""Code""='$code'"   # 7 = Completed (giả lập quán hoàn thành đơn)
$page = Get-Page "/Orders/Details/$code"
Check "Đơn hoàn thành => hiện form đánh giá" ($page.Content -match "Đánh giá món")
$null = Post-Form "/Orders/Review/$code" @{ productId = $prodId; rating = 5; comment = "Test flow: rất ngon" } "/Orders/Details/$code"
Check "Đánh giá đã lưu trong DB" ([int](Sql "select count(*) from ""Reviews"" r join ""Orders"" o on o.""Id""=r.""OrderId"" where o.""Code""='$code' and r.""Rating""=5") -eq 1)
$null = Post-Form "/Orders/Review/$code" @{ productId = $prodId; rating = 1; comment = "trùng" } "/Orders/Details/$code"
Check "Không đánh giá trùng 1 món trong 1 đơn" ([int](Sql "select count(*) from ""Reviews"" r join ""Orders"" o on o.""Id""=r.""OrderId"" where o.""Code""='$code'") -eq 1)
Check "Menu hiện điểm sao" ((Get-Page "/menu").Content -match "★ \d\.\d")

Write-Host "`n== 6. Món đề xuất ở trang chủ =="
$homeHtml = (Get-Page "/").Content
Check "Có mục 'Món được đề xuất'" ($homeHtml -match "Món được đề xuất")
$bestId = Sql 'select "ProductId" from "Reviews" group by "ProductId" order by avg("Rating") desc, count(*) desc limit 1'
$bestName = Sql "select ""Name"" from ""Products"" where ""Id""=$bestId"
$firstCard = [regex]::Match($homeHtml, 'đề xuất.*?<b>([^<]+)</b>', 'Singleline').Groups[1].Value
Check "Món sao cao nhất ($bestName) đứng đầu danh sách đề xuất" ([Net.WebUtility]::HtmlDecode($firstCard) -eq $bestName) "(đang là '$firstCard')"

Write-Host "`n== 7. Đặt lại đơn cũ =="
Check "Danh sách đơn có nút Đặt lại" ((Get-Page "/Orders").Content -match "Đặt lại đơn này")
$r = Post-Form "/Orders/Reorder/$code" @{} "/Orders"
Check "Đặt lại => giỏ hàng có món" ($r.Content -match "Giỏ hàng" -and $r.BaseResponse.ResponseUri.AbsolutePath -eq "/Cart")

Write-Host "`n== Dọn dữ liệu test =="
$null = Sql "delete from ""Orders"" where ""Code""='$code'"   # kéo theo xóa món + đánh giá của đơn
$null = Sql "delete from ""Users"" where ""Phone""='$phone'"
Write-Host "  Đã xóa tài khoản test $phone cùng đơn, đánh giá và yêu thích." -ForegroundColor DarkGray

Write-Host "`nKết quả: $script:pass đạt, $script:fail lỗi" -ForegroundColor $(if ($script:fail) { "Red" } else { "Green" })
exit $script:fail
