using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using AspApp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using Microsoft.AspNetCore.DataProtection;

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


        //******************* Identity_DbContext *******************
        builder.Services.AddDbContext<Identity_DbContext>(options =>
        {
            options.UseNpgsql(builder.Configuration["Postgres:ConnectionString:Identity"]);
        });
        //******************* DataProtection_DbContext *******************
        builder.Services.AddDbContext<DataProtection_DbContext>(options =>
        {
            options.UseNpgsql(builder.Configuration["Postgres:ConnectionString:DataProtection"]);
        });


        //******************* Identity service *******************
        builder.Services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
        {
            options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._";

            options.User.RequireUniqueEmail = true;

            options.SignIn.RequireConfirmedEmail = false;

            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 8;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 1;
        })
        .AddEntityFrameworkStores<Identity_DbContext>()
        .AddDefaultTokenProviders();


        //******************* identity cookie *******************
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true; // prevent from js access
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // https only
            options.Cookie.SameSite = SameSiteMode.None;// no csrf protection to solve strict tracking prevention mechanisms

            //Default for "Remember Me". Without "Remember Me" → Session cookie (expires when browser closes)
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            //options.LoginPath = "/Account/Login";
            //options.AccessDeniedPath = "/Account/AccessDenied";
        });


        //******************* Data Protection *******************
        // Configure shared Data Protection keys in SQL DB
        builder.Services.AddDataProtection()
        .PersistKeysToDbContext<DataProtection_DbContext>() // Store keys in the same DB
        .SetApplicationName("SharedIdentityApp"); // Must match across all servers


        //******************* SecurityStampValidatorOptions *******************
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
        });


        // ************ MongoDb ***********
        // Register IMongoClient as a Singleton
        IMongoClient mongoClient = new MongoClient(builder.Configuration["MongoDb:ConnectionString"]);
        builder.Services.AddSingleton<IMongoClient>(mongoClient);
        // Register IMongoDatabase directly as a Singleton for quick access
        IMongoDatabase userDb = mongoClient.GetDatabase(builder.Configuration["MongoDb:UserDbName"]);
        builder.Services.AddSingleton<IMongoDatabase>(userDb);


        //******************* ControllersWithViews *******************
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
        builder.Services.AddSingleton<TurnstileService>();


        //**************************** Kafka Producer **************************
        KafkaProducerService kafkaService = new(builder.Configuration);
        builder.Services.AddSingleton<KafkaProducerService>(kafkaService);


        //**************************** Logger **************************
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(); // Keep Console for local visibility
        builder.Logging.AddKafka(kafkaService, builder.Configuration);


        //**************************** Email Sender **************************
        builder.Services.AddSingleton<KafkaEmailSender>();


        //************************* app **********************
        var app = builder.Build();


        // ************************* forward headers **********************
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            KnownIPNetworks = { new System.Net.IPNetwork(IPAddress.Parse("172.18.0.0"), 16) }
        });


        // *************** map controllers and actions ***************
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapDefaultControllerRoute();


        //******************* app.Run ******************
        Console.WriteLine($"\n*** App is running on all networks port '80'");
        app.Run();
    }
}
