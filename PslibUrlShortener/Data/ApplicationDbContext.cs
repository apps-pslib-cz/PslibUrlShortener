using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;

namespace PslibUrlShortener.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Link> Links => Set<Link>();
        public DbSet<LinkHit> LinkHits => Set<LinkHit>();
        public DbSet<ReservedCode> ReservedCodes => Set<ReservedCode>();
        public DbSet<Owner> Owners => Set<Owner>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<Link>()
                .HasIndex(x => new { x.Domain, x.Code })
                .IsUnique()
                .HasFilter("[DeletedAt] IS NULL");

            // Link: rychlé filtrování podle vlastníka
            b.Entity<Link>()
                .HasIndex(x => x.OwnerSub);

            // LinkHit: vztah a indexy
            b.Entity<LinkHit>()
                .HasOne(h => h.Link)
                .WithMany()
                .HasForeignKey(h => h.LinkId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<LinkHit>()
                .HasIndex(h => h.LinkId);

            b.Entity<LinkHit>()
                .HasIndex(h => h.AtUtc);

            b.Entity<ReservedCode>()
                .HasIndex(x => x.Code)
                .IsUnique();

            b.Entity<Owner>()
                .HasIndex(o => o.Email);
        }
    }
}