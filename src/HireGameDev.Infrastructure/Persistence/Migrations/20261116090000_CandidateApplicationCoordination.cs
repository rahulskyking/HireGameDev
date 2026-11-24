using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireGameDev.Infrastructure.Persistence.Migrations;

public partial class CandidateApplicationCoordination : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Every mutation, including direct SQL and child inserts, must coordinate with the
        // Candidate row locked by submission. Updates that move a row lock both parents.
        migrationBuilder.Sql("""
            CREATE FUNCTION hgd_lock_candidate_for_application() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE old_id uuid; new_id uuid;
            BEGIN
              IF TG_OP <> 'INSERT' THEN old_id := OLD.candidate_id; END IF;
              IF TG_OP <> 'DELETE' THEN new_id := NEW.candidate_id; END IF;
              PERFORM id FROM candidate_profiles WHERE id = old_id OR id = new_id ORDER BY id FOR UPDATE;
              IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
              RETURN NEW;
            END $$;
            CREATE FUNCTION hgd_lock_portfolio_candidate_for_application() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE old_link uuid; new_link uuid;
            BEGIN
              IF TG_OP <> 'INSERT' THEN old_link := OLD.portfolio_link_id; END IF;
              IF TG_OP <> 'DELETE' THEN new_link := NEW.portfolio_link_id; END IF;
              -- Lock link rows before reading their owners, including for direct SQL updates.
              PERFORM id FROM portfolio_links WHERE id = old_link OR id = new_link ORDER BY id FOR UPDATE;
              PERFORM id FROM candidate_profiles WHERE id IN
                (SELECT candidate_id FROM portfolio_links WHERE id = old_link OR id = new_link)
                ORDER BY id FOR UPDATE;
              IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER hgd_application_candidate_lock BEFORE INSERT OR UPDATE OR DELETE ON candidate_skills FOR EACH ROW EXECUTE FUNCTION hgd_lock_candidate_for_application();
            CREATE TRIGGER hgd_application_candidate_lock BEFORE INSERT OR UPDATE OR DELETE ON candidate_experiences FOR EACH ROW EXECUTE FUNCTION hgd_lock_candidate_for_application();
            CREATE TRIGGER hgd_application_candidate_lock BEFORE INSERT OR UPDATE OR DELETE ON candidate_education FOR EACH ROW EXECUTE FUNCTION hgd_lock_candidate_for_application();
            CREATE TRIGGER hgd_application_candidate_lock BEFORE INSERT OR UPDATE OR DELETE ON shipped_titles FOR EACH ROW EXECUTE FUNCTION hgd_lock_candidate_for_application();
            CREATE TRIGGER hgd_application_candidate_lock BEFORE INSERT OR UPDATE OR DELETE ON portfolio_links FOR EACH ROW EXECUTE FUNCTION hgd_lock_candidate_for_application();
            CREATE TRIGGER hgd_application_candidate_lock BEFORE INSERT OR UPDATE OR DELETE ON candidate_studio_blocks FOR EACH ROW EXECUTE FUNCTION hgd_lock_candidate_for_application();
            CREATE TRIGGER hgd_application_candidate_lock BEFORE INSERT OR UPDATE OR DELETE ON portfolio_link_skills FOR EACH ROW EXECUTE FUNCTION hgd_lock_portfolio_candidate_for_application();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER hgd_application_candidate_lock ON portfolio_link_skills;
            DROP TRIGGER hgd_application_candidate_lock ON candidate_studio_blocks;
            DROP TRIGGER hgd_application_candidate_lock ON portfolio_links;
            DROP TRIGGER hgd_application_candidate_lock ON shipped_titles;
            DROP TRIGGER hgd_application_candidate_lock ON candidate_education;
            DROP TRIGGER hgd_application_candidate_lock ON candidate_experiences;
            DROP TRIGGER hgd_application_candidate_lock ON candidate_skills;
            DROP FUNCTION hgd_lock_portfolio_candidate_for_application();
            DROP FUNCTION hgd_lock_candidate_for_application();
            """);
    }
}
