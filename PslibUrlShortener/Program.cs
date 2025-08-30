using Microsoft.AspNetCore.Antiforgery;
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
    .WriteTo.File("Logs/shortener.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 32,
                  outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}",
                  formatProvider: CultureInfo.InvariantCulture)
    .Enrich.FromLogContext()
    .ReadFrom.Configuration(ctx.Configuration));

var config = builder.Configuration;

//builder.Services.AddRouting(o => o.LowercaseUrls = true);

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(config.GetConnectionString("DefaultConnection")));

//builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddHttpContextAccessor();

// Politiky
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PslibUrlShortener.Constants.Identity.Policy.IsUser, p => p.RequireClaim("shortener.user", "true", "1"));
    options.AddPolicy(PslibUrlShortener.Constants.Identity.Policy.IsAdmin, p => p.RequireClaim("shortener.admin", "true", "1"));
});

// AuthN: Cookies + OIDC
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
        options.AccessDeniedPath = "/forbidden";
        options.LoginPath = "/sign-in";
        options.ReturnUrlParameter = "returnUrl";
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

        // vlastní claimy z IdP
        options.ClaimActions.MapUniqueJsonKey("shortener.user", "shortener.user");
        options.ClaimActions.MapUniqueJsonKey("shortener.admin", "shortener.admin");

        // vlastní chování při chybě přihlášení
        options.Events = new OpenIdConnectEvents
        {
            OnRemoteFailure = ctx =>
            {
                var reason = Uri.EscapeDataString(ctx.Failure?.Message ?? "access_denied");
                ctx.Response.Redirect($"/signin-error?error={reason}");
                ctx.HandleResponse();
                return Task.CompletedTask;
            }
        };
    });

// Razor Pages + konvence
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeAreaFolder("Links", "/", PslibUrlShortener.Constants.Identity.Policy.IsUser);
    options.Conventions.AuthorizeAreaFolder("Admin", "/", PslibUrlShortener.Constants.Identity.Policy.IsAdmin);    
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/SigninError");
    options.Conventions.AllowAnonymousToPage("/Forbidden");
    options.Conventions.AllowAnonymousToPage("/Privacy");
});

var app = builder.Build();

// Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
/*
app.UseWhen(ctx => !ctx.Request.Path.Equals("/forbidden", StringComparison.OrdinalIgnoreCase), branch =>
{
    branch.UseStatusCodePagesWithReExecute("/Error/{0}");
});
*/

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var claims = context.User.Claims
        .Select(c => $"{c.Type}: {c.Value}")
        .ToArray();

    // Dočasně vypiš do logu nebo do response headeru (POZOR - v produkci nikdy neklaimovat do headeru)
    Console.WriteLine("---- ALL CLAIMS ----");
    foreach (var claim in claims)
        Console.WriteLine(claim);

    // Nebo: context.Response.Headers.Add("X-Claims", string.Join(";", claims));
    await next();
});
app.Use(async (context, next) =>
{
    foreach (var identity in context.User.Identities)
    {
        Console.WriteLine($"Identity: {identity.AuthenticationType}, Authenticated: {identity.IsAuthenticated}");
        foreach (var claim in identity.Claims)
        {
            Console.WriteLine($"  {claim.Type}: {claim.Value}");
        }
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    // Diagnostika / pomocné endpointy
    app.MapGet("/_me", (ClaimsPrincipal user) =>
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
}

app.MapRazorPages();

// Přihlášení (GET) – po úspěchu na alias /links
app.MapGet("/sign-in", () =>
    Results.Challenge(
        properties: new AuthenticationProperties { RedirectUri = "/links" },
        authenticationSchemes: new[] { OpenIdConnectDefaults.AuthenticationScheme }
    ));

// Odhlášení (POST) – s CSRF validací a povoleno jen přihlášeným
app.MapPost("/sign-out", async (HttpContext ctx, IAntiforgery af) =>
{
    await af.ValidateRequestAsync(ctx);
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(
        OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" }
    );
    return Results.Empty;
}).RequireAuthorization();

app.Run();