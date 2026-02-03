using System.Diagnostics;
using System.Text;
using IronCondor.Infrastructure;
using IronCondor.Models;

namespace IronCondor.Services;

/// <summary>
/// Service for generating and displaying HTML charts.
/// </summary>
public class ChartService
{
    private readonly ILogger _logger;

    public ChartService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates an HTML chart for backtest results and opens it in the default browser.
    /// </summary>
    public void DisplayBackTestChart(BackTestResult result, string symbol)
    {
        try
        {
            var html = GenerateChartHtml(result, symbol);
            var tempPath = Path.Combine(Path.GetTempPath(), $"ironcondor_analysis_{symbol}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            
            File.WriteAllText(tempPath, html);
            _logger.LogInfo($"Chart saved to: {tempPath}");

            // Open in default browser
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            _logger.LogSuccess("Chart opened in browser. Review the data before making your selection.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to generate chart: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates HTML content with both bar chart and scatter plot.
    /// </summary>
    private string GenerateChartHtml(BackTestResult result, string symbol)
    {
        var labels = new StringBuilder();
        var data = new StringBuilder();
        var colors = new StringBuilder();

        bool isFirst = true;

        for (int i = 0; i < result.Percents.Count; i++)
        {
            int percentage = result.StartPercentage + i;
            bool isInteresting = result.InterestingPoints.Contains(percentage);
            bool isInterpolated = result.InterpolatedIndices.Contains(i);

            if (!isFirst)
            {
                labels.Append(", ");
                data.Append(", ");
                colors.Append(", ");
            }
            isFirst = false;

            labels.Append($"'{percentage}%'");
            data.Append($"{result.Percents[i]:F1}");

            // Color priority: Interesting (green) > Interpolated (orange) > Original (gray)
            string color;
            if (isInteresting)
                color = "'rgba(34, 197, 94, 0.8)'";  // Green for interesting points
            else if (isInterpolated)
                color = "'rgba(251, 146, 60, 0.7)'";  // Orange for interpolated values
            else
                color = "'rgba(156, 163, 175, 0.6)'";  // Gray for original values

            colors.Append(color);
        }

        // Generate scatter data with interesting/interpolated flags (convert to percentages)
        var scatterData = new StringBuilder();
        isFirst = true;
        for (int i = 0; i < result.ForwardSteps.Length; i++)
        {
            if (!isFirst) scatterData.Append(", ");
            isFirst = false;
            int pct = result.StartPercentage + i;
            bool isInteresting = result.InterestingPoints.Contains(pct);
            bool isInterpolated = result.InterpolatedIndices.Contains(i);
            // Convert relative values (0.0-1.0) to percentages (0-100)
            double forwardPct = result.ForwardSteps[i] * 100;
            double backwardPct = result.BackwardSteps[i] * 100;
            scatterData.Append($"{{x:{forwardPct:F1},y:{backwardPct:F1},pct:{pct},int:{(isInteresting ? 1 : 0)},interp:{(isInterpolated ? 1 : 0)}}}");
        }

        // Convert relative values to percentages for JavaScript arrays
        var forwardStepsJs = string.Join(",", result.ForwardSteps.Select(v => (v * 100).ToString("F1")));
        var backwardStepsJs = string.Join(",", result.BackwardSteps.Select(v => (v * 100).ToString("F1")));
        var interestingLabels = string.Join(", ", result.InterestingPoints.Select((p, i) => $"[{i + 1}] {p}%"));

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>IronCondor Analysis - {symbol}</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
               background: #1a1a2e; color: #eee; margin: 0; padding: 20px; }}
        .container {{ max-width: 1400px; margin: 0 auto; }}
        h1 {{ color: #4ade80; margin-bottom: 5px; }}
        .subtitle {{ color: #9ca3af; margin-bottom: 20px; }}
        .charts {{ display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }}
        .chart-container {{ background: #16213e; padding: 20px; border-radius: 8px; }}
        .chart-title {{ color: #4ade80; font-size: 14px; margin-bottom: 10px; }}
        .legend {{ display: flex; gap: 15px; margin-bottom: 10px; flex-wrap: wrap; }}
        .legend-item {{ display: flex; align-items: center; gap: 6px; font-size: 12px; }}
        .legend-box {{ width: 14px; height: 14px; border-radius: 3px; }}
        .green {{ background: rgba(34, 197, 94, 0.8); }}
        .orange {{ background: rgba(251, 146, 60, 0.7); }}
        .gray {{ background: rgba(156, 163, 175, 0.6); }}
        .info {{ background: #16213e; padding: 15px; border-radius: 8px; margin-bottom: 20px; }}
        .info-title {{ font-weight: bold; color: #4ade80; margin-bottom: 10px; }}
        @media (max-width: 1000px) {{ .charts {{ grid-template-columns: 1fr; }} }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>IronCondor Backtest Analysis</h1>
        <p class=""subtitle"">Symbol: {symbol} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>

        <div class=""info"">
            <div class=""info-title"">Detected Interesting Points:</div>
            <div>{interestingLabels}</div>
        </div>

        <div class=""charts"">
            <div class=""chart-container"">
                <div class=""chart-title"">Success Rate by Trigger Percentage</div>
                <div class=""legend"">
                    <div class=""legend-item""><div class=""legend-box green""></div> Interesting Points</div>
                    <div class=""legend-item""><div class=""legend-box orange""></div> Interpolated</div>
                    <div class=""legend-item""><div class=""legend-box gray""></div> Original</div>
                </div>
                <canvas id=""barChart""></canvas>
            </div>
            <div class=""chart-container"">
                <div class=""chart-title"">Isolation Distance Scatter (Forward vs Backward)</div>
                <div class=""legend"">
                    <div class=""legend-item""><div class=""legend-box green""></div> Interesting Points</div>
                    <div class=""legend-item""><div class=""legend-box orange""></div> Interpolated</div>
                    <div class=""legend-item""><div class=""legend-box gray""></div> Original</div>
                </div>
                <canvas id=""scatterChart""></canvas>
            </div>
        </div>
    </div>
    <script>
        const forwardSteps = [{forwardStepsJs}];
        const backwardSteps = [{backwardStepsJs}];
        const startPct = {result.StartPercentage};

        // Bar Chart
        new Chart(document.getElementById('barChart'), {{
            type: 'bar',
            data: {{
                labels: [{labels}],
                datasets: [{{
                    label: 'Success Rate (%)',
                    data: [{data}],
                    backgroundColor: [{colors}],
                    borderWidth: 1
                }}]
            }},
            options: {{
                responsive: true,
                plugins: {{ legend: {{ display: false }} }},
                scales: {{
                    y: {{ beginAtZero: true, max: 100, ticks: {{ color: '#9ca3af' }}, grid: {{ color: '#333' }} }},
                    x: {{ ticks: {{ color: '#9ca3af', maxRotation: 90, font: {{ size: 8 }} }}, grid: {{ color: '#333' }} }}
                }}
            }}
        }});

        // Scatter Chart
        const scatterData = [{scatterData}];
        new Chart(document.getElementById('scatterChart'), {{
            type: 'scatter',
            data: {{
                datasets: [{{
                    label: 'Isolation',
                    data: scatterData,
                    backgroundColor: scatterData.map(d => {{
                        if (d.int === 1) return 'rgba(34, 197, 94, 0.8)';
                        if (d.interp === 1) return 'rgba(251, 146, 60, 0.7)';
                        return 'rgba(156, 163, 175, 0.6)';
                    }}),
                    pointRadius: 6,
                    pointHoverRadius: 10,
                    clip: false
                }}]
            }},
            options: {{
                responsive: true,
                layout: {{
                    padding: {{ top: 40 }}
                }},
                plugins: {{
                    legend: {{ display: false }},
                    tooltip: {{
                        callbacks: {{
                            label: (ctx) => {{
                                const d = ctx.raw;
                                return d.pct + '%: Fwd=' + d.x.toFixed(1) + '%, Bwd=' + d.y.toFixed(1) + '%';
                            }}
                        }}
                    }}
                }},
                scales: {{
                    x: {{ title: {{ display: true, text: 'Forward Isolation (%)', color: '#9ca3af' }},
                         ticks: {{ color: '#9ca3af', callback: (v) => v + '%' }}, grid: {{ color: '#333' }}, min: 0, max: 100 }},
                    y: {{ title: {{ display: true, text: 'Backward Isolation (%)', color: '#9ca3af' }},
                         ticks: {{ color: '#9ca3af', callback: (v) => v + '%' }}, grid: {{ color: '#333' }}, min: 0, max: 100 }}
                }}
            }}
        }});
    </script>
</body>
</html>";
    }
}

