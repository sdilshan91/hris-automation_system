using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Performance_PipCheckpointObjectiveId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "objective_id",
                table: "pip_checkpoint",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pip_checkpoint_objective_id",
                table: "pip_checkpoint",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_pip_checkpoint_tenant_id_objective_id",
                table: "pip_checkpoint",
                columns: new[] { "tenant_id", "objective_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_pip_checkpoint_pip_objectives_objective_id",
                table: "pip_checkpoint",
                column: "objective_id",
                principalTable: "pip_objective",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pip_checkpoint_pip_objectives_objective_id",
                table: "pip_checkpoint");

            migrationBuilder.DropIndex(
                name: "ix_pip_checkpoint_objective_id",
                table: "pip_checkpoint");

            migrationBuilder.DropIndex(
                name: "ix_pip_checkpoint_tenant_id_objective_id",
                table: "pip_checkpoint");

            migrationBuilder.DropColumn(
                name: "objective_id",
                table: "pip_checkpoint");
        }
    }
}
