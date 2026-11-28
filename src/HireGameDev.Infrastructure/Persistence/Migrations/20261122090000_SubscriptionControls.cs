using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace HireGameDev.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122090000_SubscriptionControls")]
public sealed class SubscriptionControls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE subscription_controls (id integer PRIMARY KEY CHECK (id = 1), premium_enabled boolean NOT NULL DEFAULT false);
        INSERT INTO subscription_controls (id, premium_enabled) VALUES (1, false);
        CREATE TABLE manual_studio_plans (
            studio_id uuid PRIMARY KEY REFERENCES studio_profiles(id) ON DELETE CASCADE,
            plan_code text NOT NULL CHECK (plan_code IN ('plus', 'pro', 'elite')),
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE manual_studio_plans; DROP TABLE subscription_controls;");
}
