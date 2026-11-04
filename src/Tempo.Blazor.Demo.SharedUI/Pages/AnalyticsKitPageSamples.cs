using System.Globalization;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// One order of the <c>/analytics-kit</c> demo data set.
/// </summary>
/// <param name="Id">Stable identity of the row. Demo data, never used as a DOM id.</param>
/// <param name="Date">The day the order was placed (the date the range filter reads).</param>
/// <param name="Status">Slug of the order state: <c>paid</c>, <c>refunded</c>, <c>open</c> or <c>cancelled</c>.</param>
/// <param name="Channel">Slug of the acquisition channel: <c>direct</c>, <c>search</c>, <c>social</c> or <c>email</c>.</param>
/// <param name="Region">Slug of the region: <c>emea</c>, <c>amer</c> or <c>apac</c>.</param>
/// <param name="Amount">The order value in the demo currency.</param>
internal sealed record KitOrder(
    string Id,
    DateOnly Date,
    string Status,
    string Channel,
    string Region,
    double Amount);

/// <summary>
/// The aggregate figures one window reports — what the KPI row and the trend list show.
/// </summary>
/// <param name="Revenue">Booked revenue: every order whose state is <c>paid</c> or <c>open</c>.</param>
/// <param name="OrderCount">How many orders the window contains, in any state.</param>
/// <param name="AverageOrderValue">Revenue divided by the number of booked orders (0 for an empty window).</param>
/// <param name="RefundRate">Refunded orders as a fraction of all orders in the window (0 for an empty window).</param>
internal sealed record KitTotals(double Revenue, int OrderCount, double AverageOrderValue, double RefundRate);

/// <summary>
/// The bucket size the analytics kit's chart and sparklines aggregate by.
/// </summary>
internal enum KitGranularity
{
    /// <summary>One point per day.</summary>
    Day,

    /// <summary>One point per seven days.</summary>
    Week,

    /// <summary>One point per thirty days.</summary>
    Month,
}

/// <summary>
/// The data and the arithmetic of the <c>/analytics-kit</c> demo page: a deterministic order history, the
/// window filters, the facet counts and the projections the page renders through
/// <c>TmStatCard</c>, <c>TmTrendList</c>, <c>TmBreakdownCard</c>, <c>TmChart</c>, <c>TmFacetFilter</c> and
/// <c>TmUsageMeter</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is demo DATA, not user-interface text: the same way a reporting demo ships a sample order table.
/// Every label the page puts on screen still resolves through <c>ITmLocalizer</c> — the projections below
/// take the caller's label delegate instead of hardcoding a caption.
/// </para>
/// <para>
/// The generator is seeded, so the same build always produces the same history and a screenshot test can
/// compare pixels. The channel mix DRIFTS over the history (direct is growing, search is shrinking), which
/// is what makes the current window and its comparison window rank the channels differently — the state
/// that gives <c>TmBreakdownCard</c>'s FLIP reorder something to do.
/// </para>
/// </remarks>
internal static class AnalyticsKitPageSamples
{
    /// <summary>How many orders the generated history holds.</summary>
    internal const int OrderCount = 720;

    /// <summary>How many days back the generated history reaches.</summary>
    internal const int HistoryDays = 360;

    /// <summary>The default window length of the page, in days.</summary>
    internal const int DefaultWindowDays = 30;

    /// <summary>The largest number of buckets the chart or a sparkline is cut into.</summary>
    private const int MaxBuckets = 24;

    /// <summary>The order states the facet groups offer, in display order.</summary>
    internal static IReadOnlyList<string> Statuses { get; } = ["paid", "refunded", "open", "cancelled"];

    /// <summary>The acquisition channels the facet groups offer, in display order.</summary>
    internal static IReadOnlyList<string> Channels { get; } = ["direct", "search", "social", "email"];

    /// <summary>The regions the facet groups offer, in display order.</summary>
    internal static IReadOnlyList<string> Regions { get; } = ["emea", "amer", "apac"];

    /// <summary>The generated order history, oldest first.</summary>
    internal static IReadOnlyList<KitOrder> Orders { get; } = BuildOrders();

