using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace HireGameDev.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261120090000_SearchQuota")]
public sealed class SearchQuota : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE search_quota_settings (
            audience text PRIMARY KEY CHECK (audience IN ('candidate','studio')),
            daily_limit integer NULL CHECK (daily_limit BETWEEN 1 AND 10000)
        );
        INSERT INTO search_quota_settings (audience, daily_limit) VALUES ('candidate', NULL), ('studio', NULL);
        CREATE TABLE search_usage_events (
            sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            run_id uuid NOT NULL UNIQUE,
            intent_id uuid NOT NULL,
            audience text NOT NULL CHECK (audience IN ('candidate','studio')),
            candidate_user_id uuid NULL REFERENCES users(id) ON DELETE CASCADE,
            studio_id uuid NULL REFERENCES studio_profiles(id) ON DELETE CASCADE,
            actor_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            fingerprint char(64) NOT NULL CHECK (fingerprint ~ '^[0-9a-f]{64}$'),
            occurred_at timestamptz NOT NULL,
            UNIQUE (actor_id, intent_id),
            CHECK ((audience = 'candidate' AND candidate_user_id IS NOT NULL AND studio_id IS NULL) OR
                   (audience = 'studio' AND studio_id IS NOT NULL AND candidate_user_id IS NULL))
        );
        CREATE INDEX ix_search_usage_candidate ON search_usage_events(candidate_user_id, occurred_at) WHERE candidate_user_id IS NOT NULL;
        CREATE INDEX ix_search_usage_studio ON search_usage_events(studio_id, occurred_at) WHERE studio_id IS NOT NULL;
        CREATE INDEX ix_search_usage_actor_run ON search_usage_events(actor_id, audience, sequence DESC);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE search_usage_events; DROP TABLE search_quota_settings;");
}
