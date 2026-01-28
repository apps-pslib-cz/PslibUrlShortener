using PslibUrlShortener.Data;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace PslibUrlShortener.Services
{
    public class PeriodicTasksService : BackgroundService
    {
        private readonly ILogger<PeriodicTasksService> _logger;
        private readonly PeriodicTasksOptions _options;
        public IServiceProvider Services { get; }
        private Timer? _timer = null;

        public PeriodicTasksService(IServiceProvider services, ILogger<PeriodicTasksService> logger, IOptions<PeriodicTasksOptions> options)
        {
            Services = services;
            _logger = logger;
            _options = options.Value;
        }

        public override Task StartAsync(CancellationToken stoppingToken)
        {
            base.StartAsync(stoppingToken);
            _logger.LogInformation("PeriodicTasks Service starting.");
            
            // Zpoždìný start - dáme aplikaci èas na inicializaci (30 sekund)
            var initialDelay = TimeSpan.FromSeconds(30);
            
            _timer = new Timer(async (state) =>
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("PeriodicTasks Service cancellation requested.");
                    return;
                }

                _logger.LogInformation("PeriodicTasks Service working on tasks.");
                using (var scope = Services.CreateScope())
                {
                    try
                    {
                        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        
                        // Mazání odkazù oznaèených ke smazání pøed více než X dny
                        var stopwatch = Stopwatch.StartNew();
                        var cutoffDate = DateTime.UtcNow.AddDays(-_options.DaysBeforeDeletion);
                        var res = await context.Links
                            .Where(l => l.DeletedAt != null && l.DeletedAt < cutoffDate)
                            .ExecuteDeleteAsync(stoppingToken);
                        stopwatch.Stop();
                        
                        if (res > 0)
                        {
                            _logger.LogInformation($"{res} links permanently deleted in {stopwatch.ElapsedMilliseconds} ms.");
                        }
                        else
                        {
                            _logger.LogDebug($"No links to delete (checked in {stopwatch.ElapsedMilliseconds} ms).");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Unable to delete old links (will retry next time): {Message}", ex.Message);
                    }
                }
            }, null, initialDelay, TimeSpan.FromSeconds(_options.Seconds));

            return Task.CompletedTask;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PeriodicTasks Service running.");

            return Task.CompletedTask;
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PeriodicTasks Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);
            _timer?.Dispose();

            await base.StopAsync(stoppingToken);
        }
    }

    public class PeriodicTasksOptions
    {
        public int Seconds { get; set; } = 3600; // Kontrola každou hodinu
        public int DaysBeforeDeletion { get; set; } = 30; // Smazání po 30 dnech
    }
}
