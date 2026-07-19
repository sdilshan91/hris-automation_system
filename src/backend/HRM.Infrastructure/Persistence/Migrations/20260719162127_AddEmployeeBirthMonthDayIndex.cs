using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeBirthMonthDayIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "birth_month_day",
                table: "employees",
                type: "integer",
                nullable: true);

            // ISSUE-285(a): backfill existing rows so the recurring-birthday widget is correct immediately,
            // without waiting for each employee's next write to trigger EmployeeBirthMonthDayInterceptor. This
            // MUST stay identical to EmployeeBirthMonthDayInterceptor.ToMonthDayKey (month*100 + day; NULL when
            // date_of_birth is NULL — the NULL case falls out naturally since arithmetic on NULL yields NULL).
            migrationBuilder.Sql(
                "UPDATE employees " +
                "SET birth_month_day = (EXTRACT(MONTH FROM date_of_birth)::int * 100 + EXTRACT(DAY FROM date_of_birth)::int) " +
                "WHERE date_of_birth IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_birth_month_day",
                table: "employees",
                columns: new[] { "tenant_id", "birth_month_day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_employees_tenant_id_birth_month_day",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "birth_month_day",
                table: "employees");
        }
    }
}
