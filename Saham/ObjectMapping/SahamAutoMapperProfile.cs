using AutoMapper;
using Saham.Entities.Books;
using Saham.Entities.Notifications;
using Saham.Services.Dtos.Admin;
using Saham.Services.Dtos.Books;
using Volo.Abp.AutoMapper;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.ObjectMapping;

public class SahamAutoMapperProfile : Profile
{
    public SahamAutoMapperProfile()
    {
        CreateMap<Book, BookDto>();
        CreateMap<CreateUpdateBookDto, Book>();
        CreateMap<BookDto, CreateUpdateBookDto>();

        // Admin
        CreateMap<IdentityUser, StaffUserDto>()
            .ForMember(d => d.PhoneNumber, o => o.MapFrom(s => s.PhoneNumber ?? string.Empty))
            .Ignore(d => d.Roles);
        CreateMap<SmsLog, SmsLogDto>();
        /* Create your AutoMapper object mappings here */
    }
}