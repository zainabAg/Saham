using System.ComponentModel.DataAnnotations;
using Saham.Entities.Notifications;

namespace Saham.Services.Dtos.Account;

public class RegisterDeviceDto
{
    /* The FCM registration token from the Firebase SDK on the phone. */
    [Required]
    [StringLength(UserDevice.TokenMaxLength)]
    public string Token { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }
}

public class UnregisterDeviceDto
{
    [Required]
    [StringLength(UserDevice.TokenMaxLength)]
    public string Token { get; set; } = string.Empty;
}
