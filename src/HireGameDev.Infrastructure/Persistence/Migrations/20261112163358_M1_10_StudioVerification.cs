using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_10_StudioVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_identity_gstin",
                table: "studio_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_identity_name",
                table: "studio_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_identity_requested_at",
                table: "studio_profiles",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_identity_slug",
                table: "studio_profiles",
                type: "citext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verification_applicant_notes",
                table: "studio_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "verification_proof_urls",
                table: "studio_profiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verification_submitted_at",
                table: "studio_profiles",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_studio_profiles_pending_identity_queue",
                table: "studio_profiles",
                column: "pending_identity_requested_at",
                filter: "deleted_at IS NULL AND pending_identity_requested_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_studio_profiles_verification_queue",
                table: "studio_profiles",
                columns: new[] { "verification_status", "verification_submitted_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_studio_profiles_pending_identity_slug",
                table: "studio_profiles",
                column: "pending_identity_slug",
                unique: true,
                filter: "deleted_at IS NULL AND pending_identity_slug IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_studio_profiles_pending_identity_consistency",
                table: "studio_profiles",
                sql: "(pending_identity_requested_at IS NULL AND pending_identity_name IS NULL AND pending_identity_slug IS NULL AND pending_identity_gstin IS NULL) OR (verification_status = 'verified' AND pending_identity_requested_at IS NOT NULL AND pending_identity_name IS NOT NULL AND char_length(btrim(pending_identity_name)) BETWEEN 2 AND 100 AND pending_identity_slug IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_studio_profiles_pending_identity_gstin",
                table: "studio_profiles",
                sql: "pending_identity_gstin IS NULL OR pending_identity_gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_studio_profiles_pending_identity_slug",
                table: "studio_profiles",
                sql: "pending_identity_slug IS NULL OR (char_length(pending_identity_slug) BETWEEN 2 AND 100 AND pending_identity_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_studio_profiles_verification_applicant_notes_len",
                table: "studio_profiles",
                sql: "verification_applicant_notes IS NULL OR char_length(verification_applicant_notes) <= 1000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_studio_profiles_verification_proof_urls",
                table: "studio_profiles",
                sql: "cardinality(verification_proof_urls) <= 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_studio_profiles_pending_identity_queue",
                table: "studio_profiles");

            migrationBuilder.DropIndex(
                name: "ix_studio_profiles_verification_queue",
                table: "studio_profiles");

            migrationBuilder.DropIndex(
                name: "ux_studio_profiles_pending_identity_slug",
                table: "studio_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_studio_profiles_pending_identity_consistency",
                table: "studio_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_studio_profiles_pending_identity_gstin",
                table: "studio_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_studio_profiles_pending_identity_slug",
                table: "studio_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_studio_profiles_verification_applicant_notes_len",
                table: "studio_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_studio_profiles_verification_proof_urls",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "pending_identity_gstin",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "pending_identity_name",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "pending_identity_requested_at",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "pending_identity_slug",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "verification_applicant_notes",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "verification_proof_urls",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "verification_submitted_at",
                table: "studio_profiles");
        }
    }
}
