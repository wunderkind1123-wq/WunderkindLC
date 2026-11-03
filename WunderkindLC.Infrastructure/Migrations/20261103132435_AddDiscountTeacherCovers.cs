using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountTeacherCovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TeacherCoverHistory",
                table: "StudentDiscounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TeacherCoverPct",
                table: "StudentDiscounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TeacherCovers",
                table: "StudentDiscounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TeacherCoversFrom",
                table: "StudentDiscounts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeacherCoverHistory",
                table: "StudentDiscounts");

            migrationBuilder.DropColumn(
                name: "TeacherCoverPct",
                table: "StudentDiscounts");

            migrationBuilder.DropColumn(
                name: "TeacherCovers",
                table: "StudentDiscounts");

            migrationBuilder.DropColumn(
                name: "TeacherCoversFrom",
                table: "StudentDiscounts");
        }
    }
}
