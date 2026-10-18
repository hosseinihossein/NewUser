using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

//******************** Postgres *********************
public class Identity_DbContext : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>
{
    public Identity_DbContext(DbContextOptions<Identity_DbContext> options) : base(options) { }
}

//******************** Forms *********************
public class Identity_SignupFormModel
{
    [StringLength(60, MinimumLength = 8)]
    public string Username { get; set; } = string.Empty;

    [StringLength(60, MinimumLength = 8)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(60, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? CfTurnstileResponse { get; set; }
}