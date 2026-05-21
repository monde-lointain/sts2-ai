namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Observer callback for effect actions. Attached to an
/// <see cref="ExecutionContext"/> at construction; each <see cref="IAction"/>
/// invokes <see cref="Record"/> from its <c>Execute</c> when an observer is
/// present (null-conditional).
///
/// <para>
/// Per-context (not thread-static) — multiple concurrent contexts each have
/// their own observer. Replaces the legacy <c>EffectObserver._log</c>
/// thread-static slot (deleted in wave-39 stream C.3).
/// </para>
/// </summary>
public interface IActionObserver
{
    void Record(IAction action);
}