    /// <summary>Today on the demo machine — the anchor the default window and the presets share.</summary>
    internal static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>The window the page opens with: the last <see cref="DefaultWindowDays"/> days, today included.</summary>
    internal static (DateOnly Start, DateOnly End) DefaultWindow()
        => (Today.AddDays(-(DefaultWindowDays - 1)), Today);

    /// <summary>
    /// Keeps only the orders a facet selection accepts: the groups combine with AND, the values inside one
    /// group with OR. A null or empty selection accepts everything, and a group key that matches no group is
    /// ignored here (the component still renders it as a chip).
    /// </summary>
    /// <param name="orders">The orders to filter.</param>
    /// <param name="selection">The selected values per group key, or <see langword="null"/> for no filter.</param>
    /// <returns>The matching orders, in their original order.</returns>
    internal static IReadOnlyList<KitOrder> Filter(
        IReadOnlyList<KitOrder> orders,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? selection)
    {
        if (selection is null || selection.Count == 0)
        {
            return orders;
        }

        return orders
            .Where(order => selection.All(pair => Matches(order, pair.Key, pair.Value)))
            .ToList();
    }

    /// <summary>Keeps only the orders placed inside an inclusive date window.</summary>
    /// <param name="orders">The orders to filter.</param>
    /// <param name="start">First day of the window.</param>
    /// <param name="end">Last day of the window.</param>
    /// <returns>The orders inside the window.</returns>
    internal static IReadOnlyList<KitOrder> InWindow(IReadOnlyList<KitOrder> orders, DateOnly start, DateOnly end)
        => orders.Where(order => order.Date >= start && order.Date <= end).ToList();

    /// <summary>Aggregates a window into the figures the KPI row and the trend list show.</summary>
    /// <param name="orders">The orders of ONE window (already date- and facet-filtered).</param>
    /// <returns>The window's totals.</returns>
    internal static KitTotals Totals(IReadOnlyList<KitOrder> orders)
    {
        var booked = orders.Where(IsBooked).ToList();
        var revenue = Math.Round(booked.Sum(order => order.Amount), 2);
        var refunded = orders.Count(order => order.Status == "refunded");
        return new KitTotals(
            Revenue: revenue,
            OrderCount: orders.Count,
            AverageOrderValue: booked.Count == 0 ? 0 : Math.Round(revenue / booked.Count, 2),
            RefundRate: orders.Count == 0 ? 0 : (double)refunded / orders.Count);
    }

    /// <summary>
    /// The relative change from a comparison window to the current one, in percent points — the number
    /// <c>TmStatCard.Delta</c> and <c>TrendItem.Delta</c> want.
    /// </summary>
    /// <param name="current">The current window's figure.</param>
    /// <param name="previous">The comparison window's figure.</param>
    /// <returns>The change, or <see langword="null"/> when there is nothing to compare against.</returns>
    internal static double? Delta(double current, double previous)
        => previous == 0 ? null : Math.Round((current - previous) / previous * 100, 1);

    /// <summary>The number of points a window of this length is cut into.</summary>
    /// <param name="start">First day of the window.</param>
    /// <param name="end">Last day of the window.</param>
    /// <param name="granularity">The bucket size.</param>
    /// <returns>The bucket count, between 1 and <see cref="MaxBuckets"/>.</returns>
    internal static int BucketCount(DateOnly start, DateOnly end, KitGranularity granularity)
    {
        var days = Math.Max(1, end.DayNumber - start.DayNumber + 1);
        var size = granularity switch
        {
            KitGranularity.Day => 1,
            KitGranularity.Week => 7,
            _ => 30,
        };

        return Math.Clamp((days + size - 1) / size, 1, MaxBuckets);
    }

