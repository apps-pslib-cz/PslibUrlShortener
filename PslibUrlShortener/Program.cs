using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PslibUrlShortener.Data;
using PslibUrlShortener.Services;
using PslibUrlShortener.Services.Options;
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

builder.Services.AddRouting(o => o.LowercaseUrls = true);

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(config.GetConnectionString("DefaultConnection")));

builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.Configure<ListingOptions>(config.GetSection("Listing"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<LinkManager>();
builder.Services.AddScoped<OwnerManager>();

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

        options.ClaimActions.MapUniqueJsonKey(ClaimTypes.NameIdentifier, "sub");
        options.ClaimActions.MapUniqueJsonKey(ClaimTypes.Name, "name");
        options.ClaimActions.MapUniqueJsonKey(ClaimTypes.Email, "email");


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

// Redirect endpoint: zachytí /{code} (pouze alfanum + - _ , do 64 znaků)
// Pozor na pořadí: tento endpoint dávejte až ZA vaše admin/links routy, ať je nepřekrývá.
app.MapGet("/{code:regex(^[a-zA-Z0-9_-]{{1,64}}$)}", async (HttpContext ctx, string code, LinkManager linkMgr) =>
{
    var now = DateTime.UtcNow;
    var link = await linkMgr.ResolveForRedirectAsync(ctx.Request.Scheme, ctx.Request.Host.Host, code, now);

    if (link is null)
    {
        // Můžete vrátit 404, nebo 410 pro soft-deleted. Tady volíme "safe" 404.
        return Results.NotFound();
    }

    // Log hit + bump counters
    var referer = ctx.Request.Headers.Referer.ToString();
    var userAgent = ctx.Request.Headers.UserAgent.ToString();
    var remoteIp = ctx.Connection.RemoteIpAddress?.ToString();
    var isBot = LinkManager.LooksLikeBot(userAgent);

    await linkMgr.RegisterHitAndTouchAsync(link.Id, referer, userAgent, remoteIp, isBot, now);

    // Zachovejme query string, ať lze přenést parametry (volitelně)
    var target = AppendQuery(link.TargetUrl, ctx.Request.QueryString.Value);

    // 302 Found – běžné pro shortenery; 307 by zachoval metodu, ale pro GET je to jedno
    return Results.Redirect(target, permanent: false);
});

// pomocník: přidá query z aktuálního požadavku k cílové URL (pokud chcete vypnout, prostě nepoužívejte)
static string AppendQuery(string targetUrl, string? incomingQuery)
{
    if (string.IsNullOrEmpty(incomingQuery) || incomingQuery == "?") return targetUrl;
    // pokud target už má query, spojíme & ; jinak nahradíme ?
    return targetUrl.Contains('?')
        ? $"{targetUrl}&{incomingQuery.TrimStart('?')}"
        : $"{targetUrl}{incomingQuery}";
}


app.UseAuthentication();
app.UseAuthorization();
/*
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
*/
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
app.Use(async (ctx, next) =>
{
    if (ctx.User?.Identity?.IsAuthenticated == true)
    {
        using var scope = ctx.RequestServices.CreateScope();
        var om = scope.ServiceProvider.GetRequiredService<OwnerManager>();
        try { await om.EnsureOwnerAsync(ctx.User, ctx.RequestAborted); }
        catch (Exception ex)
        {
            // nechceme brzdit appku, jen zalogovat
            var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                                           .CreateLogger("OwnerEnsure");
            log.LogError(ex, "Nepodařilo se zajistit Owner z claims.");
        }
    }
    await next();
});


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