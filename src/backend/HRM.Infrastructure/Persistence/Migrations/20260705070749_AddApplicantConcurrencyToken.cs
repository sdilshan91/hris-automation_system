using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicantConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: `xmin` is a PostgreSQL SYSTEM column that already exists on every table.
            // The Applicant.RowVersion mapping (IsRowVersion -> xmin) only surfaces the existing
            // system column as an EF concurrency token — no DDL is required. This migration exists
            // solely to keep the model snapshot in sync (avoiding PendingModelChangesWarning at
            // startup). The scaffolder's ADD COLUMN xmin is intentionally omitted (it would fail /
            // is a no-op), matching the LeaveRequest xmin precedent (20260613181803_AddLeaveApprovalHistory).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — see Up(): the xmin system column is not owned by this migration.
        }
    }
}
