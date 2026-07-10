using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// US-PLT-002 — RLS increment 2a: create the <c>tenant_isolation</c> row-level-security policies for
    /// EVERY base table in <c>public</c> that carries a <c>tenant_id</c> column, but leave them DORMANT
    /// (no <c>ENABLE ROW LEVEL SECURITY</c>). A policy is inert until its table has RLS enabled, so this
    /// migration is enforcement-neutral even though <c>DbInitializer</c> auto-applies it on startup — the
    /// flip (ENABLE + FORCE, gated by <c>Rls:Enabled</c>) lands in a later increment.
    ///
    /// The SQL is a self-maintaining PL/pgSQL DO-block that discovers the target tables from
    /// <c>information_schema.columns</c> rather than hand-listing ~112 names, so a future tenant table
    /// picks up its policy automatically the next time the migration chain is (re-)applied to a fresh DB.
    ///
    /// Policy form (see IMPLEMENTATION-DESIGN §1):
    ///   • strict (tenant_id NOT NULL):
    ///       USING (tenant_id = current_setting('app.current_tenant', true)::uuid)
    ///       WITH CHECK (same)
    ///   • nullable (tenant_id IS NULLABLE — e.g. roles, audit_log): USING also admits NULL-tenant (system)
    ///     rows for READ, but WITH CHECK stays STRICT so an app session can never INSERT/relabel a
    ///     NULL/foreign-tenant row.
    ///
    /// <c>current_setting('app.current_tenant', true)</c> uses <c>missing_ok =&gt; true</c>: an unset GUC
    /// returns SQL NULL, and <c>tenant_id = NULL</c> is never true ⇒ fail-closed (an unscoped session on the
    /// non-bypass role sees nothing), the inverse of the EF query filter's unresolved =&gt; see-all. The value
    /// is additionally wrapped in <c>NULLIF(..., '')</c>: a <c>set_config(..., is_local =&gt; true)</c> GUC
    /// reverts to the EMPTY STRING (not NULL) once its transaction commits, so on a pooled connection a later
    /// unscoped statement would otherwise hit <c>''::uuid</c> (SQLSTATE 22P02). <c>NULLIF</c> collapses both
    /// the unset (NULL) and reset ('') cases to NULL ⇒ clean fail-closed-to-zero rather than an error.
    /// </summary>
    public partial class Platform_RlsPolicies_Dormant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent (IF NOT EXISTS on pg_policies) so it is safe to re-run. DORMANT: no ENABLE/FORCE.
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    r record;
                    v_using text;
                    v_check text := $q$tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                BEGIN
                    FOR r IN
                        SELECT c.table_name, c.is_nullable
                        FROM information_schema.columns c
                        JOIN information_schema.tables t
                          ON t.table_schema = c.table_schema
                         AND t.table_name  = c.table_name
                        WHERE c.table_schema = 'public'
                          AND c.column_name  = 'tenant_id'
                          AND t.table_type   = 'BASE TABLE'
                          -- 'users' is a GLOBAL identity table (login spans tenants; no query filter). Its
                          -- tenant_id is an unused nullable SHADOW column — policying it would fail-closed a
                          -- cross-tenant login and its strict WITH CHECK would block user creation on hrm_app.
                          -- Excluded here to match IMPLEMENTATION-DESIGN §1's {tenants, users} carve-out.
                          AND c.table_name NOT IN ('users', 'tenants')
                    LOOP
                        -- WITH CHECK is ALWAYS strict: an app session must only ever write its own tenant's rows,
                        -- even on a nullable-tenant table (a tenant must never mint a NULL/system row).
                        IF r.is_nullable = 'YES' THEN
                            v_using := $q$tenant_id IS NULL OR tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid$q$;
                        ELSE
                            v_using := v_check;
                        END IF;

                        IF NOT EXISTS (
                            SELECT 1 FROM pg_policies
                            WHERE schemaname = 'public'
                              AND tablename  = r.table_name
                              AND policyname = 'tenant_isolation'
                        ) THEN
                            EXECUTE format(
                                'CREATE POLICY tenant_isolation ON public.%I USING (%s) WITH CHECK (%s)',
                                r.table_name, v_using, v_check);
                        END IF;
                    END LOOP;
                END
                $do$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    r record;
                BEGIN
                    FOR r IN
                        SELECT c.table_name
                        FROM information_schema.columns c
                        JOIN information_schema.tables t
                          ON t.table_schema = c.table_schema
                         AND t.table_name  = c.table_name
                        WHERE c.table_schema = 'public'
                          AND c.column_name  = 'tenant_id'
                          AND t.table_type   = 'BASE TABLE'
                          AND c.table_name NOT IN ('users', 'tenants')
                    LOOP
                        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON public.%I', r.table_name);
                    END LOOP;
                END
                $do$;
                """);
        }
    }
}
