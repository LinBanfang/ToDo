using System;

namespace ToDo.Sync;

/// <summary>
/// Hybrid Logical Clock (ADR-018): the source of truth for ModifiedAt. Each device keeps a
/// (physical ms, logical counter) pair plus a stable 8-bit discriminator; writes tick it and
/// applying a remote change merges it, so last-writer-wins resolves causally instead of by
/// whoever's wall clock happens to run fast. Encoded into a single long so every existing
/// comparison (server Merge, client IsLocalNewer, ordering) keeps working unchanged.
/// </summary>
public sealed class HybridClock
{
    // Encoding: 43 bits physical ms | 13 bits logical | 8 bits discriminator.
    // (42 bits of physical are actually usable before the encoded long's sign bit is set —
    //  year ~2109, far beyond what the app needs.)
    private const int DiscriminatorBits = 8;
    private const int LogicalBits = 13;
    private const int LogicalShift = DiscriminatorBits;                 // 8
    private const int PhysicalShift = LogicalBits + DiscriminatorBits;  // 21
    private const long LogicalMask = (1L << LogicalBits) - 1;           // 0x1FFF
    private const long DiscriminatorMask = (1L << DiscriminatorBits) - 1;

    private long _physical;
    private long _logical;
    private readonly long _discriminator;

    public HybridClock(byte discriminator)
    {
        _discriminator = discriminator & DiscriminatorMask;
        _physical = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Restores a persisted high-water mark so a restart (or an NTP rollback)
    /// never lets new writes sort before older ones; takes the greater of the persisted
    /// state and the current wall clock.</summary>
    public HybridClock(byte discriminator, long physical, long logical)
        : this(discriminator)
    {
        var wall = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (physical > wall) { _physical = physical; _logical = logical; }
        else if (physical == wall && logical > _logical) _logical = logical;
        // else keep the fresh wall-clock state.
    }

    /// <summary>Stable 8-bit per-device discriminator derived from the DeviceId GUID
    /// (its last byte), so two devices almost never collide and one device is constant.</summary>
    public static byte DiscriminatorFor(string deviceId) =>
        Guid.TryParse(deviceId, out var g) ? g.ToByteArray()[^1] : (byte)0;

    /// <summary>Advances the clock and returns the encoded timestamp for the next write.</summary>
    public long Tick()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now > _physical)
        {
            _physical = now;
            _logical = 0;
        }
        else if (_logical >= LogicalMask)
        {
            _physical++;      // 13-bit counter exhausted within one ms → borrow the next ms
            _logical = 0;
        }
        else
        {
            _logical++;
        }
        return Encode();
    }

    /// <summary>Merges a remote timestamp so the next local write sorts after everything
    /// this device has seen (causal consistency).</summary>
    public void Observe(long encoded)
    {
        var p = encoded >> PhysicalShift;
        var l = (encoded >> LogicalShift) & LogicalMask;
        if (p > _physical) { _physical = p; _logical = l; }
        else if (p == _physical && l > _logical) _logical = l;
    }

    public long Physical => _physical;
    public long Logical => _logical;

    private long Encode() => Encode(_physical, _logical, _discriminator);

    public static long Encode(long physical, long logical, long discriminator) =>
        (physical << PhysicalShift) | (logical << LogicalShift) | (discriminator & DiscriminatorMask);

    /// <summary>Physical component of an encoded timestamp (only needed where wall-clock is
    /// actually wanted; nothing in the app reads ModifiedAt as a date).</summary>
    public static long DecodePhysical(long encoded) => encoded >> PhysicalShift;
}
