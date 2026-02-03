namespace IronCondor.Models;

/// <summary>
/// Represents the result of backtest analysis including success rates and identified peaks.
/// </summary>
public class BackTestResult
{
    /// <summary>
    /// The starting percentage value for the percents array.
    /// </summary>
    public int StartPercentage { get; set; }

    /// <summary>
    /// Array of success rates (actualChance) for each percentage from StartPercentage.
    /// Index 0 = StartPercentage, Index 1 = StartPercentage + 1, etc.
    /// Duplicate consecutive values are zeroed out (only first of plateau kept).
    /// </summary>
    public List<double> Percents { get; set; } = new();

    /// <summary>
    /// Forward isolation distance for each index (relative: steps / available forward range).
    /// Values are 0.0 to 1.0 representing percentage of available range.
    /// </summary>
    public double[] ForwardSteps { get; set; } = Array.Empty<double>();

    /// <summary>
    /// Backward isolation distance for each index (relative: steps / available backward range).
    /// Values are 0.0 to 1.0 representing percentage of available range.
    /// </summary>
    public double[] BackwardSteps { get; set; } = Array.Empty<double>();

    /// <summary>
    /// List of identified optimal trigger percentages (peaks).
    /// These are the actual percentage values (e.g., 15, 25, 45).
    /// </summary>
    public List<int> InterestingPoints { get; set; } = new();

    /// <summary>
    /// Set of indices that were interpolated (not original values).
    /// Used to display interpolated values in a different color on charts.
    /// </summary>
    public HashSet<int> InterpolatedIndices { get; set; } = new();
}