    /// <summary>
    /// Projects one window into a series of <paramref name="buckets"/> values, applying
    /// <paramref name="aggregate"/> to the orders of each bucket. A bucket that receives no order reports 0,
    /// so every series of one window has the same length as the chart's labels.
    /// </summary>
    /// <param name="orders">The orders of the window.</param>
    /// <param name="start">First day of the window.</param>
    /// <param name="end">Last day of the window.</param>
    /// <param name="buckets">How many points the series has.</param>
    /// <param name="aggregate">The figure one bucket reports.</param>
    /// <returns>One value per bucket.</returns>
    internal static double[] BucketSeries(
        IReadOnlyList<KitOrder> orders,
        DateOnly start,
        DateOnly end,
        int buckets,
        Func<IReadOnlyList<KitOrder>, double> aggregate)
    {
        var days = Math.Max(1, end.DayNumber - start.DayNumber + 1);
        var slices = new List<KitOrder>[Math.Max(1, buckets)];
        for (var index = 0; index < slices.Length; index++)
        {
            slices[index] = [];
        }

        foreach (var order in orders)
        {
            if (order.Date < start || order.Date > end)
            {
                continue;
            }

            var offset = order.Date.DayNumber - start.DayNumber;
            var index = Math.Min(slices.Length - 1, (int)((long)offset * slices.Length / days));
            slices[index].Add(order);
        }

        return slices.Select(slice => aggregate(slice)).ToArray();
    }

    /// <summary>The revenue of one bucket slice — the default series of the chart and the sparklines.</summary>
    /// <param name="orders">The orders of the bucket.</param>
    /// <returns>The booked revenue of the bucket.</returns>
    internal static double RevenueOf(IReadOnlyList<KitOrder> orders)
        => Math.Round(orders.Where(IsBooked).Sum(order => order.Amount), 2);

    /// <summary>
    /// The trend rows of a window: revenue, orders, average order value and refund rate, each compared
    /// against the comparison window and each carrying a sparkline of the current window.
    /// </summary>
    /// <param name="current">The current window's orders.</param>
    /// <param name="previous">The comparison window's orders.</param>
    /// <param name="start">First day of the current window.</param>
    /// <param name="end">Last day of the current window.</param>
    /// <param name="buckets">How many points each sparkline has.</param>
    /// <param name="label">Resolves the localized caption of a metric key (<c>revenue</c>, <c>orders</c>, …).</param>
    /// <param name="culture">The culture the two percentage-handed rows format their value with.</param>
    /// <returns>One row per metric, in display order.</returns>
    internal static IReadOnlyList<TrendItem> BuildTrendItems(
        IReadOnlyList<KitOrder> current,
        IReadOnlyList<KitOrder> previous,
        DateOnly start,
        DateOnly end,
        int buckets,
        Func<string, string> label,
        CultureInfo culture)
    {
        var now = Totals(current);
        var before = Totals(previous);

        var revenue = BucketSeries(current, start, end, buckets, RevenueOf);
        var orders = BucketSeries(current, start, end, buckets, slice => slice.Count);
        var average = BucketSeries(current, start, end, buckets, slice =>
        {
            var sliceTotals = Totals(slice);
            return sliceTotals.AverageOrderValue;
        });
        var refunds = BucketSeries(current, start, end, buckets, slice =>
            Math.Round(Totals(slice).RefundRate * 100, 2));

        return
        [
            new TrendItem
            {
                Id = "revenue",
                Label = label("revenue"),
                NumericValue = now.Revenue,
                Delta = Delta(now.Revenue, before.Revenue),
                Sparkline = revenue,
            },
            new TrendItem
            {
                Id = "orders",
                Label = label("orders"),
                NumericValue = now.OrderCount,
                Delta = Delta(now.OrderCount, before.OrderCount),
                Sparkline = orders,
            },
            new TrendItem
            {
                Id = "average-order-value",
                Label = label("average-order-value"),
                NumericValue = now.AverageOrderValue,
                Delta = Delta(now.AverageOrderValue, before.AverageOrderValue),
                Sparkline = average,
            },
            new TrendItem
            {
                Id = "refund-rate",
                Label = label("refund-rate"),
                Value = (now.RefundRate * 100).ToString("N1", culture) + " %",
                Delta = Delta(now.RefundRate, before.RefundRate),
                PositiveIsGood = false,
                Sparkline = refunds,
            },
        ];
    }

