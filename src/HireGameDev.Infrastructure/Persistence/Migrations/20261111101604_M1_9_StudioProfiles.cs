using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_9_StudioProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "studio_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "citext", nullable: false),
                    logo_path = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    city_id = table.Column<int>(type: "integer", nullable: true),
                    size_band = table.Column<string>(type: "text", nullable: false),
                    studio_type = table.Column<string>(type: "text", nullable: false),
                    founded_year = table.Column<short>(type: "smallint", nullable: true),
                    culture_tags = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    gstin = table.Column<string>(type: "text", nullable: true),
                    company_email_domain = table.Column<string>(type: "text", nullable: true),
                    verification_status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'unverified'"),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verification_notes = table.Column<string>(type: "text", nullable: true),
                    apps_90d = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "0"),
                    response_rate_90d = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    median_first_response_hours = table.Column<decimal>(type: "numeric(6,1)", nullable: true),
                    metrics_updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_studio_profiles", x => x.id);
                    table.CheckConstraint("ck_studio_profiles_apps_90d", "apps_90d >= 0");
                    table.CheckConstraint("ck_studio_profiles_culture_tags", "cardinality(culture_tags) <= 6 AND culture_tags <@ ARRAY['remote_friendly', 'hybrid_work', 'flexible_hours', 'no_crunch', 'indie_spirit', 'original_ip', 'co_dev_partner', 'learning_budget', 'health_insurance', 'relocation_support', 'revenue_share', 'diverse_inclusive']::text[]");
                    table.CheckConstraint("ck_studio_profiles_description_len", "description IS NULL OR char_length(description) <= 3000");
                    table.CheckConstraint("ck_studio_profiles_founded_year", "founded_year IS NULL OR (founded_year BETWEEN 1950 AND 2100)");
                    table.CheckConstraint("ck_studio_profiles_gstin", "gstin IS NULL OR gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$'");
                    table.CheckConstraint("ck_studio_profiles_median_response_hours", "median_first_response_hours IS NULL OR median_first_response_hours >= 0");
                    table.CheckConstraint("ck_studio_profiles_name_len", "char_length(btrim(name)) BETWEEN 2 AND 100");
                    table.CheckConstraint("ck_studio_profiles_response_rate_90d", "response_rate_90d IS NULL OR (response_rate_90d >= 0 AND response_rate_90d <= 100)");
                    table.CheckConstraint("ck_studio_profiles_size_band", "size_band IN ('1-10', '11-50', '51-200', '200+')");
                    table.CheckConstraint("ck_studio_profiles_slug", "char_length(slug) BETWEEN 2 AND 100 AND slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.CheckConstraint("ck_studio_profiles_studio_type", "studio_type IN ('indie', 'game_studio', 'outsourcing', 'publisher', 'other')");
                    table.CheckConstraint("ck_studio_profiles_verification_notes_len", "verification_notes IS NULL OR char_length(verification_notes) <= 2000");
                    table.CheckConstraint("ck_studio_profiles_verification_status", "verification_status IN ('unverified', 'pending', 'verified', 'rejected')");
                    table.CheckConstraint("ck_studio_profiles_website", "website IS NULL OR (char_length(website) <= 500 AND website ~ '^https://[^/\\s:@]+\\.[^/\\s]+')");
                    table.ForeignKey(
                        name: "fk_studio_profiles_cities_city_id",
                        column: x => x.city_id,
                        principalTable: "cities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_studio_profiles_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_studio_profiles_users_verified_by",
                        column: x => x.verified_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "studio_members",
                columns: table => new
                {
                    studio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_role = table.Column<string>(type: "text", nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_studio_members", x => new { x.studio_id, x.user_id });
                    table.CheckConstraint("ck_studio_members_member_role", "member_role IN ('owner', 'recruiter', 'viewer')");
                    table.ForeignKey(
                        name: "fk_studio_members_studio_profiles_studio_id",
                        column: x => x.studio_id,
                        principalTable: "studio_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_studio_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "studio_skills",
                columns: table => new
                {
                    studio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_studio_skills", x => new { x.studio_id, x.skill_id });
                    table.ForeignKey(
                        name: "fk_studio_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_studio_skills_studio_profiles_studio_id",
                        column: x => x.studio_id,
                        principalTable: "studio_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "studio_titles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    studio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_name = table.Column<string>(type: "text", nullable: false),
                    release_year = table.Column<short>(type: "smallint", nullable: true),
                    platforms = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    store_url = table.Column<string>(type: "text", nullable: true),
                    igdb_id = table.Column<long>(type: "bigint", nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_studio_titles", x => x.id);
                    table.CheckConstraint("ck_studio_titles_game_name_len", "char_length(btrim(game_name)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_studio_titles_platforms", "cardinality(platforms) <= 5 AND platforms <@ ARRAY['pc', 'android', 'ios', 'console', 'web']::text[]");
                    table.CheckConstraint("ck_studio_titles_release_year", "release_year IS NULL OR (release_year BETWEEN 1980 AND 2100)");
                    table.CheckConstraint("ck_studio_titles_sort_order", "sort_order >= 0");
                    table.CheckConstraint("ck_studio_titles_store_url", "store_url IS NULL OR (char_length(store_url) <= 500 AND store_url ~ '^https://[^/\\s:@]+\\.[^/\\s]+')");
                    table.ForeignKey(
                        name: "fk_studio_titles_studio_profiles_studio_id",
                        column: x => x.studio_id,
                        principalTable: "studio_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_studio_members_user_id",
                table: "studio_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_studio_profiles_city_id",
                table: "studio_profiles",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_studio_profiles_owner_user_id",
                table: "studio_profiles",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_studio_profiles_verification_status",
                table: "studio_profiles",
                column: "verification_status",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_studio_profiles_verified_by",
                table: "studio_profiles",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "ux_studio_profiles_owner_active",
                table: "studio_profiles",
                column: "owner_user_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_studio_profiles_slug",
                table: "studio_profiles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_studio_skills_skill_id",
                table: "studio_skills",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_studio_titles_studio_sort",
                table: "studio_titles",
                columns: new[] { "studio_id", "sort_order", "release_year" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "studio_members");

            migrationBuilder.DropTable(
                name: "studio_skills");

            migrationBuilder.DropTable(
                name: "studio_titles");

            migrationBuilder.DropTable(
                name: "studio_profiles");
        }
    }
}
