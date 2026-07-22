using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeavePoolAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "carry_forward_tracking_id",
                table: "leave_ledger",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pool",
                table: "leave_ledger",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "consumed_days",
                table: "leave_carry_forward_tracking",
                type: "numeric(7,2)",
                nullable: false,
                defaultValue: 0m);

            // DF-19 / ISSUE-045 backfill: seed the new persisted ConsumedDays counter for every existing
            // carry-forward bucket with the SAME MIN(carried, used-in-to-year) the derived BR-4 FIFO used
            // (LeaveCarryForwardCalculator.RemainingCarriedDays: consumedFromCarried = MIN(carried,
            // MAX(0, used-in-to-year))). This keeps existing buckets' expiry behavior byte-identical after
            // the expiry job switches from the derived sum to the persisted counter. Used entry types fold
            // in Encashed (both are consumption), matching GetUsedInYearAsync. Tenant-agnostic by design:
            // the correlated subquery is keyed on employee_id + leave_type_id + to_year, which are already
            // tenant-unique; this runs once at migration time (RLS is disabled during migration).
            migrationBuilder.Sql(@"
                UPDATE leave_carry_forward_tracking AS t
                SET consumed_days = LEAST(
                    t.carried_days,
                    GREATEST(0, -1 * COALESCE((
                        SELECT SUM(l.amount)
                        FROM leave_ledger l
                        WHERE l.employee_id = t.employee_id
                          AND l.leave_type_id = t.leave_type_id
                          AND l.leave_year = t.to_year
                          AND l.entry_type IN ('Used', 'Encashed')
                          AND l.is_deleted = false
                    ), 0))
                )
                WHERE t.is_deleted = false;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_leave_ledger_tenant_cf_tracking",
                table: "leave_ledger",
                columns: new[] { "tenant_id", "carry_forward_tracking_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leave_ledger_tenant_cf_tracking",
                table: "leave_ledger");

            migrationBuilder.DropColumn(
                name: "carry_forward_tracking_id",
                table: "leave_ledger");

            migrationBuilder.DropColumn(
                name: "pool",
                table: "leave_ledger");

            migrationBuilder.DropColumn(
                name: "consumed_days",
                table: "leave_carry_forward_tracking");
        }
    }
}
