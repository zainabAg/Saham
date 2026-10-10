using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Saham.Entities.Drivers;
using Saham.Entities.Restaurants;
using Saham.Identity;
using Volo.Abp.Identity;
using Volo.Abp.Timing;
using Saham.Entities.Shared;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace Saham.Data.Seeding;

/* Demo restaurants and menus for development and the client demonstration (SRS 21).
 * Runs only when "Saham:SeedDemoData" is true and no restaurant exists yet.
 * Demo accounts log in with phone + OTP (the code appears in the SMS log). */
public class DemoDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<MenuCategory, Guid> _categoryRepository;
    private readonly IRepository<MenuItem, Guid> _itemRepository;
    private readonly MenuManager _menuManager;
    private readonly RestaurantStaffManager _restaurantStaffManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ReferenceDataSeedContributor _referenceDataSeeder;
    private readonly RolesAndPermissionsDataSeedContributor _rolesSeeder;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly PhoneUserManager _phoneUserManager;
    private readonly IdentityUserManager _userManager;
    private readonly IClock _clock;

    public DemoDataSeedContributor(
        IConfiguration configuration,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<MenuCategory, Guid> categoryRepository,
        IRepository<MenuItem, Guid> itemRepository,
        MenuManager menuManager,
        RestaurantStaffManager restaurantStaffManager,
        IGuidGenerator guidGenerator,
        ReferenceDataSeedContributor referenceDataSeeder,
        RolesAndPermissionsDataSeedContributor rolesSeeder,
        IRepository<Driver, Guid> driverRepository,
        PhoneUserManager phoneUserManager,
        IdentityUserManager userManager,
        IClock clock)
    {
        _driverRepository = driverRepository;
        _phoneUserManager = phoneUserManager;
        _userManager = userManager;
        _clock = clock;
        _referenceDataSeeder = referenceDataSeeder;
        _rolesSeeder = rolesSeeder;
        _configuration = configuration;
        _restaurantRepository = restaurantRepository;
        _categoryRepository = categoryRepository;
        _itemRepository = itemRepository;
        _menuManager = menuManager;
        _restaurantStaffManager = restaurantStaffManager;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (!_configuration.GetValue<bool>("Saham:SeedDemoData"))
        {
            return;
        }

        // Seeder order is not guaranteed: the city and the roles must exist first (both are idempotent).
        await _referenceDataSeeder.SeedAsync(context);
        await _rolesSeeder.SeedAsync(context);

        if (await _driverRepository.GetCountAsync() == 0)
        {
            await SeedDriversAsync();
        }

        if (await _restaurantRepository.GetCountAsync() > 0)
        {
            return;
        }

        var everyDay = Enum.GetValues<DayOfWeek>();

        // 1. Grill restaurant, open 12:00 → 02:00 (past midnight), with all three accounts.
        var grill = await CreateRestaurantAsync(
            new I18nText("مشويات الحسين", "Al-Hussein Grill"), "+9647700000100",
            32.6175, 44.0321, "شارع الإمام علي، قرب ساحة البلدية",
            everyDay.Select(d => (d, new TimeOnly(12, 0), new TimeOnly(2, 0))),
            minOrder: 5_000);
        await AddMenuAsync(grill.Id, new()
        {
            [new I18nText("مشويات", "Grills")] = new()
            {
                (new I18nText("كباب لحم", "Lamb kebab"), 8_000),
                (new I18nText("تكة دجاج", "Chicken tikka"), 7_000),
                (new I18nText("نص مشكل", "Mixed grill (half)"), 12_000)
            },
            [new I18nText("مقبلات", "Sides")] = new()
            {
                (new I18nText("حمص", "Hummus"), 2_000),
                (new I18nText("سلطة", "Salad"), 1_500)
            }
        });
        await _restaurantStaffManager.CreateAsync(grill.Id, "+9647700000101", "مدير مشويات الحسين", RestaurantStaffRole.Manager);
        await _restaurantStaffManager.CreateAsync(grill.Id, "+9647700000102", "مطبخ مشويات الحسين", RestaurantStaffRole.Kitchen);
        await _restaurantStaffManager.CreateAsync(grill.Id, "+9647700000103", "كاشير مشويات الحسين", RestaurantStaffRole.Cashier);

        // 2. Breakfast place with split shifts: 07:00 → 12:00 and 17:00 → 23:00.
        var breakfast = await CreateRestaurantAsync(
            new I18nText("فطور كربلاء", "Karbala Breakfast"), "+9647700000200",
            32.6102, 44.0189, "حي الحسين، مقابل جامع الرحمن",
            everyDay.SelectMany(d => new[]
            {
                (d, new TimeOnly(7, 0), new TimeOnly(12, 0)),
                (d, new TimeOnly(17, 0), new TimeOnly(23, 0))
            }),
            minOrder: 3_000);
        await AddMenuAsync(breakfast.Id, new()
        {
            [new I18nText("فطور", "Breakfast")] = new()
            {
                (new I18nText("كاهي وقيمر", "Kahi with geymar"), 4_000),
                (new I18nText("باقلاء بالدهن", "Bagilla bil dihin"), 3_000),
                (new I18nText("بيض بالطماطة", "Eggs with tomato"), 2_500)
            }
        });
        await _restaurantStaffManager.CreateAsync(breakfast.Id, "+9647700000201", "مدير فطور كربلاء", RestaurantStaffRole.Manager);

        // 3. Pizza place, open 24 hours (00:00 → 23:59) — the free-delivery example of S-04.
        var pizza = await CreateRestaurantAsync(
            new I18nText("بيتزا الفرات", "Euphrates Pizza"), "+9647700000300",
            32.6230, 44.0410, "شارع ميثم التمار، قرب الجسر",
            everyDay.Select(d => (d, new TimeOnly(0, 0), new TimeOnly(23, 59))),
            minOrder: 10_000);
        await AddMenuAsync(pizza.Id, new()
        {
            [new I18nText("بيتزا", "Pizza")] = new()
            {
                (new I18nText("بيتزا مارغريتا", "Margherita"), 9_000),
                (new I18nText("بيتزا دجاج", "Chicken pizza"), 11_000),
                (new I18nText("بيتزا عائلية", "Family pizza"), 18_000)
            },
            [new I18nText("مشروبات", "Drinks")] = new()
            {
                (new I18nText("ببسي", "Pepsi"), 1_000),
                (new I18nText("ماء", "Water"), 500)
            }
        });
        await _restaurantStaffManager.CreateAsync(pizza.Id, "+9647700000301", "مدير بيتزا الفرات", RestaurantStaffRole.Manager);
    }

    /* Three approved demo drivers, offline until they open the app and send a location. */
    private async Task SeedDriversAsync()
    {
        var approver = await _userManager.FindByNameAsync("admin");
        foreach (var (phone, name, plate) in new[]
                 {
                     ("+9647700000901", "علي السائق", "كربلاء 1101"),
                     ("+9647700000902", "حسين السائق", "كربلاء 1102"),
                     ("+9647700000903", "مهدي السائق", "كربلاء 1103")
                 })
        {
            var user = await _phoneUserManager.CreateAsync(phone, name, new[] { SahamRoles.Driver });
            var driver = new Driver(user.Id, ReferenceDataSeedContributor.KarbalaCityId, plate, null, 25_000);
            driver.ApproveForSeed(approver?.Id ?? user.Id, _clock.Now);
            await _driverRepository.InsertAsync(driver, autoSave: true);
        }
    }

    private async Task<Restaurant> CreateRestaurantAsync(
        I18nText name,
        string phone,
        double latitude,
        double longitude,
        string addressText,
        IEnumerable<(DayOfWeek, TimeOnly, TimeOnly)> hours,
        long minOrder)
    {
        var restaurant = new Restaurant(
            _guidGenerator.Create(),
            ReferenceDataSeedContributor.KarbalaCityId,
            name,
            phone,
            GeoFactory.CreatePoint(latitude, longitude),
            addressText);

        restaurant.SetHours(hours, _guidGenerator.Create);
        restaurant.SetMinOrderAmount(minOrder);
        restaurant.Activate();

        return await _restaurantRepository.InsertAsync(restaurant, autoSave: true);
    }

    private async Task AddMenuAsync(Guid restaurantId, Dictionary<I18nText, List<(I18nText Name, long Price)>> menu)
    {
        var categoryOrder = 0;
        foreach (var (categoryName, items) in menu)
        {
            var category = new MenuCategory(_guidGenerator.Create(), restaurantId, categoryName, categoryOrder++);
            await _categoryRepository.InsertAsync(category, autoSave: true);

            var itemOrder = 0;
            foreach (var (itemName, price) in items)
            {
                var item = await _menuManager.CreateItemAsync(restaurantId, category.Id, itemName, null, price, null, itemOrder++);
                await _itemRepository.InsertAsync(item);
            }
        }
    }
}
