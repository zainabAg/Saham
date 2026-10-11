namespace Saham.Entities.Ledger;

/* Chart of accounts (SRS 12.2). */
public enum LedgerAccountCode
{
    // Company
    CashOnHand = 0,
    CompanyBank = 1,
    WalletClearing = 2,
    CustomerPrepaid = 3,
    RefundPayable = 4,
    CommissionRevenue = 5,
    DeliveryFeeRevenue = 6,
    DriverPayExpense = 7,
    DiscountExpense = 8,
    WalletFeeExpense = 9,
    AdjustmentExpense = 10,
    OperatingExpense = 11,

    // One per driver
    DriverCash = 20,      // cash collected and not yet handed over ("in the driver's hands")
    DriverFloat = 21,     // change float carried by the driver
    DriverPayable = 22,   // earnings not yet paid

    // One per restaurant
    RestaurantPayable = 30, // credit = we owe the restaurant; debit = the restaurant owes us

    // One per customer
    CustomerBalance = 40    // credit = in-app credit; debit = debt
}

public enum LedgerAccountType
{
    Asset = 0,
    Liability = 1,
    Revenue = 2,
    Expense = 3
}

public enum LedgerPartyType
{
    Company = 0,
    Driver = 1,
    Restaurant = 2,
    Customer = 3
}

/* Posting templates (SRS 12.4). */
public enum JournalEntryType
{
    CashDelivered = 0,
    WalletCaptured = 1,
    WalletDelivered = 2,
    DriverPay = 3,
    NotReceived = 4,
    CancelDriverPay = 5,
    CompensationCredit = 6,
    RefundInitiated = 7,
    RefundSettled = 8,
    RefundManual = 9,
    FloatIssue = 10,
    FloatReturn = 11,
    Handover = 12,
    DriverPayout = 13,
    RestaurantPayout = 14,
    RestaurantCollect = 15,
    Adjustment = 16,
    ChangeShortfall = 17,
    CustomerLateCancel = 18,
    DriverIncident = 19,
    OperatingExpense = 20,
    Reversal = 21
}

public enum JournalSourceType
{
    Order = 0,
    Payment = 1,
    Refund = 2,
    CashMovement = 3,
    Settlement = 4,
    Adjustment = 5
}

public enum CashMovementType
{
    Handover = 0,
    FloatIssue = 1,
    FloatReturn = 2
}
