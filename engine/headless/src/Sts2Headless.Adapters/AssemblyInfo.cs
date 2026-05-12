using System.Runtime.CompilerServices;

// The state-codec primitives (ByteWriter, ByteReader) are internal — they are
// implementation details of the StateCodec class. Tests under
// Sts2Headless.Tests.Adapters drill into the primitive shape (LE byte layout,
// EOF behavior) so the high-level codec contract can rest on a verified
// foundation. InternalsVisibleTo is the standard .NET pattern for this.
[assembly: InternalsVisibleTo("Sts2Headless.Tests.Adapters")]
