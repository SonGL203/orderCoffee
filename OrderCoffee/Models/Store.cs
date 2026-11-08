using System.ComponentModel.DataAnnotations;

namespace OrderCoffee.Models;

// Chi nhánh cửa hàng — dữ liệu lấy từ OpenStreetMap (Nominatim) qua StoreSyncService
public class Store
{
    public int Id { get; set; }
    [MaxLength(30)] public string OsmKey { get; set; } = "";   // vd "node/123456", dùng để cập nhật không trùng
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(500)] public string Address { get; set; } = "";
    [MaxLength(100)] public string City { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.Now;
}
