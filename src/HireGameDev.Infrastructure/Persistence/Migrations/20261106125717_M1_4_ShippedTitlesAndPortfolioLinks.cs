using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_4_ShippedTitlesAndPortfolioLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "portfolio_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_type = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_links", x => x.id);
                    table.CheckConstraint("ck_portfolio_links_link_type", "link_type IN ('github', 'itchio', 'artstation', 'behance', 'youtube', 'website', 'linkedin', 'other')");
                    table.CheckConstraint("ck_portfolio_links_sort_order", "sort_order >= 0");
                    table.CheckConstraint("ck_portfolio_links_title_len", "title IS NULL OR char_length(title) <= 160");
                    table.CheckConstraint("ck_portfolio_links_url", "char_length(url) <= 500 AND url ~ '^https://[^/\\s:@]+\\.[^/\\s]+'");
                    table.ForeignKey(
                        name: "fk_portfolio_links_candidate_profiles_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidate_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipped_titles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_name = table.Column<string>(type: "text", nullable: false),
                    studio_name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    platforms = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    engine_skill_id = table.Column<int>(type: "integer", nullable: true),
                    release_year = table.Column<short>(type: "smallint", nullable: true),
                    store_url = table.Column<string>(type: "text", nullable: true),
                    igdb_id = table.Column<long>(type: "bigint", nullable: true),
                    verification = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'none'"),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    is_nda = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    hide_studio_in_public = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipped_titles", x => x.id);
                    table.CheckConstraint("ck_shipped_titles_game_name_len", "char_length(btrim(game_name)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_shipped_titles_nda_studio_hide", "NOT hide_studio_in_public OR is_nda");
                    table.CheckConstraint("ck_shipped_titles_platforms", "cardinality(platforms) <= 5 AND platforms <@ ARRAY['pc', 'android', 'ios', 'console', 'web']::text[]");
                    table.CheckConstraint("ck_shipped_titles_release_year", "release_year IS NULL OR (release_year BETWEEN 1990 AND 2100)");
                    table.CheckConstraint("ck_shipped_titles_role_len", "char_length(btrim(role)) BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_shipped_titles_sort_order", "sort_order >= 0");
                    table.CheckConstraint("ck_shipped_titles_store_url", "store_url IS NULL OR (char_length(store_url) <= 500 AND store_url ~ '^https://[^/\\s:@]+\\.[^/\\s]+')");
                    table.CheckConstraint("ck_shipped_titles_studio_name_len", "char_length(btrim(studio_name)) BETWEEN 1 AND 160");
                    table.CheckConstraint("ck_shipped_titles_verification", "verification IN ('none', 'title_verified', 'credit_verified')");
                    table.ForeignKey(
                        name: "fk_shipped_titles_candidate_profiles_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidate_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shipped_titles_skills_engine_skill_id",
                        column: x => x.engine_skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_link_skills",
                columns: table => new
                {
                    portfolio_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_link_skills", x => new { x.portfolio_link_id, x.skill_id });
                    table.ForeignKey(
                        name: "fk_portfolio_link_skills_portfolio_links_portfolio_link_id",
                        column: x => x.portfolio_link_id,
                        principalTable: "portfolio_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_portfolio_link_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_portfoliolinkskills_skill",
                table: "portfolio_link_skills",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_portfoliolinks_candidate_sort",
                table: "portfolio_links",
                columns: new[] { "candidate_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_shippedtitles_candidate_sort",
                table: "shipped_titles",
                columns: new[] { "candidate_id", "sort_order", "release_year" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_shippedtitles_engine_skill",
                table: "shipped_titles",
                column: "engine_skill_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "portfolio_link_skills");

            migrationBuilder.DropTable(
                name: "shipped_titles");

            migrationBuilder.DropTable(
                name: "portfolio_links");
        }
    }
}
