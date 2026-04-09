using System.Collections.Generic;
using System.Linq;

namespace Decisions.EligibilityDecision.BitmaskDecision
{
    /// <summary>
    /// Specification pattern (Evans, DDD §9).
    /// Holds an acceptance set of valid bitvector encodings.
    /// </summary>
    public class Specification
    {
        private readonly HashSet<int> _acceptanceSet;

        public Specification(IEnumerable<int> acceptanceSet)
            => _acceptanceSet = acceptanceSet.ToHashSet();

        public bool IsSatisfiedBy(int encoding)
            => _acceptanceSet.Contains(encoding);
    }
}
