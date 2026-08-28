using Microsoft.EntityFrameworkCore;
using Wonrich.Auth.Authorization;
using Wonrich.AuthService.Domain;

namespace Wonrich.AuthService.Infrastructure;

/// <summary>Accounts and refresh tokens for the Wonrich authentication service (SCRUM-34).</summary>
public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserAccount>(user =>
        {
            user.ToTable("users");
            user.HasKey(account => account.Id);

            user.Property(account => account.UserName)
                .HasMaxLength(UserAccount.MaxUserNameLength)
                .IsRequired();

            user.Property(account => account.DisplayName)
                .HasMaxLength(UserAccount.MaxDisplayNameLength)
                .IsRequired();

            user.Property(account => account.PasswordHash)
                .HasMaxLength(200)
                .IsRequired();

            user.Property(account => account.Role)
                .HasMaxLength(50)
                .IsRequired();

            user.Property(account => account.Facility)
                .HasMaxLength(UserAccount.MaxFacilityLength);

            user.Property(account => account.IsActive)
                .IsRequired();

            // Sign-in names are stored lower-cased, so a unique index is enough to stop two
            // accounts differing only by case.
            user.HasIndex(account => account.UserName)
                .IsUnique()
                .HasDatabaseName("ux_users_username");
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.ToTable("refresh_tokens");
            token.HasKey(record => record.Id);

            token.Property(record => record.TokenHash)
                .HasMaxLength(64)
                .IsRequired();

            token.Property(record => record.ExpiresAtUtc)
                .IsRequired();

            token.HasIndex(record => record.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_refresh_tokens_hash");

            token.HasOne(record => record.User)
                .WithMany()
                .HasForeignKey(record => record.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>Role names the schema expects, surfaced so seeding and tests agree on them.</summary>
    public static IReadOnlyList<string> ConfiguredRoles => WonrichRoles.All;
}
