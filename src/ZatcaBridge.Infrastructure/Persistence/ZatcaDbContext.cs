using Microsoft.EntityFrameworkCore;
using ZatcaBridge.Infrastructure.Persistence.Entities;

namespace ZatcaBridge.Infrastructure.Persistence;

public class ZatcaDbContext : DbContext
{
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<HashChainEntity> HashChainRecords => Set<HashChainEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<CsidCredentialEntity> CsidCredentials => Set<CsidCredentialEntity>();

    public ZatcaDbContext(DbContextOptions<ZatcaDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Invoice entity configuration
        modelBuilder.Entity<InvoiceEntity>(builder =>
        {
            builder.ToTable("Invoices");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.Uuid).IsUnique();
            builder.HasIndex(e => e.InvoiceNumber);

            builder.Property(e => e.InvoiceNumber).HasMaxLength(128).IsRequired();
            builder.Property(e => e.SellerName).HasMaxLength(256).IsRequired();
            builder.Property(e => e.SellerVatNumber).HasMaxLength(15).IsRequired();
            builder.Property(e => e.BuyerName).HasMaxLength(256);
            builder.Property(e => e.BuyerVatNumber).HasMaxLength(15);
            builder.Property(e => e.PreviousInvoiceHash).HasMaxLength(256);

            // CRITICAL RULE 1: decimal(18,2) precision
            builder.Property(e => e.LineExtensionAmount).HasPrecision(18, 2);
            builder.Property(e => e.TaxExclusiveAmount).HasPrecision(18, 2);
            builder.Property(e => e.TaxInclusiveAmount).HasPrecision(18, 2);
            builder.Property(e => e.TotalTaxAmount).HasPrecision(18, 2);
            builder.Property(e => e.PayableAmount).HasPrecision(18, 2);
        });

        // HashChain entity configuration
        modelBuilder.Entity<HashChainEntity>(builder =>
        {
            builder.ToTable("HashChainRecords");
            builder.HasKey(e => e.Icv);
            builder.Property(e => e.Icv).ValueGeneratedNever(); // Strictly managed by domain ICV logic

            builder.Property(e => e.PreviousHash).HasMaxLength(256).IsRequired();
            builder.Property(e => e.InvoiceHash).HasMaxLength(256).IsRequired();
        });

        // OutboxMessage entity configuration
        modelBuilder.Entity<OutboxMessageEntity>(builder =>
        {
            builder.ToTable("OutboxMessages");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => new { e.Status, e.InvoiceCounterValue, e.CreatedAt });

            builder.Property(e => e.InvoiceHash).HasMaxLength(256).IsRequired();
            builder.Property(e => e.PayloadXml).IsRequired();
            builder.Property(e => e.ErrorMessage).HasMaxLength(4000);
        });

        // CsidCredential entity configuration
        modelBuilder.Entity<CsidCredentialEntity>(builder =>
        {
            builder.ToTable("CsidCredentials");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.IsActive);

            builder.Property(e => e.PrivateKeyPem).IsRequired();
            builder.Property(e => e.Secret).HasMaxLength(512);
            builder.Property(e => e.RequestId).HasMaxLength(128);
            builder.Property(e => e.Environment).IsRequired();
            builder.Property(e => e.IsActive).IsRequired();
            builder.Property(e => e.CreatedAt).HasConversion<string>().IsRequired();
        });
    }
}
