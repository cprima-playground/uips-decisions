using System;

namespace Decisions.EligibilityDecision.BitmaskDecision
{
    /// <summary>
    /// Atomic propositions for eligibility evaluation.
    /// Each member is a distinct bit; combinations are OR-composed.
    /// </summary>
    [Flags]
    public enum EligibilityFact
    {
        None                 = 0,
        IdentityVerified     = 1 << 0,   //   1
        AgeEligible          = 1 << 1,   //   2
        ResidencyConfirmed   = 1 << 2,   //   4
        CreditworthinessOk   = 1 << 3,   //   8
        DocumentsComplete    = 1 << 4,   //  16
        NoExistingDisputes   = 1 << 5,   //  32
        ConsentGiven         = 1 << 6,   //  64

        All                  = IdentityVerified | AgeEligible | ResidencyConfirmed
                             | CreditworthinessOk | DocumentsComplete
                             | NoExistingDisputes | ConsentGiven,   // 127
    }
}
