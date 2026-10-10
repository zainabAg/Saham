using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Pricing;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Admin;

public class PricingPlanDto : AuditedEntityDto<Guid>
{
    public Guid CityId { get; set; }

    public string Name { get; set; } = string.Empty;

    public PricingPlanStatus Status { get; set; }

    public long DriverBasePay { get; set; }

    public long BatchSecondOrderPay { get; set; }

    public int CancelAfterArrivalPayPct { get; set; }

    public long FreeDeliveryThreshold { get; set; }

    public FreeDeliveryOperator FreeDeliveryOperator { get; set; }

    public FreeDeliveryBasis FreeDeliveryBasis { get; set; }

    public DistanceMethod DistanceMethod { get; set; }

    public int MaxDeliveryDistanceM { get; set; }

    public DateTime? EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public List<PricingTierDto> Tiers { get; set; } = new();
}

/* A distance band (FromM, ToM] — the upper bound is inclusive. */
public class PricingTierDto
{
    [Range(0, int.MaxValue)]
    public int FromM { get; set; }

    [Range(1, int.MaxValue)]
    public int ToM { get; set; }

    [Range(0, long.MaxValue)]
    public long CustomerFee { get; set; }

    [Range(0, long.MaxValue)]
    public long DriverExtra { get; set; }

    public bool FreeDeliveryEligible { get; set; }
}

public class GetPricingPlansInput
{
    public Guid? CityId { get; set; }

    public PricingPlanStatus? Status { get; set; }
}

public class CreateUpdatePricingPlanDto
{
    [Required]
    public Guid CityId { get; set; }

    [Required]
    [StringLength(PricingPlan.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long DriverBasePay { get; set; } = 1_500;

    [Range(0, long.MaxValue)]
    public long BatchSecondOrderPay { get; set; } = 1_000;

    [Range(0, 100)]
    public int CancelAfterArrivalPayPct { get; set; } = 50;

    [Range(0, long.MaxValue)]
    public long FreeDeliveryThreshold { get; set; } = 25_000;

    public FreeDeliveryOperator FreeDeliveryOperator { get; set; } = FreeDeliveryOperator.GreaterThan;

    public FreeDeliveryBasis FreeDeliveryBasis { get; set; } = FreeDeliveryBasis.AfterDiscount;

    public DistanceMethod DistanceMethod { get; set; } = DistanceMethod.StraightLine;

    [Range(1, int.MaxValue)]
    public int MaxDeliveryDistanceM { get; set; } = 10_000;

    [Required]
    [MinLength(1)]
    public List<PricingTierDto> Tiers { get; set; } = new();
}
