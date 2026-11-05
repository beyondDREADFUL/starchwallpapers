using Microsoft.EntityFrameworkCore;
using MVCVideoGuide.Data.Entities;

namespace MVCVideoGuide.Data
{
    public class VideoDbContext : DbContext
    {
        // Forbindelsen (SQLite-filen) konfigureres i Program.cs og gives videre via options.
        public VideoDbContext(DbContextOptions<VideoDbContext> options) : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<NASA> NASAIotd { get; set; }
        public DbSet<Bing> BingIotd { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Samme dato må kun gemmes én gang. Tidligere blev det kun tjekket i controlleren.
            modelBuilder.Entity<NASA>().HasIndex(n => n.Date).IsUnique();
            modelBuilder.Entity<Bing>().HasIndex(b => b.Date).IsUnique();
        }
    }
}
