using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Remodel_DateOnly_Columns : Migration
    {
        // DF-1: these 7 columns hold date-only values but were modelled as `timestamp with time zone`,
        // which forced the site-by-site SpecifyKind(.Date, Utc) band-aids (BUG-289/290). Remodel them to
        // real `date` columns (and their CLR properties to DateOnly), which makes that Kind bug class
        // impossible.
        //
        // The generated AlterColumn<> emits a bare `USING "col"::date`, which casts at the SESSION
        // timezone. Existing values were persisted as midnight-UTC instants, so a non-UTC session would
        // truncate them to the PREVIOUS calendar day. We therefore pin the conversion to UTC explicitly
        // with `AT TIME ZONE 'UTC'` so the stored UTC calendar date is preserved regardless of the
        // session/server timezone. The snapshot still reflects the `date`/DateOnly model change.

        private static readonly (string Table, string Column)[] Columns =
        {
            ("onboarding_task_instance", "due_date"),
            ("onboarding_checklist_instance", "start_date"),
            ("offboarding_task_instance", "due_date"),
            ("offboarding_instance", "last_working_day"),
            ("exit_interview", "interview_date"),
            ("asset", "purchase_date"),
            ("asset", "issue_date"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in Columns)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" TYPE date " +
                    $"USING (\"{column}\" AT TIME ZONE 'UTC')::date;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, column) in Columns)
            {
                // Restore the midnight-UTC instant: interpret the date at UTC, not the session TZ.
                migrationBuilder.Sql(
                    $"ALTER TABLE \"{table}\" ALTER COLUMN \"{column}\" TYPE timestamp with time zone " +
                    $"USING (\"{column}\"::timestamp AT TIME ZONE 'UTC');");
            }
        }
    }
}
