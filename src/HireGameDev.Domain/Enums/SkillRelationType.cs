namespace HireGameDev.Domain.Enums;

/// <summary>
/// Directional relation type for partial-credit and ecosystem implication edges (ADR-009).
/// Stored as snake_case text in PostgreSQL:
/// - implies (0.85–0.95): specialized child skill proves competency in parent/core skill (passes F2 >= 0.80 Engine Gate)
/// - superset (0.70–0.80): deeper skill covers lighter workflow in the same stack
/// - peer_tool (0.50–0.65): equivalent DCC, audio, or middleware tool with fast studio ramp-up
/// - transferable (0.30–0.45): cross-engine or adjacent language where fundamentals transfer
/// </summary>
public enum SkillRelationType
{
    Implies = 0,
    Superset = 1,
    PeerTool = 2,
    Transferable = 3
}
