using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_3_CandidateExperienceEducation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidate_education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    institution = table.Column<string>(type: "text", nullable: false),
                    degree = table.Column<string>(type: "text", nullable: false),
                    field = table.Column<string>(type: "text", nullable: true),
                    start_year = table.Column<short>(type: "smallint", nullable: true),
                    end_year = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_education", x => x.id);
                    table.CheckConstraint("ck_candidate_education_degree_len", "char_length(btrim(degree)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_candidate_education_end_year", "end_year IS NULL OR (end_year BETWEEN 1950 AND 2100)");
                    table.CheckConstraint("ck_candidate_education_field_len", "field IS NULL OR char_length(field) <= 160");
                    table.CheckConstraint("ck_candidate_education_institution_len", "char_length(btrim(institution)) BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_candidate_education_start_year", "start_year IS NULL OR (start_year BETWEEN 1950 AND 2100)");
                    table.CheckConstraint("ck_candidate_education_year_order", "start_year IS NULL OR end_year IS NULL OR end_year >= start_year");
                    table.ForeignKey(
                        name: "fk_candidate_education_candidate_profiles_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidate_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "candidate_experiences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_experiences", x => x.id);
                    table.CheckConstraint("ck_candidate_experiences_company_name_len", "char_length(btrim(company_name)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_candidate_experiences_current_end_date", "NOT (is_current AND end_date IS NOT NULL)");
                    table.CheckConstraint("ck_candidate_experiences_date_order", "end_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_candidate_experiences_description_len", "description IS NULL OR char_length(description) <= 1500");
                    table.CheckConstraint("ck_candidate_experiences_title_len", "char_length(btrim(title)) BETWEEN 1 AND 160");
                    table.ForeignKey(
                        name: "fk_candidate_experiences_candidate_profiles_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidate_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_candedu_candidate_timeline",
                table: "candidate_education",
                columns: new[] { "candidate_id", "end_year" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_candexp_candidate_timeline",
                table: "candidate_experiences",
                columns: new[] { "candidate_id", "is_current", "start_date" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_education");

            migrationBuilder.DropTable(
                name: "candidate_experiences");
        }
    }
}
