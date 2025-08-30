using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PslibUrlShortener.Data;
using Serilog;
using System.Globalization;
using System.Security.Claims;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

Log.Information("Starting up");

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}", formatProvider: CultureInfo.InvariantCulture)
        .Enrich.FromLogContext()
        .WriteTo.File("Logs/shortener.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 32, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}", formatProvider: CultureInfo.InvariantCulture)
        .Enrich.FromLogContext()
        .ReadFrom.Configuration(ctx.Configuration));

var config = builder.Configuration;

var connectionStringName = "DefaultConnection";
var connectionString = config.GetConnectionString(connectionStringName);
var migrationsAssembly = typeof(Program).Assembly.GetName().Name;
builder.Services.AddDbContext<ApplicationDbContext>(o =>
                o.UseSqlServer(connectionString));

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeAreaFolder("Links", "/", "IsUser");
    options.Conventions.AuthorizeAreaFolder("Admin", "/", "IsAdmin");
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PslibUrlShortener.Constants.Identity.Policy.IsUser,
        p => p.RequireClaim("shortener.user", "true"));

    options.AddPolicy(PslibUrlShortener.Constants.Identity.Policy.IsAdmin,
        p => p.RequireClaim("shortener.admin", "true"));
});

// po AddRazorPages()
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = config["Authority:Server"];
        options.ClientId = config["Authority:ClientId"];
        options.ClientSecret = config["Authority:ClientSecret"];
        options.ResponseType = OpenIdConnectResponseType.Code;

        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.Scope.Clear();
        foreach (var s in (config["Authority:Scopes"] ?? "openid profile pslib")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(s);
        }
        options.ClaimActions.MapUniqueJsonKey("shortener.user", "shortener.user");
        options.ClaimActions.MapUniqueJsonKey("shortener.admin", "shortener.admin");
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapGet("/sign-in", () =>
    Results.Challenge(
        properties: new AuthenticationProperties { RedirectUri = "/Links" },
        authenticationSchemes: new[] { OpenIdConnectDefaults.AuthenticationScheme }
    ));

app.MapPost("/sign-out", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(
        OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" }
    );
});

app.MapGet("/me", (ClaimsPrincipal user) =>
{
    var who = user.Identity?.IsAuthenticated == true ? user.Identity?.Name ?? "(no name)" : "Anonymous";
    var claims = user.Claims.Select(c => new { c.Type, c.Value });
    return Results.Json(new { who, claims });
});
app.MapGet("/_endpoints", (EndpointDataSource es) =>
{
    var list = es.Endpoints.Select(e => e.DisplayName).OrderBy(x => x).ToList();
    return Results.Json(list);
});
app.MapGet("/_routes", (EndpointDataSource es) =>
{
    var lines = es.Endpoints
        .OfType<RouteEndpoint>()
        .Select(e => $"{e.RoutePattern.RawText}  =>  {e.DisplayName}")
        .OrderBy(s => s)
        .ToList();
    return Results.Text(string.Join("\n", lines));
});

app.Run();