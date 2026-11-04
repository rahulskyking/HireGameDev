using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M0_4_ReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "citext", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    state_code = table.Column<string>(type: "text", nullable: false),
                    country_code = table.Column<string>(type: "char(2)", nullable: false, defaultValueSql: "'IN'"),
                    metro_group = table.Column<string>(type: "text", nullable: true),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    is_hub = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false"),
                    sort_order = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "100"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cities", x => x.id);
                    table.CheckConstraint("ck_cities_country_code_len", "char_length(country_code) = 2");
                });

            migrationBuilder.CreateTable(
                name: "skills",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "citext", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    subcategory = table.Column<string>(type: "text", nullable: true),
                    parent_skill_id = table.Column<int>(type: "integer", nullable: true),
                    aliases = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    idf_weight = table.Column<decimal>(type: "numeric(4,2)", nullable: false, defaultValueSql: "1.00"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skills", x => x.id);
                    table.CheckConstraint("ck_skills_category", "category IN ('engine', 'language', 'art_tool', 'design_audio_tool', 'devops', 'genre', 'platform', 'role')");
                    table.CheckConstraint("ck_skills_idf_weight", "idf_weight >= 0.50 AND idf_weight <= 2.00");
                    table.CheckConstraint("ck_skills_no_self_parent", "parent_skill_id IS NULL OR parent_skill_id <> id");
                    table.ForeignKey(
                        name: "fk_skills_skills_parent_skill_id",
                        column: x => x.parent_skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skill_relations",
                columns: table => new
                {
                    skill_id = table.Column<int>(type: "integer", nullable: false),
                    related_skill_id = table.Column<int>(type: "integer", nullable: false),
                    relation_type = table.Column<string>(type: "text", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(3,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skill_relations", x => new { x.skill_id, x.related_skill_id });
                    table.CheckConstraint("ck_skill_relations_not_self", "skill_id <> related_skill_id");
                    table.CheckConstraint("ck_skill_relations_relation_type", "relation_type IN ('implies', 'superset', 'peer_tool', 'transferable')");
                    table.CheckConstraint("ck_skill_relations_weight", "weight > 0 AND weight <= 1");
                    table.ForeignKey(
                        name: "fk_skill_relations_skills_related_skill_id",
                        column: x => x.related_skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_skill_relations_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cities_aliases_gin",
                table: "cities",
                column: "aliases")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_cities_hub_sort",
                table: "cities",
                columns: new[] { "is_hub", "sort_order" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ux_cities_name_state",
                table: "cities",
                columns: new[] { "name", "state" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cities_slug",
                table: "cities",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_skill_relations_related_skill_id",
                table: "skill_relations",
                column: "related_skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_skills_aliases_gin",
                table: "skills",
                column: "aliases")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_skills_category",
                table: "skills",
                column: "category",
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_skills_name_trgm",
                table: "skills",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_skills_parent_skill_id",
                table: "skills",
                column: "parent_skill_id");

            migrationBuilder.CreateIndex(
                name: "ux_skills_name",
                table: "skills",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_skills_slug",
                table: "skills",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cities");

            migrationBuilder.DropTable(
                name: "skill_relations");

            migrationBuilder.DropTable(
                name: "skills");
        }
    }
}
