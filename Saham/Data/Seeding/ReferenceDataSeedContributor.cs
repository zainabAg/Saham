using System;
using System.Threading.Tasks;
using Saham.Entities.Geography;
using Saham.Entities.Shared;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace Saham.Data.Seeding;

/* Reference rows every environment needs: the operating city (Karbala, S-18).
 * Its service-area polygon is left empty for the owner to draw (DER-ADR-07). */
public class ReferenceDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public static readonly Guid KarbalaCityId = Guid.Parse("6f1c3a52-9d4e-4b8a-a1d2-3c5e7f9b0a11");

    private readonly IRepository<City, Guid> _cityRepository;

    public ReferenceDataSeedContributor(IRepository<City, Guid> cityRepository)
    {
        _cityRepository = cityRepository;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (await _cityRepository.FindAsync(KarbalaCityId) != null)
        {
            return;
        }

        await _cityRepository.InsertAsync(new City(
            KarbalaCityId,
            new I18nText("كربلاء", "Karbala", "کربلا", "کربلا"),
            GeoFactory.CreatePoint(32.6160, 44.0249)));
    }
}
