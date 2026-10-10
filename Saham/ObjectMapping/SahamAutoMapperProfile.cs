using AutoMapper;
using Saham.Entities.Books;
using Saham.Services.Dtos.Books;

namespace Saham.ObjectMapping;

public class SahamAutoMapperProfile : Profile
{
    public SahamAutoMapperProfile()
    {
        CreateMap<Book, BookDto>();
        CreateMap<CreateUpdateBookDto, Book>();
        CreateMap<BookDto, CreateUpdateBookDto>();
        /* Create your AutoMapper object mappings here */
    }
}