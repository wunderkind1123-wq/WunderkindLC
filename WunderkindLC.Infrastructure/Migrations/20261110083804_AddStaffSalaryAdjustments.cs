using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffSalaryAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffSalaryAdjustments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffSalaryAdjustments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffSalaryAdjustments_Month_UserId",
                table: "StaffSalaryAdjustments",
                columns: new[] { "Month", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffSalaryAdjustments");
        }
    }
}
