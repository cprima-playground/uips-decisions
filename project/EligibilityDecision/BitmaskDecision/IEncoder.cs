namespace Decisions.EligibilityDecision.BitmaskDecision
{
    /// <summary>
    /// Encodes a domain value of type <typeparamref name="TSource"/>
    /// into an integer bitvector.
    /// </summary>
    public interface IEncoder<TSource>
    {
        int Encode(TSource source);
    }
}
