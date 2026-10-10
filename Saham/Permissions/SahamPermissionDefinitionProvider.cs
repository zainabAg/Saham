using Saham.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace Saham.Permissions;

public class SahamPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(SahamPermissions.GroupName);


        var booksPermission = myGroup.AddPermission(SahamPermissions.Books.Default, L("Permission:Books"));
        booksPermission.AddChild(SahamPermissions.Books.Create, L("Permission:Books.Create"));
        booksPermission.AddChild(SahamPermissions.Books.Edit, L("Permission:Books.Edit"));
        booksPermission.AddChild(SahamPermissions.Books.Delete, L("Permission:Books.Delete"));

        //Define your own permissions here. Example:
        //myGroup.AddPermission(SahamPermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<SahamResource>(name);
    }
}
