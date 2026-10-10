using System;
using System.ComponentModel.DataAnnotations;

namespace Saham.Services.Dtos.Account;

public class SendOtpInput
{
    [Required]
    [StringLength(SahamConsts.PhoneNumberMaxLength + 5)]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class SendOtpResultDto
{
    /* Normalized E.164 number to send back to /connect/token as phone_number. */
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public int ResendAfterSeconds { get; set; }
}
