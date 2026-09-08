using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ISSUE-523: retypes <c>employees.bank_account_number</c> from <c>varchar(50)</c> to <c>text</c> so it can
    /// hold the AES-256-GCM <c>enc:v1:</c> ciphertext written by the value converter wired in
    /// <c>EmployeeConfiguration.ApplyEncryption</c>. Mirrors <c>20260712185610_EncryptSensitiveFields</c> and
    /// <c>20260708055825_WidenMfaSecretForEncryption</c>.
    ///
    /// <para>Up is a NO-OP over data: the column has no write path anywhere in the codebase (the only assignment
    /// outside a test is none — see the ISSUE-523 finding), so it is structurally NULL in every tenant and the
    /// widening rewrites nothing. The startup back-fill
    /// (<c>DbInitializer.EncryptSensitiveFieldsAtRestAsync</c>) selects
    /// <c>WHERE bank_account_number IS NOT NULL AND ... NOT LIKE 'enc:v1:%'</c> and therefore matches zero rows.
    /// This is precisely why the change is made NOW rather than after a capture endpoint ships.</para>
    ///
    /// <para>⚠ <b>Down is destructive once data exists.</b> Narrowing back to <c>varchar(50)</c> cannot hold a
    /// ciphertext value (it is well over 50 chars), so rolling this migration back on a database that has
    /// ANY encrypted account number will fail or truncate. Decrypt the column before rolling back.</para>
    /// </summary>
    public partial class Employee_EncryptBankAccountNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "bank_account_number",
                table: "employees",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "bank_account_number",
                table: "employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
