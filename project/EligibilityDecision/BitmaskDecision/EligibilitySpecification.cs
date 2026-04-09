namespace Decisions.EligibilityDecision.BitmaskDecision
{
    /// <summary>
    /// Acceptance set for eligibility decisions.
    /// Each entry is a whitelisted combination of <see cref="EligibilityFact"/> bits.
    /// Add rows here when the business rules change — nowhere else.
    /// </summary>
    public static class EligibilitySpecification
    {
        public static Specification Build() => new Specification(new[]
        {
            // All seven facts satisfied — full eligibility
            (int)EligibilityFact.All,

            // Consent waived by regulation — otherwise fully eligible
            (int)(  EligibilityFact.IdentityVerified
                  | EligibilityFact.AgeEligible
                  | EligibilityFact.ResidencyConfirmed
                  | EligibilityFact.CreditworthinessOk
                  | EligibilityFact.DocumentsComplete
                  | EligibilityFact.NoExistingDisputes),

            // Minor applicant — age gate not applied, guardian consent covers it
            (int)(  EligibilityFact.IdentityVerified
                  | EligibilityFact.ResidencyConfirmed
                  | EligibilityFact.CreditworthinessOk
                  | EligibilityFact.DocumentsComplete
                  | EligibilityFact.NoExistingDisputes
                  | EligibilityFact.ConsentGiven),
        });

        public static BitmaskClassifier<System.Collections.Generic.Dictionary<string, object>> BuildClassifier()
            => new BitmaskClassifier<System.Collections.Generic.Dictionary<string, object>>(
                new EligibilityEncoder(),
                Build());
    }
}
