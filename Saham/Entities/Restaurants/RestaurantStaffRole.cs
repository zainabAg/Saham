using Saham.Identity;

namespace Saham.Entities.Restaurants;

public enum RestaurantStaffRole
{
    Manager = 0,
    Kitchen = 1,
    Cashier = 2
}

public static class RestaurantStaffRoleExtensions
{
    public static string ToRoleName(this RestaurantStaffRole role) => role switch
    {
        RestaurantStaffRole.Manager => SahamRoles.RestaurantManager,
        RestaurantStaffRole.Kitchen => SahamRoles.RestaurantKitchen,
        RestaurantStaffRole.Cashier => SahamRoles.RestaurantCashier,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
