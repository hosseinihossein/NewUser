using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AspApp.Helpers;
using Confluent.Kafka;
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
    //private readonly SignInManager<IdentityUser<Guid>> _signInManager;


    public NewUserController(IMongoDatabase mongoUserDb, ILogger<NewUserController> logger,
    KafkaEmailSender emailSender, UserManager<IdentityUser<Guid>> userManager,
    IConfiguration configuration)//, SignInManager<IdentityUser<Guid>> signInManager)
    {
        _logger = logger;
        _emailSender = emailSender;
        _userManager = userManager;
        _userProfileCollection = mongoUserDb.GetCollection<BsonDocument>(configuration["MongoDB:ProfileCollectionName"] ?? "Profile");
        //_signInManager = signInManager;
    }

    /*public IActionResult TestProducer()
    {
        int ranNum = new Random().Next(100);
        var messageObject = new
        {
            Name = $"Name{ranNum}",
            Source = $"Source{ranNum}",
            Type = $"Type{ranNum}",
            Description = $"This is a fake message with random number {ranNum} for testing kafka",
            RandonNumber = ranNum,
        };
        string messageJson = JsonSerializer.Serialize(messageObject);

        _logger.LogInformation(messageJson);

        return Ok(messageJson);
    }*/


    [HttpPost]
    public async Task<IActionResult> Signup([FromBody] Identity_SignupFormModel signupModel,
    [FromServices] TurnstileService turnstileService,
    [FromServices] SignInManager<IdentityUser<Guid>> signInManager)
    {
        if (ModelState.IsValid)
        {
            TurnstileResponse? turnstileResponse =
            await turnstileService.ValidateTokenAsync(HttpContext, signupModel.CfTurnstileResponse);

            if (turnstileResponse is null || turnstileResponse.Success is false)
            {
                ModelState.AddModelError("TurnstileError",
                turnstileResponse is null ? "response null!" : string.Join(", ", turnstileResponse.ErrorCodes));
                return BadRequest(ModelState);
            }

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
                return Redirect("~/Api/Authorization/Logout");
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
                return Redirect("~/Api/Authorization/Logout");
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


    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitResetPassword(Identity_ResetPasswordFormModel formModel)
    {
        if (ModelState.IsValid)
        {
            IdentityUser<Guid>? user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Redirect("~/Warning?status=danger&msg=User Not found!");
            }

            var result =
            await _userManager.ResetPasswordAsync(user, formModel.Token, formModel.NewPassword);
            if (result.Succeeded)
            {
                // force the user to authorize again to refresh the oidc tokens
                return Redirect("~/Api/Authorization/Logout");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("SubmitResetPassword", error.Description);
            }
        }
        return BadRequest(ModelState);
    }

}