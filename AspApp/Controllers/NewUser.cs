using System.Text.Json;
using AspApp.Helpers;
using Confluent.Kafka;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AspApp.Controllers;

[ApiController]
[Route("Api/[controller]/[action]")]
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
    [FromServices] TurnstileService turnstileService)
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

                return Ok(new { success = true });
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

}