    /// <summary>
    /// The channel split of one window's revenue, for <c>TmBreakdownCard</c>. The parts are intentionally
    /// ordered as the channel list is, because the card itself renders biggest-first and animates the
    /// reorder — a pre-sorted list would hide that behaviour.
    /// </summary>
    /// <param name="orders">The orders of the window.</param>
    /// <param name="label">Resolves the localized caption of a channel slug.</param>
    /// <returns>One part per channel that booked revenue in the window.</returns>
    internal static IReadOnlyList<BreakdownPart> BuildBreakdown(
        IReadOnlyList<KitOrder> orders,
        Func<string, string> label)
        => Channels
            .Select(channel => new BreakdownPart
            {
                Id = channel,
                Label = label(channel),
                Value = Math.Round(
                    orders.Where(order => order.Channel == channel && IsBooked(order)).Sum(order => order.Amount),
                    2),
            })
            .Where(part => part.Value > 0)
            .ToList();

    /// <summary>
    /// The comparison chart of one window: the current window's revenue per bucket next to the comparison
    /// window's revenue in the same buckets, so the two series line up point by point.
    /// </summary>
    /// <param name="current">The current window's orders.</param>
    /// <param name="previous">The comparison window's orders.</param>
    /// <param name="start">First day of the current window.</param>
    /// <param name="end">Last day of the current window.</param>
    /// <param name="previousStart">First day of the comparison window.</param>
    /// <param name="previousEnd">Last day of the comparison window.</param>
    /// <param name="granularity">The bucket size.</param>
    /// <param name="culture">The culture the bucket labels are formatted with.</param>
    /// <param name="currentLabel">The localized caption of the current series.</param>
    /// <param name="previousLabel">The localized caption of the comparison series.</param>
    /// <returns>The chart data plus the labels of the buckets.</returns>
    internal static ChartData BuildChart(
        IReadOnlyList<KitOrder> current,
        IReadOnlyList<KitOrder> previous,
        DateOnly start,
        DateOnly end,
        DateOnly previousStart,
        DateOnly previousEnd,
        KitGranularity granularity,
        CultureInfo culture,
        string currentLabel,
        string previousLabel)
    {
        var buckets = BucketCount(start, end, granularity);
        var days = Math.Max(1, end.DayNumber - start.DayNumber + 1);
        var step = (int)Math.Ceiling(days / (double)buckets);

        var labels = new string[buckets];
        for (var index = 0; index < buckets; index++)
        {
            var bucketStart = start.AddDays(index * step);
            labels[index] = bucketStart.ToString("d MMM", culture);
        }

        return new ChartData
        {
            Labels = labels,
            Datasets =
            [
                new ChartDataset
                {
                    Label = currentLabel,
                    Values = BucketSeries(current, start, end, buckets, RevenueOf),
                    Color = "#2563eb",
                },
                new ChartDataset
                {
                    Label = previousLabel,
                    Values = BucketSeries(previous, previousStart, previousEnd, buckets, RevenueOf),
                    Color = "#94a3b8",
                },
            ],
        };
    }

