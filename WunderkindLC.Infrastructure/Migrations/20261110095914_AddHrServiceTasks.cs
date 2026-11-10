using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHrServiceTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "service_task_employee_id",
                table: "hr_settings",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trial_task_employee_id",
                table: "hr_settings",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_service_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    employee_id = table.Column<string>(type: "text", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    request_ids = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_service_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_service_tasks_hr_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "hr_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_service_tasks_task_id",
                table: "hr_service_tasks",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "uq_hr_service_task",
                table: "hr_service_tasks",
                columns: new[] { "employee_id", "day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_service_tasks");

            migrationBuilder.DropColumn(
                name: "service_task_employee_id",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "trial_task_employee_id",
                table: "hr_settings");
        }
    }
}
