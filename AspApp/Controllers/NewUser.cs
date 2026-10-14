using System.Text.Json;
using AspApp.Helpers;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;

namespace AspApp.Controllers;

[ApiController]
[Route("Api/[controller]/[action]")]
public class NewUserController : ControllerBase
{
    private readonly KafkaProducerService _kafka;
    private readonly ILogger<NewUserController> _logger;


    public NewUserController(KafkaProducerService kafka, ILogger<NewUserController> logger)
    {
        _kafka = kafka;
        _logger = logger;
    }

    public IActionResult TestProducer()
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
        /*var message = new Message<Null, string>()
        {
            Value = messageJson,
        };
        
        _kafka.Producer.Produce("test", message, (deliveryReport) =>
            {
                if (deliveryReport.Error.Code != ErrorCode.NoError) {
                    Console.WriteLine($"Failed to deliver message: {deliveryReport.Error.Reason}");
                }
                else {
                    Console.WriteLine($"Produced event to topic test: value = {messageJson}");
                }
            }
        );*/

        _logger.LogInformation(messageJson);

        return Ok(messageJson);
    }
}