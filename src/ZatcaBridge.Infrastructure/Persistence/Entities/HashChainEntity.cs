using System;

namespace ZatcaBridge.Infrastructure.Persistence.Entities;

public class HashChainEntity
{
    public long Icv { get; set; }
    public string PreviousHash { get; set; } = string.Empty;
    public string InvoiceHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
