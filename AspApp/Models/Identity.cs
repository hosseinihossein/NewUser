using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

//******************** Postgres *********************
//******************** Identity_DbContext *********************
public class Identity_DbContext : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>
{
    public Identity_DbContext(DbContextOptions<Identity_DbContext> options) : base(options) { }
}

//**************** DataProtection_DbContext ****************
class DataProtection_DbContext : DbContext, IDataProtectionKeyContext
{
    public DataProtection_DbContext(DbContextOptions<DataProtection_DbContext> options)
    : base(options) { }

    // This maps to the table that stores keys
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
}

//******************** Forms *********************
public class Identity_SignupFormModel
{
    [StringLength(60, MinimumLength = 8)]
    public string Username { get; set; } = string.Empty;

    [StringLength(128, MinimumLength = 8)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(60, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? CfTurnstileResponse { get; set; }
}

public class Identity_ChangePasswordFormModel
{
    [StringLength(60, MinimumLength = 8)]
    public string CurrentPassword { get; set; } = string.Empty;

    [StringLength(60, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;

    /*[StringLength(60, MinimumLength = 8)]
    [Compare(nameof(NewPassword))]
    public string RepeatNewPassword { get; set; } = string.Empty;*/
}

public class Identity_ResetPasswordFormModel
{
    //public Guid UserGuid { get; set; }

    public string Token { get; set; } = string.Empty;

    [StringLength(60, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;

    /*[StringLength(60, MinimumLength = 8)]
    [Compare(nameof(NewPassword))]
    public string RepeatNewPassword { get; set; } = string.Empty;*/
}