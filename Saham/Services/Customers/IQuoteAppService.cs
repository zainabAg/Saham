using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Customers;
using Volo.Abp.Application.Services;

namespace Saham.Services.Customers;

public interface IQuoteAppService : IApplicationService
{
    Task<QuoteDto> CreateAsync(CreateQuoteDto input);

    Task<QuoteDto> GetAsync(Guid id);
}
