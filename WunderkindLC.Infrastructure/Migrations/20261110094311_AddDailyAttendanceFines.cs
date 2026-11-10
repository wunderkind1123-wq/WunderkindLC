using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyAttendanceFines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeacherAttendanceFines_GroupId_LessonDate",
                table: "TeacherAttendanceFines");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "TeacherAttendanceFines",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "lesson");

            migrationBuilder.AddColumn<string>(
                name: "MissedGroups",
                table: "TeacherAttendanceFines",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceDailyDeadline",
                table: "CenterMeta",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "19:30");

            migrationBuilder.AddColumn<string>(
                name: "AttendanceFineMode",
                table: "CenterMeta",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "lesson");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAttendanceFines_GroupId_LessonDate",
                table: "TeacherAttendanceFines",
                columns: new[] { "GroupId", "LessonDate" },
                unique: true,
                filter: "\"Kind\" = 'lesson'");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAttendanceFines_TeacherId_LessonDate_Daily",
                table: "TeacherAttendanceFines",
                columns: new[] { "TeacherId", "LessonDate" },
                unique: true,
                filter: "\"Kind\" = 'daily'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeacherAttendanceFines_GroupId_LessonDate",
                table: "TeacherAttendanceFines");

            migrationBuilder.DropIndex(
                name: "IX_TeacherAttendanceFines_TeacherId_LessonDate_Daily",
                table: "TeacherAttendanceFines");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TeacherAttendanceFines");

            migrationBuilder.DropColumn(
                name: "MissedGroups",
                table: "TeacherAttendanceFines");

            migrationBuilder.DropColumn(
                name: "AttendanceDailyDeadline",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceFineMode",
                table: "CenterMeta");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAttendanceFines_GroupId_LessonDate",
                table: "TeacherAttendanceFines",
                columns: new[] { "GroupId", "LessonDate" },
                unique: true);
        }
    }
}
