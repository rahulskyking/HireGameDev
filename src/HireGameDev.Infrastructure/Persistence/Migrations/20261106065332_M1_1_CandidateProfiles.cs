using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_1_CandidateProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidate_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    headline = table.Column<string>(type: "text", nullable: false),
                    primary_category = table.Column<string>(type: "text", nullable: false),
                    photo_path = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    phone_verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    city_id = table.Column<int>(type: "integer", nullable: false),
                    seniority = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'fresher'"),
                    years_experience = table.Column<decimal>(type: "numeric(3,1)", nullable: false, defaultValueSql: "0.0"),
                    current_ctc_lpa = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    show_current_ctc = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    expected_ctc_lpa = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    notice_period_days = table.Column<short>(type: "smallint", nullable: true),
                    open_to_remote = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    willing_to_relocate = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    availability = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'actively_looking'"),
                    bio = table.Column<string>(type: "text", nullable: true),
                    completeness = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0"),
                    onboarding_step = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "1"),
                    is_discoverable = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    boosted_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_profiles", x => x.id);
                    table.CheckConstraint("ck_candidate_profiles_availability", "availability IN ('actively_looking', 'open_to_offers', 'not_looking')");
                    table.CheckConstraint("ck_candidate_profiles_bio_len", "bio IS NULL OR char_length(bio) <= 1500");
                    table.CheckConstraint("ck_candidate_profiles_completeness", "completeness >= 0 AND completeness <= 100");
                    table.CheckConstraint("ck_candidate_profiles_current_ctc_lpa", "current_ctc_lpa IS NULL OR current_ctc_lpa >= 0");
                    table.CheckConstraint("ck_candidate_profiles_expected_ctc_lpa", "expected_ctc_lpa IS NULL OR expected_ctc_lpa > 0");
                    table.CheckConstraint("ck_candidate_profiles_full_name_len", "char_length(btrim(full_name)) BETWEEN 2 AND 80");
                    table.CheckConstraint("ck_candidate_profiles_headline_len", "char_length(btrim(headline)) BETWEEN 3 AND 80");
                    table.CheckConstraint("ck_candidate_profiles_notice_period_days", "notice_period_days IS NULL OR notice_period_days IN (0, 15, 30, 45, 60, 90)");
                    table.CheckConstraint("ck_candidate_profiles_onboarding_step", "onboarding_step >= 1 AND onboarding_step <= 5");
                    table.CheckConstraint("ck_candidate_profiles_phone_e164", "phone IS NULL OR phone ~ '^\\+[1-9][0-9]{7,14}$'");
                    table.CheckConstraint("ck_candidate_profiles_primary_category", "primary_category IN ('programming', 'art', 'design', 'audio', 'qa', 'production')");
                    table.CheckConstraint("ck_candidate_profiles_seniority", "seniority IN ('fresher', 'junior', 'mid', 'senior', 'lead')");
                    table.CheckConstraint("ck_candidate_profiles_years_experience", "years_experience >= 0 AND years_experience <= 50");
                    table.ForeignKey(
                        name: "fk_candidate_profiles_cities_city_id",
                        column: x => x.city_id,
                        principalTable: "cities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_candidate_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cand_discover",
                table: "candidate_profiles",
                columns: new[] { "primary_category", "city_id", "seniority" },
                filter: "is_discoverable AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_candidate_profiles_city_id",
                table: "candidate_profiles",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ux_candidate_profiles_user_id",
                table: "candidate_profiles",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "candidate_profiles");
        }
    }
}
