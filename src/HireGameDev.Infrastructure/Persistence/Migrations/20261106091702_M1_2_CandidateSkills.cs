using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_2_CandidateSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidate_skills",
                columns: table => new
                {
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false),
                    proficiency = table.Column<short>(type: "smallint", nullable: false),
                    years_used = table.Column<decimal>(type: "numeric(3,1)", nullable: true),
                    last_used_year = table.Column<short>(type: "smallint", nullable: true),
                    evidence_level = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0"),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_skills", x => new { x.candidate_id, x.skill_id });
                    table.CheckConstraint("ck_candidate_skills_evidence_level", "evidence_level IN (0, 1, 2)");
                    table.CheckConstraint("ck_candidate_skills_last_used_year", "last_used_year IS NULL OR (last_used_year BETWEEN 1990 AND 2100)");
                    table.CheckConstraint("ck_candidate_skills_proficiency", "proficiency BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_candidate_skills_years_used", "years_used IS NULL OR (years_used >= 0 AND years_used <= 50)");
                    table.ForeignKey(
                        name: "fk_candidate_skills_candidate_profiles_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidate_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_candidate_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_candskills_skill",
                table: "candidate_skills",
                columns: new[] { "skill_id", "proficiency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_skills");
        }
    }
}
