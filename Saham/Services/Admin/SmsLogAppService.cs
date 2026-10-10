using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Notifications;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Admin;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Admin;

/* The message log in the admin panel: until a real SMS provider exists,
 * login codes and "order left the restaurant" messages are read here (Appendix C §5). */
[Authorize(SahamPermissions.SmsLogs.View)]
public class SmsLogAppService : SahamAppService, ISmsLogAppService
{
    private readonly IRepository<SmsLog, Guid> _repository;

    public SmsLogAppService(IRepository<SmsLog, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<PagedResultDto<SmsLogDto>> GetListAsync(GetSmsLogsInput input)
    {
        var phoneNumber = input.PhoneNumber.IsNullOrWhiteSpace()
            ? null
            : PhoneNumberNormalizer.Normalize(input.PhoneNumber);

        var queryable = (await _repository.GetQueryableAsync())
            .WhereIf(phoneNumber != null, x => x.PhoneNumber == phoneNumber);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var logs = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(input.Sorting.IsNullOrWhiteSpace() ? nameof(SmsLog.CreationTime) + " desc" : input.Sorting)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<SmsLogDto>(totalCount, ObjectMapper.Map<List<SmsLog>, List<SmsLogDto>>(logs));
    }
}
