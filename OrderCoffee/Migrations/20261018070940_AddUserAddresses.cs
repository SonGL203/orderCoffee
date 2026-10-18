using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OrderCoffee.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ContactName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAddresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Điền sẵn sổ địa chỉ từ các đơn giao hàng cũ của khách đã đăng ký
            migrationBuilder.Sql(@"
                INSERT INTO ""UserAddresses"" (""UserId"", ""ContactName"", ""Phone"", ""Address"", ""UseCount"", ""LastUsedAt"", ""CreatedAt"")
                SELECT ""UserId"", MAX(""CustomerName""), ""Phone"", ""Address"", COUNT(*), MAX(""CreatedAt""), MIN(""CreatedAt"")
                FROM ""Orders""
                WHERE ""UserId"" IS NOT NULL AND ""Phone"" IS NOT NULL AND ""Address"" IS NOT NULL AND ""Address"" <> ''
                GROUP BY ""UserId"", ""Phone"", ""Address"";");

            migrationBuilder.CreateIndex(
                name: "IX_UserAddresses_UserId_Phone_Address",
                table: "UserAddresses",
                columns: new[] { "UserId", "Phone", "Address" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAddresses");
        }
    }
}