    /// <summary>
    /// The facet groups over the whole history, with HOST-computed counts: the count of an option is how many
    /// orders it would leave given the selection of the OTHER groups (the standard faceting rule), so the
    /// numbers move when a chip in another group is removed and never report zero for the value just picked.
    /// </summary>
    /// <param name="orders">The orders the counts are computed over.</param>
    /// <param name="selection">The current selection, or <see langword="null"/>.</param>
    /// <param name="groupLabel">Resolves the localized caption of a group key.</param>
    /// <param name="optionLabel">Resolves the localized caption of one option (group key, value slug).</param>
    /// <returns>The three groups, in display order.</returns>
    internal static IReadOnlyList<FacetGroup> BuildFacetGroups(
        IReadOnlyList<KitOrder> orders,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? selection,
        Func<string, string> groupLabel,
        Func<string, string, string> optionLabel)
    {
        var groups = new List<FacetGroup>(3);
        foreach (var (key, values) in new (string Key, IReadOnlyList<string> Values)[]
                 {
                     ("status", Statuses),
                     ("channel", Channels),
                     ("region", Regions),
                 })
        {
            var others = selection is null
                ? null
                : selection
                    .Where(pair => !string.Equals(pair.Key, key, StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var scoped = Filter(orders, others);

            groups.Add(new FacetGroup
            {
                Key = key,
                Label = groupLabel(key),
                IsCollapsed = key == "region",
                // The channel group is short enough to show "Show {n} more", and its four values are the one
                // list where the in-group search pays off; the other two groups get neither.
                MaxVisible = key == "channel" ? 3 : null,
                SearchThreshold = key == "channel" ? 3 : 0,
                Options = values
                    .Select(value => new FacetOption
                    {
                        Value = value,
                        Label = optionLabel(key, value),
                        Count = scoped.Count(order => ValueOf(order, key) == value),
                    })
                    .ToList(),
            });
        }

        return groups;
    }

    /// <summary>Whether one order books revenue: a paid order does, an open one is booked, refunded and cancelled do not.</summary>
    /// <param name="order">The order to test.</param>
    /// <returns><see langword="true"/> when the order counts towards revenue.</returns>
    internal static bool IsBooked(KitOrder order)
        => order.Status is "paid" or "open";

    /// <summary>Reads one order's value for a facet group key.</summary>
    /// <param name="order">The order to read.</param>
    /// <param name="groupKey">The group key (<c>status</c>, <c>channel</c> or <c>region</c>).</param>
    /// <returns>The order's value, or the empty string for an unknown key.</returns>
    private static string ValueOf(KitOrder order, string groupKey) => groupKey switch
    {
        "status" => order.Status,
        "channel" => order.Channel,
        "region" => order.Region,
        _ => string.Empty,
    };

    /// <summary>Whether an order satisfies one group's selected values (OR inside the set).</summary>
    private static bool Matches(KitOrder order, string groupKey, IReadOnlySet<string> values)
        => values.Count == 0 || groupKey switch
        {
            "status" or "channel" or "region" => values.Contains(ValueOf(order, groupKey)),
            // An unknown group key cannot be evaluated, so it must not exclude every order.
            _ => true,
        };

    /// <summary>
    /// Builds the deterministic order history: <see cref="OrderCount"/> orders spread evenly over
    /// <see cref="HistoryDays"/> days, with a seeded generator and a channel mix that drifts over time.
    /// </summary>
    /// <returns>The orders, oldest first.</returns>
    private static IReadOnlyList<KitOrder> BuildOrders()
    {
        var today = Today;
        var random = new Random(20261031);
        var orders = new List<KitOrder>(OrderCount);

        for (var index = 0; index < OrderCount; index++)
        {
            var daysAgo = (int)Math.Round((OrderCount - 1 - index) * (double)(HistoryDays - 1) / (OrderCount - 1));
            var drift = 1.0 - (daysAgo / (double)(HistoryDays - 1));
            orders.Add(new KitOrder(
                Id: "order-" + (index + 1).ToString("D4", CultureInfo.InvariantCulture),
                Date: today.AddDays(-daysAgo),
                Status: StatusFor(random, daysAgo),
                Channel: ChannelFor(random, drift),
                Region: Regions[random.Next(Regions.Count)],
                Amount: Math.Round(45 + (random.NextDouble() * 755), 2)));
        }

        return orders;
    }

    /// <summary>
    /// Picks the order state: recent orders can still be open, refunds and cancellations stay rare, and
    /// everything else is paid.
    /// </summary>
    private static string StatusFor(Random random, int daysAgo)
    {
        var roll = random.NextDouble();
        if (daysAgo <= 7 && roll < 0.20)
        {
            return "open";
        }

        return roll switch
        {
            < 0.12 => "refunded",
            < 0.17 => "cancelled",
            _ => "paid",
        };
    }

    /// <summary>
    /// Picks the acquisition channel from a mix that drifts over the history: direct grows, search shrinks,
    /// so the current window and the comparison window rank the channels differently.
    /// </summary>
    private static string ChannelFor(Random random, double drift)
    {
        (string Channel, double Weight)[] weights =
        [
            ("direct", 18 + (26 * drift)),
            ("search", 42 - (18 * drift)),
            ("social", 14 + (4 * drift)),
            ("email", 26 - (12 * drift)),
        ];

        var target = random.NextDouble() * weights.Sum(entry => entry.Weight);
        foreach (var (channel, weight) in weights)
        {
            target -= weight;
            if (target <= 0)
            {
                return channel;
            }
        }

        return weights[^1].Channel;
    }
}
