using HireGameDev.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace HireGameDev.Infrastructure.Persistence.Migrations;

// Initial catalog. Selected Free values are used by Development enforcement;
// other values remain proposals and paid plans are not activated.
[DbContext(typeof(AppDbContext))]
[Migration("20261121090000_PlanCatalog")]
public sealed class PlanCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE plan_catalog_plans (
            audience text NOT NULL CHECK (audience IN ('candidate', 'studio')),
            code text NOT NULL,
            display_name text NOT NULL,
            enabled boolean NOT NULL DEFAULT false,
            PRIMARY KEY (audience, code)
        );
        INSERT INTO plan_catalog_plans (audience, code, display_name) VALUES
            ('candidate','free','Free'), ('candidate','plus','Plus'), ('candidate','pro','Pro'), ('candidate','elite','Elite'),
            ('studio','free','Free'), ('studio','plus','Plus'), ('studio','pro','Pro'), ('studio','elite','Elite');
        CREATE TABLE plan_catalog_features (
            audience text NOT NULL CHECK (audience IN ('candidate', 'studio')),
            feature text NOT NULL,
            label text NOT NULL,
            value_type text NOT NULL CHECK (value_type IN ('number', 'choice')),
            allows_unlimited boolean NOT NULL DEFAULT false,
            allowed_values text[] NOT NULL,
            PRIMARY KEY (audience, feature)
        );
        CREATE TABLE plan_catalog_values (
            audience text NOT NULL,
            plan_code text NOT NULL,
            feature text NOT NULL,
            value text NOT NULL,
            version bigint NOT NULL DEFAULT 1,
            PRIMARY KEY (audience, plan_code, feature),
            FOREIGN KEY (audience, plan_code) REFERENCES plan_catalog_plans(audience, code),
            FOREIGN KEY (audience, feature) REFERENCES plan_catalog_features(audience, feature)
        );
        INSERT INTO plan_catalog_features (audience, feature, label, value_type, allows_unlimited, allowed_values) VALUES
            ('candidate', 'searches', 'Daily filtered searches', 'number', false, ARRAY[]::text[]),
            ('candidate', 'applications', 'Applications per 30 days', 'number', false, ARRAY[]::text[]),
            ('candidate', 'saved_jobs', 'Saved jobs', 'number', true, ARRAY[]::text[]),
            ('candidate', 'match_score', 'Match score presentation', 'choice', false, ARRAY['basic', 'full']::text[]),
            ('candidate', 'match_insights', 'Match insights', 'choice', false, ARRAY['none', 'basic', 'advanced']::text[]),
            ('candidate', 'recommendations', 'Job recommendations', 'choice', false, ARRAY['basic', 'improved', 'smart', 'priority']::text[]),
            ('candidate', 'visibility', 'Profile visibility', 'choice', false, ARRAY['standard', 'boosted', 'priority']::text[]),
            ('candidate', 'analytics', 'Profile analytics', 'choice', false, ARRAY['none', 'basic', 'advanced']::text[]),
            ('candidate', 'portfolio', 'Portfolio presentation', 'choice', false, ARRAY['basic', 'enhanced', 'premium']::text[]),
            ('candidate', 'tracking', 'Application tracking', 'choice', false, ARRAY['basic', 'full']::text[]),
            ('studio', 'searches', 'Daily filtered candidate searches', 'number', false, ARRAY[]::text[]),
            ('studio', 'active_jobs', 'Occupied job slots', 'number', false, ARRAY[]::text[]),
            ('studio', 'postings', 'Job postings per 30 days', 'number', false, ARRAY[]::text[]),
            ('studio', 'drafts', 'Draft jobs', 'number', true, ARRAY[]::text[]),
            ('studio', 'seats', 'Team seats including owner', 'number', false, ARRAY[]::text[]),
            ('studio', 'discovery', 'Candidate discovery', 'choice', false, ARRAY['basic', 'advanced']::text[]),
            ('studio', 'match_score', 'Match score presentation', 'choice', false, ARRAY['basic', 'full']::text[]),
            ('studio', 'match_insights', 'Match insights', 'choice', false, ARRAY['none', 'basic', 'advanced']::text[]),
            ('studio', 'filters', 'Candidate filters', 'choice', false, ARRAY['basic', 'advanced', 'all']::text[]),
            ('studio', 'analytics', 'Recruitment analytics', 'choice', false, ARRAY['none', 'basic', 'advanced']::text[]),
            ('studio', 'visibility', 'Job visibility', 'choice', false, ARRAY['standard', 'boosted', 'priority']::text[]),
            ('studio', 'featured', 'Featured job slots', 'number', false, ARRAY[]::text[]);
        INSERT INTO plan_catalog_values (audience, plan_code, feature, value) VALUES
            ('candidate', 'free', 'searches', '20'),
            ('candidate', 'plus', 'searches', '50'),
            ('candidate', 'pro', 'searches', '150'),
            ('candidate', 'elite', 'searches', '500'),
            ('candidate', 'free', 'applications', '10'),
            ('candidate', 'plus', 'applications', '30'),
            ('candidate', 'pro', 'applications', '75'),
            ('candidate', 'elite', 'applications', '150'),
            ('candidate', 'free', 'saved_jobs', '25'),
            ('candidate', 'plus', 'saved_jobs', '100'),
            ('candidate', 'pro', 'saved_jobs', '500'),
            ('candidate', 'elite', 'saved_jobs', 'unlimited'),
            ('candidate', 'free', 'match_score', 'basic'),
            ('candidate', 'plus', 'match_score', 'full'),
            ('candidate', 'pro', 'match_score', 'full'),
            ('candidate', 'elite', 'match_score', 'full'),
            ('candidate', 'free', 'match_insights', 'none'),
            ('candidate', 'plus', 'match_insights', 'basic'),
            ('candidate', 'pro', 'match_insights', 'advanced'),
            ('candidate', 'elite', 'match_insights', 'advanced'),
            ('candidate', 'free', 'recommendations', 'basic'),
            ('candidate', 'plus', 'recommendations', 'improved'),
            ('candidate', 'pro', 'recommendations', 'smart'),
            ('candidate', 'elite', 'recommendations', 'priority'),
            ('candidate', 'free', 'visibility', 'standard'),
            ('candidate', 'plus', 'visibility', 'standard'),
            ('candidate', 'pro', 'visibility', 'boosted'),
            ('candidate', 'elite', 'visibility', 'priority'),
            ('candidate', 'free', 'analytics', 'none'),
            ('candidate', 'plus', 'analytics', 'none'),
            ('candidate', 'pro', 'analytics', 'basic'),
            ('candidate', 'elite', 'analytics', 'advanced'),
            ('candidate', 'free', 'portfolio', 'basic'),
            ('candidate', 'plus', 'portfolio', 'enhanced'),
            ('candidate', 'pro', 'portfolio', 'enhanced'),
            ('candidate', 'elite', 'portfolio', 'premium'),
            ('candidate', 'free', 'tracking', 'basic'),
            ('candidate', 'plus', 'tracking', 'full'),
            ('candidate', 'pro', 'tracking', 'full'),
            ('candidate', 'elite', 'tracking', 'full'),
            ('studio', 'free', 'searches', '15'),
            ('studio', 'plus', 'searches', '50'),
            ('studio', 'pro', 'searches', '200'),
            ('studio', 'elite', 'searches', '500'),
            ('studio', 'free', 'active_jobs', '3'),
            ('studio', 'plus', 'active_jobs', '5'),
            ('studio', 'pro', 'active_jobs', '15'),
            ('studio', 'elite', 'active_jobs', '30'),
            ('studio', 'free', 'postings', '5'),
            ('studio', 'plus', 'postings', '15'),
            ('studio', 'pro', 'postings', '50'),
            ('studio', 'elite', 'postings', '100'),
            ('studio', 'free', 'drafts', 'unlimited'),
            ('studio', 'plus', 'drafts', 'unlimited'),
            ('studio', 'pro', 'drafts', 'unlimited'),
            ('studio', 'elite', 'drafts', 'unlimited'),
            ('studio', 'free', 'seats', '1'),
            ('studio', 'plus', 'seats', '2'),
            ('studio', 'pro', 'seats', '5'),
            ('studio', 'elite', 'seats', '10'),
            ('studio', 'free', 'discovery', 'basic'),
            ('studio', 'plus', 'discovery', 'basic'),
            ('studio', 'pro', 'discovery', 'advanced'),
            ('studio', 'elite', 'discovery', 'advanced'),
            ('studio', 'free', 'match_score', 'basic'),
            ('studio', 'plus', 'match_score', 'full'),
            ('studio', 'pro', 'match_score', 'full'),
            ('studio', 'elite', 'match_score', 'full'),
            ('studio', 'free', 'match_insights', 'none'),
            ('studio', 'plus', 'match_insights', 'basic'),
            ('studio', 'pro', 'match_insights', 'advanced'),
            ('studio', 'elite', 'match_insights', 'advanced'),
            ('studio', 'free', 'filters', 'basic'),
            ('studio', 'plus', 'filters', 'basic'),
            ('studio', 'pro', 'filters', 'advanced'),
            ('studio', 'elite', 'filters', 'all'),
            ('studio', 'free', 'analytics', 'none'),
            ('studio', 'plus', 'analytics', 'basic'),
            ('studio', 'pro', 'analytics', 'advanced'),
            ('studio', 'elite', 'analytics', 'advanced'),
            ('studio', 'free', 'visibility', 'standard'),
            ('studio', 'plus', 'visibility', 'standard'),
            ('studio', 'pro', 'visibility', 'boosted'),
            ('studio', 'elite', 'visibility', 'priority'),
            ('studio', 'free', 'featured', '0'),
            ('studio', 'plus', 'featured', '0'),
            ('studio', 'pro', 'featured', '1'),
            ('studio', 'elite', 'featured', '3');
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE plan_catalog_values;
        DROP TABLE plan_catalog_features;
        DROP TABLE plan_catalog_plans;
        """);
}
