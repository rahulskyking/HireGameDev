using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1_8_ResumeParseLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resume_parse_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    input_chars = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume_parse_logs", x => x.id);
                    table.CheckConstraint("ck_resume_parse_logs_duration_ms", "duration_ms IS NULL OR duration_ms >= 0");
                    table.CheckConstraint("ck_resume_parse_logs_input_chars", "input_chars IS NULL OR input_chars >= 0");
                    table.CheckConstraint("ck_resume_parse_logs_source", "source IN ('pdf', 'linkedin_pdf')");
                    table.CheckConstraint("ck_resume_parse_logs_status", "status IN ('ok', 'failed', 'rejected')");
                    table.ForeignKey(
                        name: "fk_resume_parse_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parse_user_day",
                table: "resume_parse_logs",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resume_parse_logs");
        }
    }
}
