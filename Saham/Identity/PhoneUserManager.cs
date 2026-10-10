using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Customers;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Identity;

/* Users are identified by their phone number (SRS users.phone, E.164).
 * UserName = the normalized phone; Email is a synthetic unique placeholder
 * because ABP Identity requires one. */
public class PhoneUserManager : DomainService
{
    private const string SyntheticEmailDomain = "phone.saham.local";

    private readonly IdentityUserManager _userManager;
    private readonly IRepository<CustomerProfile, Guid> _customerProfileRepository;

    public PhoneUserManager(
        IdentityUserManager userManager,
        IRepository<CustomerProfile, Guid> customerProfileRepository)
    {
        _userManager = userManager;
        _customerProfileRepository = customerProfileRepository;
    }

    public Task<IdentityUser?> FindByPhoneAsync(string normalizedPhone)
    {
        return _userManager.FindByNameAsync(normalizedPhone);
    }

    public async Task<IdentityUser> CreateAsync(string normalizedPhone, string? name, IEnumerable<string> roles)
    {
        if (await FindByPhoneAsync(normalizedPhone) != null)
        {
            throw new BusinessException(SahamErrorCodes.PhoneAlreadyRegistered);
        }

        var user = new IdentityUser(
            GuidGenerator.Create(),
            normalizedPhone,
            $"{normalizedPhone.TrimStart('+')}@{SyntheticEmailDomain}");

        user.SetPhoneNumber(normalizedPhone, confirmed: true);
        user.Name = name;

        (await _userManager.CreateAsync(user)).CheckErrors();

        var roleList = roles.ToList();
        if (roleList.Count > 0)
        {
            (await _userManager.AddToRolesAsync(user, roleList)).CheckErrors();
        }

        if (roleList.Contains(SahamRoles.Customer))
        {
            await _customerProfileRepository.InsertAsync(new CustomerProfile(user.Id));
        }

        return user;
    }

    /* First OTP login of an unknown number registers a new customer (S-02). */
    public async Task<IdentityUser> FindOrRegisterCustomerAsync(string normalizedPhone)
    {
        return await FindByPhoneAsync(normalizedPhone)
               ?? await CreateAsync(normalizedPhone, null, new[] { SahamRoles.Customer });
    }
}
