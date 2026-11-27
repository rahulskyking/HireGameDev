using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations;

// SQL-managed tables: deliberately not part of the EF model until the transactional
// entitlement repository is implemented. This migration does not activate Premium.
[DbContext(typeof(AppDbContext))]
[Migration("20261118090000_PlanEntitlementFoundation")]
public sealed class PlanEntitlementFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE plan_definitions (
                audience text NOT NULL,
                code text NOT NULL,
                display_name text NOT NULL,
                quota_limit integer NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (audience, code),
                CONSTRAINT ck_plan_audience CHECK (audience IN ('candidate', 'studio')),
                CONSTRAINT ck_plan_code CHECK (code IN ('free', 'premium')),
                CONSTRAINT ck_plan_name CHECK (char_length(btrim(display_name)) BETWEEN 1 AND 100),
                CONSTRAINT ck_plan_limit CHECK (quota_limit BETWEEN 1 AND 10000)
            );
            INSERT INTO plan_definitions (audience, code, display_name, quota_limit) VALUES
                ('studio', 'free', 'Free', 3), ('studio', 'premium', 'Premium', 10),
                ('candidate', 'free', 'Free', 10), ('candidate', 'premium', 'Premium', 30);

            CREATE TABLE candidate_plan_assignments (
                candidate_id uuid PRIMARY KEY REFERENCES candidate_profiles(id) ON DELETE CASCADE,
                audience text NOT NULL DEFAULT 'candidate' CHECK (audience = 'candidate'),
                plan_code text NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT now(),
                FOREIGN KEY (audience, plan_code) REFERENCES plan_definitions(audience, code) ON DELETE RESTRICT
            );
            CREATE INDEX ix_candidate_plan_assignments_plan ON candidate_plan_assignments(audience, plan_code);
            CREATE TABLE studio_plan_assignments (
                studio_id uuid PRIMARY KEY REFERENCES studio_profiles(id) ON DELETE CASCADE,
                audience text NOT NULL DEFAULT 'studio' CHECK (audience = 'studio'),
                plan_code text NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT now(),
                FOREIGN KEY (audience, plan_code) REFERENCES plan_definitions(audience, code) ON DELETE RESTRICT
            );
            CREATE INDEX ix_studio_plan_assignments_plan ON studio_plan_assignments(audience, plan_code);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rolling back permanently removes plan assignments and configured limits.
        migrationBuilder.Sql("""
            DROP TABLE studio_plan_assignments;
            DROP TABLE candidate_plan_assignments;
            DROP TABLE plan_definitions;
            """);
    }
}
