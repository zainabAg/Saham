namespace Saham.Entities.Pricing;

public enum PricingPlanStatus
{
    Draft = 0,
    Active = 1,
    Retired = 2
}

/* REC-01: "above 25 thousand" = strictly greater by default. */
public enum FreeDeliveryOperator
{
    GreaterThan = 0,
    GreaterThanOrEqual = 1
}

/* ASM-03: the threshold is compared with the items value after discount by default. */
public enum FreeDeliveryBasis
{
    AfterDiscount = 0,
    BeforeDiscount = 1
}

/* TD-01: straight line (Haversine) now; road distance possible later without a redesign. */
public enum DistanceMethod
{
    StraightLine = 0,
    Road = 1
}

public enum OrderType
{
    Asap = 0,
    Scheduled = 1
}
