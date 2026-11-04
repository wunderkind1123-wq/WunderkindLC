using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>CRM'dan kiritilgan, shifrlangan tizim kalitlari (Instagram/Meta) — `StoredSecrets` jadvali.</summary>
public partial class AppDbContext
{
    public DbSet<StoredSecret> StoredSecrets => Set<StoredSecret>();

    partial void ConfigureStoredSecrets(ModelBuilder b)
    {
        b.Entity<StoredSecret>(e =>
        {
            e.ToTable("StoredSecrets");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Cipher).IsRequired();
            e.Property(x => x.UpdatedAt).HasMaxLength(32);
            e.Property(x => x.UpdatedBy).HasMaxLength(200);
        });
    }
}
