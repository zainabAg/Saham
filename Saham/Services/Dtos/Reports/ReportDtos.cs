using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Saham.Services.Dtos.Reports;

/* SRS 17.2 report catalogue (RPT-05/06 are the live balances in BalancesAppService). */
public enum ReportCode
{
    RestaurantSales = 1,      // RPT-01
    DeliveryTimes = 2,        // RPT-02
    DriverPerformance = 3,    // RPT-03
    MonthlyProfit = 4,        // RPT-04
    OrderCounts = 7,          // RPT-07
    Cancellations = 8,        // RPT-08
    Settlements = 9,          // RPT-09
    Refunds = 11,             // RPT-11
    CustomerBalances = 12,    // RPT-12
    DriverWeekly = 13         // RPT-13
}

/* Periods are operating days (BR-061: a day ends at 04:00 Baghdad), both ends included. */
public class ReportInput
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public Guid? RestaurantId { get; set; }

    public Guid? DriverId { get; set; }
}

public class ExportReportInput : ReportInput
{
    [Required]
    public ReportCode Report { get; set; }
}

/* Every report is a table: the same columns go to the Excel data sheet (SRS 17.2 "Excel export"). */
public class ReportTableDto
{
    public ReportCode Report { get; set; }

    public string Title { get; set; } = string.Empty;

    public Dictionary<string, string> Criteria { get; set; } = new();

    public List<string> Columns { get; set; } = new();

    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /* Report-level figures (totals, "incomplete" flags...). */
    public Dictionary<string, object?> Summary { get; set; } = new();
}
