namespace Saham.Identity;

/* Role codes from SRS section 5.2. The ABP built-in "admin" role is also
 * granted OWNER (see RolesAndPermissionsDataSeedContributor). */
public static class SahamRoles
{
    public const string Customer = "CUSTOMER";
    public const string RestaurantManager = "RESTAURANT_MANAGER";
    public const string RestaurantKitchen = "RESTAURANT_KITCHEN";
    public const string RestaurantCashier = "RESTAURANT_CASHIER";
    public const string Driver = "DRIVER";
    public const string Ops = "OPS";
    public const string Accountant = "ACCOUNTANT";
    public const string Owner = "OWNER";

    public static readonly string[] All =
    {
        Customer, RestaurantManager, RestaurantKitchen, RestaurantCashier,
        Driver, Ops, Accountant, Owner
    };

    /* Company staff the owner can create from the admin panel. */
    public static readonly string[] CompanyStaff = { Ops, Accountant, Owner };

    /* Restaurant accounts a restaurant manager can create (SOD-05: never a manager). */
    public static readonly string[] ManagerCreatable = { RestaurantKitchen, RestaurantCashier };
}
