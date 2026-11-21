using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    public partial class ApplicationFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "applications", columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                job_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("pk_applications", x => x.id);
                table.CheckConstraint("ck_applications_status", "status IN ('submitted','reviewing','shortlisted','rejected','hired','withdrawn')");
                table.ForeignKey("fk_applications_candidate_profiles_candidate_id", x => x.candidate_id, "candidate_profiles", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_applications_jobs_job_id", x => x.job_id, "jobs", "id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateTable(name: "application_snapshots", columns: table => new
            {
                application_id = table.Column<Guid>(type: "uuid", nullable: false),
                snapshot_version = table.Column<int>(type: "integer", nullable: false),
                candidate_data = table.Column<string>(type: "jsonb", nullable: false),
                job_data = table.Column<string>(type: "jsonb", nullable: false),
                phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                consent_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                consented_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("pk_application_snapshots", x => x.application_id);
                table.CheckConstraint("ck_application_snapshots_version", "snapshot_version > 0");
                table.CheckConstraint("ck_application_snapshots_consent", "char_length(btrim(consent_version)) > 0");
                table.CheckConstraint("ck_application_snapshots_json", "jsonb_typeof(candidate_data) = 'object' AND jsonb_typeof(job_data) = 'object'");
                table.ForeignKey("fk_application_snapshots_applications_application_id", x => x.application_id, "applications", "id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateTable(name: "application_status_history", columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                application_id = table.Column<Guid>(type: "uuid", nullable: false),
                previous_status = table.Column<string>(type: "text", nullable: true),
                new_status = table.Column<string>(type: "text", nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("pk_application_status_history", x => x.id);
                table.CheckConstraint("ck_application_history_previous", "previous_status IS NULL OR previous_status IN ('submitted','reviewing','shortlisted','rejected','hired','withdrawn')");
                table.CheckConstraint("ck_application_history_new", "new_status IN ('submitted','reviewing','shortlisted','rejected','hired','withdrawn')");
                table.ForeignKey("fk_application_status_history_applications_application_id", x => x.application_id, "applications", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_application_status_history_users_actor_user_id", x => x.actor_user_id, "users", "id", onDelete: ReferentialAction.Restrict);
            });
            migrationBuilder.CreateIndex("ux_applications_candidate_job", "applications", new[] { "candidate_id", "job_id" }, unique: true);
            migrationBuilder.CreateIndex("ix_applications_candidate_submitted", "applications", new[] { "candidate_id", "submitted_at", "id" }, descending: new[] { false, true, false });
            migrationBuilder.CreateIndex("ix_applications_job_submitted", "applications", new[] { "job_id", "submitted_at", "id" }, descending: new[] { false, true, false });
            migrationBuilder.CreateIndex("ix_application_status_history_actor_user_id", "application_status_history", "actor_user_id");
            migrationBuilder.CreateIndex("ix_application_history_timeline", "application_status_history", new[] { "application_id", "occurred_at", "id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("application_status_history");
            migrationBuilder.DropTable("application_snapshots");
            migrationBuilder.DropTable("applications");
        }
    }
}
