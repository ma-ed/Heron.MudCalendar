using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Utilities;

namespace Heron.MudCalendar;

/// <summary>
/// Displays a year overview with one row per month and events as horizontal bars.
/// </summary>
/// <typeparam name="T">The type of item displayed in this year view.</typeparam>
public partial class YearView<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> : CalendarViewBase<T> where T : CalendarItem
{
    private const int WeeksPerRow = 6;
    private const int DaysPerWeek = 7;
    private const int TotalColumns = WeeksPerRow * DaysPerWeek; // 42

    private const int DayHeaderHeightPx = 28;
    private const int SlotHeightPx = 22;
    private const int EventBarHeightPx = 20;

    /// <summary>
    /// The data for each of the 12 month rows.
    /// </summary>
    protected List<YearMonthData<T>> MonthRows { get; private set; } = [];

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        MonthRows = BuildMonthRows();
    }

    /// <summary>
    /// Not used by YearView — data is built via BuildMonthRows instead.
    /// </summary>
    protected override List<CalendarCell<T>> BuildCells() => [];

    private List<YearMonthData<T>> BuildMonthRows()
    {
        var result = new List<YearMonthData<T>>();
        var cal = Calendar.Culture.Calendar;
        var year = cal.GetYear(Calendar.CurrentDay);
        var monthsInYear = cal.GetMonthsInYear(year);

        for (var month = 1; month <= monthsInYear; month++)
        {
            var monthStart = cal.ToDateTime(year, month, 1, 0, 0, 0, 0);
            var daysInMonth = cal.GetDaysInMonth(year, month);
            var monthEnd = cal.ToDateTime(year, month, daysInMonth, 0, 0, 0, 0);

            // Week-padded grid start — same logic as MonthView
            var dayOfWeekOffset = CalendarDateRange.GetDayOfWeek(monthStart, Calendar.Culture, Calendar.FirstDayOfWeek);
            var gridStart = monthStart.AddDays(-dayOfWeekOffset);
            var gridEnd = gridStart.AddDays(TotalColumns - 1);

            // Build 42 day cells
            var cells = new List<CalendarCell<T>>(TotalColumns);
            for (var i = 0; i < TotalColumns; i++)
            {
                var date = gridStart.AddDays(i);
                var cell = new CalendarCell<T> { Date = date };
                if (date.Date == DateTime.Today) cell.Today = true;
                if (date < monthStart || date > monthEnd) cell.Outside = true;
                cells.Add(cell);
            }

            var eventBars = CalcEventBars(gridStart, monthStart, monthEnd);
            result.Add(new YearMonthData<T>(monthStart, monthEnd, gridStart, cells, eventBars));
        }

        return result;
    }

    private List<ItemPosition<T>> CalcEventBars(DateTime gridStart, DateTime monthStart, DateTime monthEnd)
    {
        // Only include events that actually overlap with the real month range (not the padded grid).
        // This ensures a multi-month event is split: each month row only shows its own portion.
        var items = Calendar.Items
            .Where(i =>
                i.Start.Date <= monthEnd &&
                (i.End?.Date ?? i.Start.Date) >= monthStart)
            .OrderByDescending(i => i.IsMultiDay)
            .ThenBy(i => i.Start)
            .ToList();

        var positions = new List<ItemPosition<T>>();
        var slotEndCols = new List<int>(); // exclusive end column for each occupied slot

        foreach (var item in items)
        {
            // Clamp to the real month boundaries so bars never extend into "outside" cells.
            var itemStart = item.Start.Date < monthStart ? monthStart : item.Start.Date;
            var itemEnd = item.End?.Date ?? item.Start.Date;
            if (itemEnd > monthEnd) itemEnd = monthEnd;

            var startCol = (int)(itemStart - gridStart).TotalDays;
            var endCol = (int)(itemEnd - gridStart).TotalDays + 1; // exclusive
            var width = endCol - startCol;
            if (width <= 0) continue;

            // Greedy interval scheduling: find the first free slot
            var slot = -1;
            for (var i = 0; i < slotEndCols.Count; i++)
            {
                if (slotEndCols[i] <= startCol)
                {
                    slot = i;
                    slotEndCols[i] = endCol;
                    break;
                }
            }
            if (slot < 0)
            {
                slot = slotEndCols.Count;
                slotEndCols.Add(endCol);
            }

            positions.Add(new ItemPosition<T>
            {
                Item = item,
                Left = startCol,
                Width = width,
                Top = DayHeaderHeightPx + slot * SlotHeightPx,
                Position = slot,
                Total = 1,
                Date = DateOnly.FromDateTime(itemStart)
            });
        }

        return positions;
    }

    /// <summary>
    /// Handles a click on a day cell.
    /// </summary>
    protected virtual async Task OnCellClicked(CalendarCell<T> cell)
    {
        if (Calendar.CellClicked.HasDelegate && !cell.Outside
            && (Calendar.IsDateTimeDisabledFunc == null || !Calendar.IsDateTimeDisabledFunc(cell.Date, CalendarView.Year)))
        {
            await Calendar.CellClicked.InvokeAsync(cell.Date);
        }
    }

    /// <summary>
    /// Handles a click on an event bar.
    /// </summary>
    protected virtual Task OnItemClicked(T item)
    {
        return Calendar.ItemClicked.InvokeAsync(item);
    }

    /// <summary>
    /// Returns the inline style for an event bar, positioning it absolutely within the cells container.
    /// </summary>
    protected virtual string EventBarStyle(ItemPosition<T> position)
    {
        var leftPct = ((double)position.Left / TotalColumns * 100).ToInvariantString();
        var widthPct = ((double)position.Width / TotalColumns * 100).ToInvariantString();
        return new StyleBuilder()
            .AddStyle("top", $"{position.Top}px")
            .AddStyle("left", $"{leftPct}%")
            .AddStyle("width", $"{widthPct}%")
            .Build();
    }

    /// <summary>
    /// Returns the CSS classes for a day cell.
    /// </summary>
    protected virtual string DayCellClassname(CalendarCell<T> cell) =>
        new CssBuilder("mud-cal-year-day-cell")
            .AddClass(Calendar.AdditionalDateTimeClassesFunc?.Invoke(cell.Date, CalendarView.Year))
            .AddClass("mud-cal-year-outside", cell.Outside)
            .AddClass("mud-cal-year-today", cell.Today && Calendar.HighlightToday)
            .AddClass("mud-cal-year-link", Calendar.CellClicked.HasDelegate && !cell.Outside)
            .Build();

    /// <summary>
    /// Returns the minimum height of the cells container for a given month row, tall enough to fit all event bars.
    /// </summary>
    protected virtual int RowMinHeight(YearMonthData<T> monthData)
    {
        var maxSlot = monthData.EventBars.Count > 0 ? monthData.EventBars.Max(b => b.Position) + 1 : 0;
        return DayHeaderHeightPx + maxSlot * SlotHeightPx + 4;
    }
}

/// <summary>
/// Holds the data for one month row in the Year view.
/// </summary>
public class YearMonthData<T> where T : CalendarItem
{
    public YearMonthData(DateTime monthStart, DateTime monthEnd, DateTime gridStart,
        List<CalendarCell<T>> cells, List<ItemPosition<T>> eventBars)
    {
        MonthStart = monthStart;
        MonthEnd = monthEnd;
        GridStart = gridStart;
        Cells = cells;
        EventBars = eventBars;
    }

    /// <summary>First day of the calendar month.</summary>
    public DateTime MonthStart { get; }
    /// <summary>Last day of the calendar month.</summary>
    public DateTime MonthEnd { get; }
    /// <summary>Week-padded start of the 6-week grid (may be in the previous month).</summary>
    public DateTime GridStart { get; }
    /// <summary>42 day cells covering the 6-week grid.</summary>
    public List<CalendarCell<T>> Cells { get; }
    /// <summary>Absolutely-positioned event bars for this month row.</summary>
    public List<ItemPosition<T>> EventBars { get; }
}
