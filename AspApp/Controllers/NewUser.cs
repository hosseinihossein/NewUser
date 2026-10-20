using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AspApp.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AspApp.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NewUserController : ControllerBase
{
    private readonly ILogger<NewUserController> _logger;
    private readonly KafkaEmailSender _emailSender;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IMongoCollection<BsonDocument> _userProfileCollection;



    public NewUserController(IMongoDatabase mongoUserDb, ILogger<NewUserController> logger,
    KafkaEmailSender emailSender, UserManager<IdentityUser<Guid>> userManager,
    IConfiguration configuration)
    {
        _logger = logger;
        _emailSender = emailSender;
        _userManager = userManager;
        _userProfileCollection = mongoUserDb.GetCollection<BsonDocument>(configuration["MongoDB:ProfileCollectionName"] ?? "Profile");
    }



    // **********    AntiForgery    **********
    [HttpGet]
    [GenerateAntiforgeryTokenCookie]
    public IActionResult GetAntiForgery()
    {
        return Ok();
    }



    // **********    Signup    **********
    [HttpPost]
    [ValidateTurnstile]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup([FromBody] Identity_SignupFormModel signupModel,
    //[FromServices] TurnstileService turnstileService,
    [FromServices] SignInManager<IdentityUser<Guid>> signInManager)
    {
        if (ModelState.IsValid)
        {
            /*TurnstileResponse? turnstileResponse =
            await turnstileService.ValidateTokenAsync(HttpContext, signupModel.CfTurnstileResponse);

            if (turnstileResponse is null || turnstileResponse.Success is false)
            {
                ModelState.AddModelError("TurnstileError",
                turnstileResponse is null ? "response null!" : string.Join(", ", turnstileResponse.ErrorCodes));
                return BadRequest(ModelState);
            }*/

            IdentityUser<Guid> user = new IdentityUser<Guid>()
            {
                UserName = signupModel.Username,
                Email = signupModel.Email,
                EmailConfirmed = false,
            };
            IdentityResult result = await _userManager.CreateAsync(user, signupModel.Password);
            if (result.Succeeded)
            {
                //send email validation link
                await SendEmailValidationLink(user);

                //create user profile document on mongodb
                var bsonDoc = new BsonDocument
                {
                    {"_id", new BsonBinaryData(user.Id, GuidRepresentation.Standard)},
                    { "CreatedAt", new BsonDateTime(DateTime.UtcNow) }
                };
                await _userProfileCollection.InsertOneAsync(bsonDoc);

                //submit a log
                var userLog = new
                {
                    Action = nameof(Signup),
                    user.Id,
                    user.UserName,
                    user.Email,
                };
                string userLogJson = JsonSerializer.Serialize(userLog);
                _logger.LogInformation(userLogJson);

                await signInManager.SignInAsync(user, true);

                return Redirect("~/UserSettings");
            }

            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError("Signup", error.Description);
            }
        }
        return BadRequest(ModelState);
    }



    // **********    Email    **********
    private async Task SendEmailValidationLink(IdentityUser<Guid> user)
    {
        //***** Generate Email Validation Token *****
        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        //***** Sending Email *****
        string emailMessage = $"<h4>Hi dear {user.UserName}</h4>" +
        "<p>Please click " +
        $"<a href='https://localhost:5443/Identity/ConfirmEmail?token={token}&email={user.Email}' " +
        "target='_blank'>'Here'</a>" +
        " to confirm your email.</p>";

        EmailModel emailModel = new()
        {
            Message = emailMessage,
            ReceiverEmail = user.Email!,
            ReceiverName = user.UserName,
            Subject = "Email Validation"
        };

        _emailSender.Send(emailModel);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ConfirmEmail([FromQuery] string token,
    [FromQuery][StringLength(128)] string email)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user = await _userManager.FindByEmailAsync(email);
            if (user is null)
            {
                //return error to warning page about user not found
                return Redirect("~/Warning?status=danger&msg=User Not found!");
            }

            if (user.EmailConfirmed)
            {
                //return info to warning page about user already been confirmed
                return Redirect("~/Warning?status=danger&msg=Your Email has already been confirmed!");
            }

            IdentityResult result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                //return Redirect("~/UserProfile");

                // force the user to authorize again to refresh the oidc tokens
                return Redirect("/angular/logout/page/route");//that handles request to ~/Api/Authorization/Logout
            }

            //Confirmation not succeeded
            //return Redirect("~/Warning?status=danger&msg=Your email validation link Is Incorrect or Expired!");

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("ConfirmEmail", error.Description);
            }
        }

        //BadRequest
        return BadRequest(ModelState);
        //return Redirect("~/Warning?status=danger&msg=Bad Request!");
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeEmail([FromQuery][StringLength(128)] string newEmail)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                //return Redirect("~/Warning?status=danger&msg=User Not found!");
                ModelState.AddModelError("User", "User Not Found! You need to authenticate.");
                return BadRequest(ModelState);
            }

            if (user.Email == newEmail)
            {
                ModelState.AddModelError("Email", "The current and new email addresses are the same!");
            }
            else
            {
                await SendNewEmailValidationLink(user, newEmail);
                return Ok(new { success = true });
            }

        }
        return BadRequest(ModelState);
    }

    private async Task SendNewEmailValidationLink(IdentityUser<Guid> user, string newEmail)
    {
        //***** Generate Email Validation Token *****
        string token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);

        //***** Sending Email *****
        string emailMessage = $"<h4>Hi dear {user.UserName}</h4>" +
        "<p>Please click " +
        $"<a href='https://localhost:5443/Identity/ConfirmEmail?token={token}&email={user.Email}' " +
        "target='_blank'>'Here'</a>" +
        " to confirm your new email.</p>";

        EmailModel emailModel = new()
        {
            Message = emailMessage,
            ReceiverEmail = newEmail,
            ReceiverName = user.UserName,
            Subject = "New Email Validation"
        };

        _emailSender.Send(emailModel);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ConfirmNewEmail([FromQuery] string token,
    [FromQuery][StringLength(128)] string newEmail)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                //return error to warning page about user not found
                return Redirect("~/Warning?status=danger&msg=User Not found!");
            }

            if (user.Email == newEmail)
            {
                //return info to warning page about user already been confirmed
                return Redirect("~/Warning?status=danger&msg=Your Email has already been confirmed!");
            }

            IdentityResult result = await _userManager.ChangeEmailAsync(user, newEmail, token);
            if (result.Succeeded)
            {
                //return Redirect("~/UserProfile");

                // force the user to authorize again to refresh the oidc tokens
                return Redirect("/angular/logout/page/route");//that handles request to ~/Api/Authorization/Logout
            }

            //Confirmation not succeeded
            //return Redirect("~/Warning?status=danger&msg=Your email validation link Is Incorrect or Expired!");

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("ConfirmNewEmail", error.Description);
            }
        }

        //BadRequest
        return BadRequest(ModelState);
        //return Redirect("~/Warning?status=danger&msg=Bad Request!");
    }



    // **********    Password    **********
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgetPassword([FromQuery][StringLength(128)] string email)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user = await _userManager.FindByEmailAsync(email);
            if (user is not null)
            {
                await SendResetPasswordLink(user);

                return Ok(new { success = true });
            }
            ModelState.AddModelError("Email", "Email Not found!");

        }
        return BadRequest(ModelState);
    }

    private async Task SendResetPasswordLink(IdentityUser<Guid> user)
    {
        //***** Generate Email Validation Token *****
        string token = await _userManager.GeneratePasswordResetTokenAsync(user);

        //***** Sending Email *****
        string emailMessage = $"<h4>Hi dear {user.UserName}</h4>" +
        "<p>Please click " +
        $"<a href='https://localhost:5443/Identity/ResetPassword?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(user.Email!)}' " +
        "target='_blank'>'Here'</a>" +
        " to proceed resetting your password.</p>";

        EmailModel emailModel = new()
        {
            Message = emailMessage,
            ReceiverEmail = user.Email!,
            ReceiverName = user.UserName,
            Subject = "Reset Password"
        };

        _emailSender.Send(emailModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitResetPassword(Identity_ResetPasswordFormModel formModel)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user = await _userManager.FindByEmailAsync(formModel.Email);
            if (user is null)
            {
                ModelState.AddModelError("User", "User Not Found! Email is invalid!");
                return BadRequest(ModelState);
            }

            var result =
            await _userManager.ResetPasswordAsync(user, formModel.Token, formModel.NewPassword);
            if (result.Succeeded)
            {
                return Ok(new { success = true });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("SubmitResetPassword", error.Description);
            }
        }
        return BadRequest(ModelState);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitChangePassword(Identity_ChangePasswordFormModel formModel)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                ModelState.AddModelError("User", "User Not Found! You need to authenticate.");
                return BadRequest(ModelState);
            }

            var result =
            await _userManager.ChangePasswordAsync(user, formModel.CurrentPassword, formModel.NewPassword);
            if (result.Succeeded)
            {
                // force the user to authorize again to refresh the oidc tokens
                return Redirect("/angular/logout/page/route");//that handles request to ~/Api/Authorization/Logout
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("SubmitResetPassword", error.Description);
            }
        }
        return BadRequest(ModelState);
    }



    // **********    UserName    **********



}