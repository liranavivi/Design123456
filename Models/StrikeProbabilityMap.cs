namespace IronCondor.Models;

/// <summary>
/// Represents a mapping between a strike price and its success probability.
/// </summary>
public class StrikeProbability
{
    /// <summary>
    /// Strike price.
    /// </summary>
    public double Strike { get; set; }

    /// <summary>
    /// Success probability percentage (0-100).
    /// </summary>
    public double Probability { get; set; }

    public StrikeProbability(double strike, double probability)
    {
        Strike = strike;
        Probability = probability;
    }
}

/// <summary>
/// A collection of strike price to probability mappings.
/// Used for storing the probability that the underlying price will stay below each strike.
/// </summary>
public class StrikeProbabilityMap : List<StrikeProbability>
{
    /// <summary>
    /// Adds a strike/probability pair to the map.
    /// </summary>
    /// <param name="strike">Strike price.</param>
    /// <param name="probability">Success probability percentage (0-100).</param>
    public void Add(double strike, double probability)
    {
        Add(new StrikeProbability(strike, probability));
    }
}

