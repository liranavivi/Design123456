namespace IronCondor.Models;

/// <summary>
/// Represents the four strike prices of an iron condor options strategy.
/// An iron condor consists of selling a call spread and a put spread simultaneously.
/// </summary>
public record IronCondorStrikes
{
    /// <summary>
    /// Strike price for the call option to sell (short call - closer to current price).
    /// </summary>
    public double CallSell { get; init; }

    /// <summary>
    /// Strike price for the call option to buy (long call - further from current price).
    /// </summary>
    public double CallBuy { get; init; }

    /// <summary>
    /// Strike price for the put option to sell (short put - closer to current price).
    /// </summary>
    public double PutSell { get; init; }

    /// <summary>
    /// Strike price for the put option to buy (long put - further from current price).
    /// </summary>
    public double PutBuy { get; init; }

    /// <summary>
    /// The trigger price that activates this iron condor position.
    /// </summary>
    public double TriggerPrice { get; init; }

    /// <summary>
    /// Creates a new IronCondorStrikes instance.
    /// </summary>
    public IronCondorStrikes()
    {
    }

    /// <summary>
    /// Creates a new IronCondorStrikes instance with specified values.
    /// </summary>
    public IronCondorStrikes(double callSell, double callBuy, double putSell, double putBuy, double triggerPrice)
    {
        CallSell = callSell;
        CallBuy = callBuy;
        PutSell = putSell;
        PutBuy = putBuy;
        TriggerPrice = triggerPrice;
    }
}

