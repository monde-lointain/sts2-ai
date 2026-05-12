// Sts2Headless.MockWorker — test-only Q8 simulator.
//
// Invocation:
//   dotnet run --project test/mock-worker -- \
//       --base-path /dev/shm/q1-... \
//       --ring-capacity 65536 \
//       --schema 1 \
//       --content-hash <hex32> \
//       --build-id <string> \
//       [--script always-end-turn]
//
// Wire behavior:
//   - Attach to the SHM rings + semaphores created by HookProtocolAdapter.
//   - Send a ManifestResponse echoing exactly the manifest fields supplied
//     on the CLI (the orchestrating test guarantees they match Q1's).
//   - Loop: drain inbound, respond:
//       HookRequest -> HookResponse with an action payload chosen by
//                      --script (default 'echo': echo the request payload
//                      back; 'always-end-turn': payload = "ENDTURN").
//       Terminate   -> exit cleanly.
//   - Stderr: one-line breadcrumb on connect/disconnect. Stdout: silent.
//
// Latency:
//   The worker drains the inbound ring eagerly per wakeup and minimizes work
//   per frame. This matters for the latency-gate test in T6, which measures
//   the FULL roundtrip (Q1 -> worker -> Q1).

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Sts2Headless.Adapters.HookProtocol;

if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    Console.Error.WriteLine("mock-worker requires Linux");
    return 2;
}

string? basePath = null;
int ringCapacity = HookProtocolAdapter.DefaultRingCapacity;
ushort schema = HookProtocolAdapter.SchemaVersion;
string? contentHashHex = null;
string buildId = "mock-worker";
string script = "echo";

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"missing value for {a}");
    switch (a)
    {
        case "--base-path": basePath = Next(); break;
        case "--ring-capacity": ringCapacity = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--schema": schema = ushort.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--content-hash": contentHashHex = Next(); break;
        case "--build-id": buildId = Next(); break;
        case "--script": script = Next(); break;
        case "--help":
        case "-h":
            Console.WriteLine("mock-worker --base-path <p> --ring-capacity <n> --schema <u16> --content-hash <hex64> [--build-id <s>] [--script echo|always-end-turn]");
            return 0;
        default:
            Console.Error.WriteLine($"unknown arg: {a}");
            return 2;
    }
}

if (basePath is null || contentHashHex is null)
{
    Console.Error.WriteLine("missing required --base-path / --content-hash");
    return 2;
}
if (contentHashHex.Length != HookProtocolManifest.ContentHashSize * 2)
{
    Console.Error.WriteLine($"--content-hash must be {HookProtocolManifest.ContentHashSize * 2} hex chars");
    return 2;
}

byte[] contentHash = Convert.FromHexString(contentHashHex);
var manifest = new HookProtocolManifest(contentHash, schema, ringCapacity, buildId);
int total = SpscRingBuffer.HeaderSize + ringCapacity;

// Attach to Q1's pre-created rings + semaphores.
using var inboundShm = SharedMemorySegment.OpenExisting(
    HookProtocolAdapter.OutboundShmPathFor(basePath), total);   // Q1 -> Q8 (we READ)
using var outboundShm = SharedMemorySegment.OpenExisting(
    HookProtocolAdapter.InboundShmPathFor(basePath), total);    // Q8 -> Q1 (we WRITE)
SpscRingBuffer inboundRing;
SpscRingBuffer outboundRing;
unsafe
{
    inboundRing = new SpscRingBuffer(inboundShm.BasePtr, ringCapacity, initializeHeader: false);
    outboundRing = new SpscRingBuffer(outboundShm.BasePtr, ringCapacity, initializeHeader: false);
}

using var inboundSem = PosixSemaphore.Open(HookProtocolAdapter.OutboundSemNameFor(basePath));   // q1 releases
using var outboundSem = PosixSemaphore.Open(HookProtocolAdapter.InboundSemNameFor(basePath));   // we release

Console.Error.WriteLine($"mock-worker attached: base-path={basePath} ring={ringCapacity} schema={schema}");

// Send ManifestResponse immediately. The orchestrator pre-validated the
// manifest fields, so Q1 will accept this and proceed.
void SendFrame(MessageType type, ulong correlationId, ReadOnlySpan<byte> body)
{
    var hdr = new MessageHeader(type, schema, body.Length, correlationId);
    byte[] wire = new byte[hdr.WireSize];
    MessageFrame.Encode(hdr, body, wire);
    while (!outboundRing.TryWrite(wire))
    {
        Thread.SpinWait(64);
    }
    outboundSem.Release();
}

{
    byte[] mbuf = new byte[manifest.EncodedSize];
    manifest.Encode(mbuf);
    SendFrame(MessageType.ManifestResponse, 0, mbuf);
}

// Echo loop.
byte[] endTurn = System.Text.Encoding.ASCII.GetBytes("ENDTURN");
byte[] headerScratch = new byte[MessageHeader.HeaderSize];
while (true)
{
    if (!inboundSem.Wait(TimeSpan.FromSeconds(30)))
    {
        // 30 s of silence — supervisor probably wedged. Exit.
        Console.Error.WriteLine("mock-worker: timed out waiting for inbound; exiting");
        return 0;
    }
    // Drain all frames signaled.
    while (true)
    {
        if (inboundRing.AvailableToRead < MessageHeader.HeaderSize) break;
        if (!inboundRing.TryPeek(headerScratch)) break;
        var hdr = MessageHeader.Decode(headerScratch);
        int wire = hdr.WireSize;
        if (inboundRing.AvailableToRead < wire) break;
        byte[] frameBuf = new byte[wire];
        inboundRing.TryRead(frameBuf);
        var payload = new ArraySegment<byte>(frameBuf, MessageHeader.HeaderSize, hdr.PayloadLength);

        switch (hdr.Type)
        {
            case MessageType.HookRequest:
            {
                ReadOnlySpan<byte> respBody;
                if (script == "always-end-turn")
                {
                    respBody = endTurn;
                }
                else
                {
                    respBody = payload.AsSpan();
                }
                SendFrame(MessageType.HookResponse, hdr.CorrelationId, respBody);
                break;
            }
            case MessageType.Terminate:
                Console.Error.WriteLine("mock-worker: received Terminate; exiting");
                return 0;
            case MessageType.ManifestRequest:
                // Q1 may resend; echo our manifest.
                byte[] mbuf = new byte[manifest.EncodedSize];
                manifest.Encode(mbuf);
                SendFrame(MessageType.ManifestResponse, hdr.CorrelationId, mbuf);
                break;
            default:
                Console.Error.WriteLine($"mock-worker: unexpected type {hdr.Type}; dropping");
                break;
        }
    }
}
