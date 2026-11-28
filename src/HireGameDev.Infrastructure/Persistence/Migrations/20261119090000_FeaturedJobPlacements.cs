using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations;

// SQL-managed until this feature is moved into a mapped EF model.
[DbContext(typeof(AppDbContext))]
[Migration("20261119090000_FeaturedJobPlacements")]
public sealed class FeaturedJobPlacements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE featured_job_placements (
                job_id uuid PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE,
                starts_at timestamptz NOT NULL,
                ends_at timestamptz NOT NULL,
                CONSTRAINT ck_featured_job_placement_duration CHECK (ends_at > starts_at AND ends_at <= starts_at + interval '30 days')
            );
            CREATE INDEX ix_featured_job_placements_dates ON featured_job_placements (ends_at, job_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deletes all placement scheduling data; does not affect jobs or audits.
        migrationBuilder.Sql("DROP TABLE featured_job_placements;");
    }
}
