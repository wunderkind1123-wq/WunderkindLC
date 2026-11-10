using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHrAfternoonList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<short[]>(
                name: "reminder_offsets",
                table: "hr_settings",
                type: "smallint[]",
                nullable: false,
                defaultValueSql: "'{30}'",
                oldClrType: typeof(short[]),
                oldType: "smallint[]",
                oldDefaultValueSql: "'{120,60,30}'");

            migrationBuilder.AddColumn<bool>(
                name: "afternoon_list_enabled",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "afternoon_time",
                table: "hr_settings",
                type: "time without time zone",
                nullable: false,
                defaultValueSql: "'16:00'");

            // Egasi (2026-11-10): «faqat yarim soat oldin eslatilsin» — mavjud sozlama qatori ham 30 daqiqaga
            // (keyin «Sozlamalar»da yana o'zgartirish mumkin).
            migrationBuilder.Sql("UPDATE hr_settings SET reminder_offsets = '{30}';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "afternoon_list_enabled",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "afternoon_time",
                table: "hr_settings");

            migrationBuilder.AlterColumn<short[]>(
                name: "reminder_offsets",
                table: "hr_settings",
                type: "smallint[]",
                nullable: false,
                defaultValueSql: "'{120,60,30}'",
                oldClrType: typeof(short[]),
                oldType: "smallint[]",
                oldDefaultValueSql: "'{30}'");
        }
    }
}
