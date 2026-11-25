using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadPrivateContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Leads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_IsPrivate_AssigneeUserId",
                table: "Leads",
                columns: new[] { "IsPrivate", "AssigneeUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_IsPrivate_AssigneeUserId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Leads");
        }
    }
}
