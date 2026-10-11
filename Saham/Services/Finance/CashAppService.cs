using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Drivers;
using Saham.Entities.Ledger;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Finance;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Finance;

/* The accountant's cash desk (UC-23, cash.handover.record / cash.float.issue): what each driver
 * hands over at the end of the shift, and the change float. Operations staff cannot post money (SOD-02). */
[Authorize(SahamPermissions.Cash.HandoverRecord)]
public class CashAppService : SahamAppService, ICashAppService
{
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<CashMovement, Guid> _movementRepository;
    private readonly CashManager _cashManager;

    public CashAppService(IRepository<Driver, Guid> driverRepository, IRepository<CashMovement, Guid> movementRepository, CashManager cashManager)
    {
        _driverRepository = driverRepository;
        _movementRepository = movementRepository;
        _cashManager = cashManager;
    }

    [Idempotent]
    public async Task<CashMovementDto> RecordHandoverAsync(RecordHandoverDto input)
    {
        var driver = await _driverRepository.GetAsync(input.DriverId);
        var movement = await _cashManager.RecordHandoverAsync(driver, input.Amount, input.Note, CurrentUser.GetId());
        return await MapAsync(movement, driver);
    }

    [Idempotent]
    [Authorize(SahamPermissions.Cash.FloatIssue)]
    public async Task<CashMovementDto> IssueFloatAsync(MoveFloatDto input)
    {
        var driver = await _driverRepository.GetAsync(input.DriverId);
        return await MapAsync(await _cashManager.MoveFloatAsync(driver, input.Amount, issue: true, input.Note, CurrentUser.GetId()), driver);
    }

    [Idempotent]
    [Authorize(SahamPermissions.Cash.FloatIssue)]
    public async Task<CashMovementDto> ReturnFloatAsync(MoveFloatDto input)
    {
        var driver = await _driverRepository.GetAsync(input.DriverId);
        return await MapAsync(await _cashManager.MoveFloatAsync(driver, input.Amount, issue: false, input.Note, CurrentUser.GetId()), driver);
    }

    public async Task<PagedResultDto<CashMovementDto>> GetMovementsAsync(GetCashMovementsInput input)
    {
        var queryable = (await _movementRepository.GetQueryableAsync())
            .WhereIf(input.DriverId.HasValue, m => m.DriverId == input.DriverId)
            .WhereIf(input.BusinessDate.HasValue, m => m.BusinessDate == input.BusinessDate);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var movements = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(m => m.CreationTime).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<CashMovementDto>(totalCount, movements.Select(m => new CashMovementDto
        {
            Id = m.Id,
            CreationTime = m.CreationTime,
            CreatorId = m.CreatorId,
            DriverId = m.DriverId,
            MovementType = m.MovementType,
            Amount = m.Amount,
            ExpectedAmount = m.ExpectedAmount,
            Difference = m.Difference,
            BusinessDate = m.BusinessDate,
            ReceivedBy = m.ReceivedBy,
            Note = m.Note
        }).ToList());
    }

    private async Task<CashMovementDto> MapAsync(CashMovement m, Driver driver) => new()
    {
        Id = m.Id,
        CreationTime = m.CreationTime,
        CreatorId = m.CreatorId,
        DriverId = m.DriverId,
        MovementType = m.MovementType,
        Amount = m.Amount,
        ExpectedAmount = m.ExpectedAmount,
        Difference = m.Difference,
        BusinessDate = m.BusinessDate,
        ReceivedBy = m.ReceivedBy,
        Note = m.Note,
        CashInHandAfter = await _cashManager.CashInHandAsync(driver.Id),
        CashBlockedAfter = driver.CashBlocked
    };
}
