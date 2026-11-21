using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    public partial class CandidateSavedJobs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidate_saved_jobs",
                columns: table => new
                {
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_saved_jobs", x => new { x.candidate_id, x.job_id });
                    table.ForeignKey("fk_candidate_saved_jobs_candidate_profiles_candidate_id", x => x.candidate_id,
                        "candidate_profiles", "id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("fk_candidate_saved_jobs_jobs_job_id", x => x.job_id,
                        "jobs", "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("ix_candidate_saved_jobs_job_id", "candidate_saved_jobs", "job_id");
            migrationBuilder.CreateIndex("ix_candidate_saved_jobs_candidate_created_job", "candidate_saved_jobs",
                new[] { "candidate_id", "created_at", "job_id" }, descending: new[] { false, true, false });
        }

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("candidate_saved_jobs");
    }
}
