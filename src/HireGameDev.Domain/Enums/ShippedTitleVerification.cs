namespace HireGameDev.Domain.Enums;

/// <summary>
/// Backend-controlled verification state for a shipped game credit (Blueprint v2.0 Section 5).
/// Candidates cannot self-assign TitleVerified or CreditVerified.
/// </summary>
public enum ShippedTitleVerification
{
    None = 0,
    TitleVerified = 1,
    CreditVerified = 2
}
