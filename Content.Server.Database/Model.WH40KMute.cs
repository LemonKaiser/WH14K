using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

internal static class ModelWH40KMute
{
    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WH40KMute>()
            .HasOne(m => m.Player)
            .WithMany()
            .HasForeignKey(m => m.PlayerUserId)
            .HasPrincipalKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WH40KMute>()
            .HasOne(m => m.CreatedBy)
            .WithMany()
            .HasForeignKey(m => m.CreatedById)
            .HasPrincipalKey(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<WH40KUnmute>()
            .HasOne(u => u.UnmutingAdmin)
            .WithMany()
            .HasForeignKey(u => u.UnmutingAdminId)
            .HasPrincipalKey(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<WH40KUnmute>()
            .HasOne(u => u.Mute)
            .WithOne(m => m.Unmute)
            .HasForeignKey<WH40KUnmute>(u => u.MuteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

[Table("wh40k_mute")]
[Index(nameof(PlayerUserId))]
[Index(nameof(PlayerUserId), nameof(Type))]
public sealed class WH40KMute
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, ForeignKey(nameof(Player))]
    public Guid PlayerUserId { get; set; }

    public Player Player { get; set; } = null!;

    [Required]
    public int Type { get; set; }

    [Required, MaxLength(4096)]
    public string Reason { get; set; } = string.Empty;

    [ForeignKey(nameof(CreatedBy))]
    public Guid? CreatedById { get; set; }

    public Player? CreatedBy { get; set; }

    [Required]
    public DateTime MuteTime { get; set; }

    public DateTime? ExpirationTime { get; set; }

    public WH40KUnmute? Unmute { get; set; }
}

[Table("wh40k_unmute")]
public sealed class WH40KUnmute
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required, ForeignKey(nameof(Mute))]
    public int MuteId { get; set; }

    public WH40KMute Mute { get; set; } = null!;

    [ForeignKey(nameof(UnmutingAdmin))]
    public Guid? UnmutingAdminId { get; set; }

    public Player? UnmutingAdmin { get; set; }

    [Required]
    public DateTime UnmuteTime { get; set; }
}
