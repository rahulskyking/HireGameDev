using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations;

public partial class ApplicationRetentionMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "consent_version", table: "applications", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "consented_at", table: "applications", type: "timestamptz", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "snapshot_purged_at", table: "applications", type: "timestamptz", nullable: true);
        // Legacy rows with no snapshot remain NULL; future cleanup must fail closed for those rows.
        migrationBuilder.Sql("""
            UPDATE applications a SET consent_version = s.consent_version, consented_at = s.consented_at
            FROM application_snapshots s WHERE s.application_id = a.id;
            """);
        migrationBuilder.AddCheckConstraint(name: "ck_applications_consent_receipt", table: "applications",
            sql: "(consent_version IS NULL AND consented_at IS NULL) OR (consent_version IS NOT NULL AND consented_at IS NOT NULL AND char_length(btrim(consent_version)) > 0)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This drops receipt and purge metadata; it cannot restore any previously deleted details.
        migrationBuilder.DropCheckConstraint(name: "ck_applications_consent_receipt", table: "applications");
        migrationBuilder.DropColumn(name: "snapshot_purged_at", table: "applications");
        migrationBuilder.DropColumn(name: "consented_at", table: "applications");
        migrationBuilder.DropColumn(name: "consent_version", table: "applications");
    }
}
