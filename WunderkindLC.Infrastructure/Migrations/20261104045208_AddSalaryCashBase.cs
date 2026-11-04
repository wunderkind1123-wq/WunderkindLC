using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryCashBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SalaryCashBaseFrom",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Egasining qarori (2026-11-04): oktabrdan boshlab foizli maosh «maoshga tushgan pul»dan. Sentabr va undan
            // oldingi oylar o'z bazasida qoladi (maosh berilgan). Yangi bazada (CenterMeta qatori yo'q) hech narsa qilmaydi.
            migrationBuilder.Sql("UPDATE \"CenterMeta\" SET \"SalaryCashBaseFrom\" = '2026-10';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalaryCashBaseFrom",
                table: "CenterMeta");
        }
    }
}
