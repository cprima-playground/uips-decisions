using System;
using System.Collections.Generic;

namespace Decisions.EligibilityDecision.BitmaskDecision
{
    /// <summary>
    /// Maps SpecificContent keys to <see cref="EligibilityFact"/> bits.
    /// This is the only class that knows the SpecificContent key names.
    /// </summary>
    public class EligibilityEncoder : IEncoder<Dictionary<string, object>>
    {
        private static bool Flag(Dictionary<string, object> sc, string key)
            => sc.TryGetValue(key, out var v) && Convert.ToBoolean(v);

        public int Encode(Dictionary<string, object> sc)
        {
            var result = EligibilityFact.None;

            if (Flag(sc, "in_IdentityVerified"))   result |= EligibilityFact.IdentityVerified;
            if (Flag(sc, "in_AgeEligible"))         result |= EligibilityFact.AgeEligible;
            if (Flag(sc, "in_ResidencyConfirmed"))  result |= EligibilityFact.ResidencyConfirmed;
            if (Flag(sc, "in_CreditworthinessOk"))  result |= EligibilityFact.CreditworthinessOk;
            if (Flag(sc, "in_DocumentsComplete"))   result |= EligibilityFact.DocumentsComplete;
            if (Flag(sc, "in_NoExistingDisputes"))  result |= EligibilityFact.NoExistingDisputes;
            if (Flag(sc, "in_ConsentGiven"))        result |= EligibilityFact.ConsentGiven;

            return (int)result;
        }
    }
}
