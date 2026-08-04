namespace Hookwright.Core.Identifiers;

/// <summary>
/// A strongly typed identifier backed by a UUIDv7 and rendered as
/// <c>{prefix}_{26 Crockford Base32 characters}</c>.
/// </summary>
/// <typeparam name="TSelf">
/// The implementing type.
/// </typeparam>
public interface IPrefixedId<TSelf> where TSelf : struct, IPrefixedId<TSelf>
{
    /// <summary>
    /// Short lower-case tag identifying the entity kind, such as <c>app</c>
    /// or <c>ep</c>. Makes an identifier self-describing wherever it appears.
    /// </summary>
    static abstract string Prefix { get; }

    /// <summary>
    /// Wraps a value that has already been validated or decoded.
    /// </summary>
    static abstract TSelf FromGuid(Guid value);

    /// <summary>
    /// The underlying UUIDv7.
    /// </summary>
    Guid Value { get; }
}