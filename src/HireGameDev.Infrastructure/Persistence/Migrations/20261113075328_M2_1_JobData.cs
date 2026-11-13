using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2_1_JobData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                table: "studio_profiles",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "suspended_by_user_id",
                table: "studio_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_reason",
                table: "studio_profiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    studio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    job_type = table.Column<string>(type: "text", nullable: true),
                    seniority_min = table.Column<string>(type: "text", nullable: true),
                    seniority_max = table.Column<string>(type: "text", nullable: true),
                    min_years = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    max_years = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    work_mode = table.Column<string>(type: "text", nullable: true),
                    city_id = table.Column<int>(type: "integer", nullable: true),
                    relocation_assistance = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    fresher_friendly = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    pay_unit = table.Column<string>(type: "text", nullable: true),
                    salary_min = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    max_notice_days = table.Column<short>(type: "smallint", nullable: true),
                    openings = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    screening_question = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    application_cap = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)50),
                    applications_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    first_published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    removed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removal_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    normalized_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    content_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.CheckConstraint("ck_jobs_capacity", "openings BETWEEN 1 AND 50 AND application_cap > 0 AND applications_count >= 0");
                    table.CheckConstraint("ck_jobs_category", "category IN ('programming','art','design','audio','qa','production')");
                    table.CheckConstraint("ck_jobs_closed", "(status <> 'closed' OR closed_at IS NOT NULL) AND (closed_at IS NULL OR status IN ('closed','removed'))");
                    table.CheckConstraint("ck_jobs_dates", "(expires_at IS NULL OR (published_at IS NOT NULL AND expires_at > published_at)) AND (published_at IS NULL OR (first_published_at IS NOT NULL AND published_at >= first_published_at))");
                    table.CheckConstraint("ck_jobs_fingerprint", "content_fingerprint IS NULL OR content_fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_jobs_job_type", "job_type IN ('full_time','contract','internship','part_time')");
                    table.CheckConstraint("ck_jobs_lengths", "(title IS NULL OR char_length(title) <= 100) AND (description IS NULL OR char_length(description) <= 8000) AND (screening_question IS NULL OR char_length(screening_question) <= 300)");
                    table.CheckConstraint("ck_jobs_notice", "max_notice_days IS NULL OR max_notice_days IN (0,15,30,45,60,90)");
                    table.CheckConstraint("ck_jobs_pay_unit", "pay_unit IN ('lpa','monthly_inr')");
                    table.CheckConstraint("ck_jobs_published", "status NOT IN ('active','paused','full','expired','removed') OR (first_published_at IS NOT NULL AND published_at IS NOT NULL AND expires_at IS NOT NULL)");
                    table.CheckConstraint("ck_jobs_ranges", "(min_years IS NULL OR min_years >= 0) AND (max_years IS NULL OR max_years >= 0) AND (min_years IS NULL OR max_years IS NULL OR max_years >= min_years) AND (salary_min IS NULL OR salary_min > 0) AND (salary_max IS NULL OR salary_max > 0) AND (salary_min IS NULL OR salary_max IS NULL OR salary_max >= salary_min)");
                    table.CheckConstraint("ck_jobs_ready", "status NOT IN ('active','paused','full','expired') OR (title IS NOT NULL AND char_length(btrim(title)) BETWEEN 5 AND 100 AND description IS NOT NULL AND char_length(btrim(description)) BETWEEN 100 AND 8000 AND category IS NOT NULL AND job_type IS NOT NULL AND seniority_min IS NOT NULL AND seniority_max IS NOT NULL AND min_years IS NOT NULL AND work_mode IS NOT NULL AND pay_unit IS NOT NULL AND salary_min IS NOT NULL AND salary_max IS NOT NULL AND normalized_title IS NOT NULL AND content_fingerprint IS NOT NULL AND ((work_mode = 'remote' AND city_id IS NULL) OR (work_mode IN ('onsite','hybrid') AND city_id IS NOT NULL)))");
                    table.CheckConstraint("ck_jobs_removed", "(status = 'removed' AND removed_at IS NOT NULL AND removed_by_user_id IS NOT NULL AND removal_reason IS NOT NULL AND char_length(btrim(removal_reason)) BETWEEN 1 AND 1000) OR (status <> 'removed' AND removed_at IS NULL AND removed_by_user_id IS NULL AND removal_reason IS NULL)");
                    table.CheckConstraint("ck_jobs_seniority_max", "seniority_max IN ('fresher','junior','mid','senior','lead')");
                    table.CheckConstraint("ck_jobs_seniority_min", "seniority_min IN ('fresher','junior','mid','senior','lead')");
                    table.CheckConstraint("ck_jobs_seniority_order", "seniority_min IS NULL OR seniority_max IS NULL OR array_position(ARRAY['fresher','junior','mid','senior','lead']::text[], seniority_min) <= array_position(ARRAY['fresher','junior','mid','senior','lead']::text[], seniority_max)");
                    table.CheckConstraint("ck_jobs_slug", "char_length(slug) BETWEEN 2 AND 160 AND slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.CheckConstraint("ck_jobs_status", "status IN ('draft','active','paused','full','expired','closed','removed')");
                    table.CheckConstraint("ck_jobs_work_mode", "work_mode IN ('onsite','hybrid','remote')");
                    table.ForeignKey(
                        name: "fk_jobs_cities_city_id",
                        column: x => x.city_id,
                        principalTable: "cities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_studio_profiles_studio_id",
                        column: x => x.studio_id,
                        principalTable: "studio_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_users_posted_by_user_id",
                        column: x => x.posted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_users_removed_by_user_id",
                        column: x => x.removed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_publication_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    studio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    normalized_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_publication_events", x => x.id);
                    table.CheckConstraint("ck_job_publication_fingerprint", "content_fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_job_publication_kind", "kind IN ('initial','renewal')");
                    table.CheckConstraint("ck_job_publication_title", "char_length(btrim(normalized_title)) BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_job_publication_events_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_publication_events_studio_profiles_studio_id",
                        column: x => x.studio_id,
                        principalTable: "studio_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_publication_events_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_skills",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    min_proficiency = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)2)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_skills", x => new { x.job_id, x.skill_id });
                    table.CheckConstraint("ck_job_skills_proficiency", "min_proficiency BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_job_skills_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "text", nullable: true),
                    new_status = table.Column<string>(type: "text", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_status_history", x => x.id);
                    table.CheckConstraint("ck_job_history_actor", "(actor_type = 'user' AND actor_user_id IS NOT NULL) OR (actor_type = 'system' AND actor_user_id IS NULL)");
                    table.CheckConstraint("ck_job_history_new", "new_status IN ('draft','active','paused','full','expired','closed','removed')");
                    table.CheckConstraint("ck_job_history_previous", "previous_status IS NULL OR previous_status IN ('draft','active','paused','full','expired','closed','removed')");
                    table.CheckConstraint("ck_job_history_reason_code", "char_length(btrim(reason_code)) BETWEEN 1 AND 80");
                    table.CheckConstraint("ck_job_history_removal", "new_status <> 'removed' OR (actor_type = 'user' AND reason IS NOT NULL AND char_length(btrim(reason)) BETWEEN 1 AND 1000)");
                    table.ForeignKey(
                        name: "fk_job_status_history_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_status_history_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_studio_profiles_suspended_by_user_id",
                table: "studio_profiles",
                column: "suspended_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_studio_profiles_suspension",
                table: "studio_profiles",
                sql: "(suspended_at IS NULL AND suspended_by_user_id IS NULL AND suspension_reason IS NULL) OR (suspended_at IS NOT NULL AND suspended_by_user_id IS NOT NULL AND suspension_reason IS NOT NULL AND char_length(btrim(suspension_reason)) BETWEEN 1 AND 1000)");

            migrationBuilder.CreateIndex(
                name: "ix_job_publication_events_actor_user_id",
                table: "job_publication_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_publications_repeat",
                table: "job_publication_events",
                columns: new[] { "studio_id", "content_fingerprint", "published_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_job_publications_studio_time",
                table: "job_publication_events",
                columns: new[] { "studio_id", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_job_publications_initial",
                table: "job_publication_events",
                column: "job_id",
                unique: true,
                filter: "kind = 'initial'");

            migrationBuilder.CreateIndex(
                name: "ix_job_skills_skill_job",
                table: "job_skills",
                columns: new[] { "skill_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_history_timeline",
                table: "job_status_history",
                columns: new[] { "job_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_status_history_actor_user_id",
                table: "job_status_history",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_city_id",
                table: "jobs",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_expiry",
                table: "jobs",
                column: "expires_at",
                filter: "status IN ('active','paused','full')");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_posted_by_user_id",
                table: "jobs",
                column: "posted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_removed_by_user_id",
                table: "jobs",
                column: "removed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_studio_fingerprint",
                table: "jobs",
                columns: new[] { "studio_id", "content_fingerprint" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_studio_status_updated",
                table: "jobs",
                columns: new[] { "studio_id", "status", "updated_at", "id" },
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_studio_title",
                table: "jobs",
                columns: new[] { "studio_id", "normalized_title" });

            migrationBuilder.CreateIndex(
                name: "ux_jobs_slug",
                table: "jobs",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_studio_profiles_users_suspended_by_user_id",
                table: "studio_profiles",
                column: "suspended_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_studio_profiles_users_suspended_by_user_id",
                table: "studio_profiles");

            migrationBuilder.DropTable(
                name: "job_publication_events");

            migrationBuilder.DropTable(
                name: "job_skills");

            migrationBuilder.DropTable(
                name: "job_status_history");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_studio_profiles_suspended_by_user_id",
                table: "studio_profiles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_studio_profiles_suspension",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "suspended_by_user_id",
                table: "studio_profiles");

            migrationBuilder.DropColumn(
                name: "suspension_reason",
                table: "studio_profiles");
        }
    }
}
