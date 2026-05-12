// HookHandler — delegate type for hook callbacks. Sync void; receives the
// HookContext (which carries the ambient ExecutionContext + per-hook payload
// shape, expanded in later stages).
//
// Why a delegate, not an interface:
//   Upstream uses C# events on AbstractModel — effectively delegates already.
//   Our hook callbacks are typically single-method, single-purpose closures;
//   an interface would force a class per hook-handler shape for no gain.
//   When S5 starts attaching hooks from CardModel/RelicModel, the model
//   subscribes its own method via `reg.Subscribe(type, new HookRegistration(MyOnHook, ...))`.

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Hook callback. Runs synchronously inside <see cref="HookRegistry.Fire"/>.
/// May enqueue follow-up actions via <see cref="HookContext.Execution"/>.<see cref="ExecutionContext.Queue"/>.
/// MUST NOT throw on legal inputs.
/// </summary>
public delegate void HookHandler(HookContext ctx);
