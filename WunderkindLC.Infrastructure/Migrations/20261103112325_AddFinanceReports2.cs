using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceReports2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceCategoryConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TypeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SystemCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CashFlowActivity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PnlTreatment = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    PnlGroup = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsAutoClassified = table.Column<bool>(type: "boolean", nullable: false),
                    AutoReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceCategoryConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannedExpenseAccruals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CorrectionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrectedBy = table.Column<string>(type: "text", nullable: true),
                    CorrectedAt = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedExpenseAccruals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannedExpenseTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TypeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartDate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    EndDate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReminderDay = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedExpenseTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCategoryConfigs_Key",
                table: "FinanceCategoryConfigs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExpenseAccruals_Month",
                table: "PlannedExpenseAccruals",
                column: "Month");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExpenseAccruals_TemplateId_Month",
                table: "PlannedExpenseAccruals",
                columns: new[] { "TemplateId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExpenseTemplates_IsDeleted_Status",
                table: "PlannedExpenseTemplates",
                columns: new[] { "IsDeleted", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceCategoryConfigs");

            migrationBuilder.DropTable(
                name: "PlannedExpenseAccruals");

            migrationBuilder.DropTable(
                name: "PlannedExpenseTemplates");
        }
    }
}
