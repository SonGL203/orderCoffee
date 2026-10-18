using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderCoffee.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Đơn cũ chưa có mã tra cứu -> sinh mã ngẫu nhiên 12 ký tự trước khi tạo chỉ mục unique
            migrationBuilder.Sql("UPDATE \"Orders\" SET \"Code\" = substr(md5(random()::text || \"Id\"::text), 1, 12) WHERE \"Code\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Code",
                table: "Orders",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_Code",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Orders");
        }
    }
}
