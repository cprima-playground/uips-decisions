namespace Decisions.EligibilityDecision.BitmaskDecision
{
    /// <summary>
    /// Composes an <see cref="IEncoder{TSource}"/> with a <see cref="Specification"/>
    /// to classify a domain value as accepted or rejected.
    /// </summary>
    public class BitmaskClassifier<TSource>
    {
        private readonly IEncoder<TSource> _encoder;
        private readonly Specification _specification;

        public BitmaskClassifier(IEncoder<TSource> encoder, Specification specification)
        {
            _encoder       = encoder;
            _specification = specification;
        }

        public bool Classify(TSource input)
            => _specification.IsSatisfiedBy(_encoder.Encode(input));
    }
}
