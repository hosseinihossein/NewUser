using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using AspApp.Helpers;

namespace AspApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        //******************* Kestrel *******************
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, 80);
            options.Limits.MaxRequestBodySize = 16 * 1024;// 16 KB
        });


        builder.Services.AddControllersWithViews();


        //******************* AntiForgery *******************
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            //options.Cookie.Name = "XSRF-TOKEN";//swap error
        });


        //******************* IHttpClientFactory *******************
        builder.Services.AddHttpClient();

        //**************************** Custom Services **************************
        if (builder.Configuration.GetValue<bool>("TurnsTileEnable"))
        {
            builder.Services.AddSingleton<TurnstileService>();
        }


        //**************************** Kafka Producer **************************
        KafkaProducerService kafkaService = new(builder.Configuration);
        builder.Services.AddSingleton<KafkaProducerService>(kafkaService);


        //**************************** Kafka Logger **************************
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(); // Keep Console for local visibility
        builder.Logging.AddKafka(kafkaService, builder.Configuration);


        var app = builder.Build();


        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            KnownIPNetworks = { new System.Net.IPNetwork(IPAddress.Parse("172.18.0.0"), 16) }
        });


        app.MapControllers();
        app.MapDefaultControllerRoute();

        
        //******************* app.Run ******************
        Console.WriteLine($"\n*** App is running on all networks port '80'");
        app.Run();
    }
}
