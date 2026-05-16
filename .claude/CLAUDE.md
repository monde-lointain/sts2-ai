## C++ Coding Requirements

- Follow the C++ Core Guidelines in all code: safety, clarity, RAII, value semantics, and avoidance of undefined behavior.
- Adhere to established C++ coding style conventions: consistent naming, concise comments only where needed, clear ownership, and minimal global state.
- Prefer modern C++ features: smart pointers over raw pointers, range-based loops, `auto` where it improves clarity, `constexpr` when appropriate.
- Enforce strong type safety; avoid implicit conversions and unsafe casts.
- Use exceptions for error handling unless performance constraints dictate otherwise; never use error codes for normal flow.
- Ensure thread safety, avoid data races, and use standard concurrency utilities.
- Optimize only when necessary and never at the expense of readability.
- Write code that compiles cleanly on modern C++ compilers with warnings enabled and treated as errors.
