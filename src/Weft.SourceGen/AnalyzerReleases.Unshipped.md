### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
WEFT0004 | Naming | Error | Static fields use the s_ prefix
WEFT0005 | CodeQuality | Error | Metadata projections use Select before iteration
WEFT0006 | Reliability | Error | Disposable locals use structured ownership
WEFT0007 | Reliability | Error | Disposable collections use exception-safe lifetimes
WEFT0008 | Reliability | Error | Disposable locals use explicit cleanup and exception-safe handle or tuple ownership transfer, including configured using blocks
WEFT0009 | CodeQuality | Error | Sequence filters are expressed before iteration
WEFT0010 | CodeQuality | Error | Boolean conditional throws use statement control flow
WEFT0011 | CodeQuality | Error | Initialization-only fields use the readonly modifier
WEFT0012 | CodeQuality | Error | By-reference method state is encapsulated after two parameters
WEFT0013 | CodeQuality | Error | Complex Boolean conditions use named decisions
WEFT0014 | CodeQuality | Error | Deconstructed collection aliases are not mutation-only
WEFT0015 | CodeQuality | Error | Nullable out variables are proved before dereferencing
WEFT0016 | CodeQuality | Error | Redundant nested and class-receiver implicit upcasts are removed
WEFT0017 | CodeQuality | Error | Repeated null tests after exiting guards are removed
WEFT0018 | CodeQuality | Error | Writes to unread locals are removed
WEFT0019 | CodeQuality | Error | Explicit casts to the operand's existing type are removed
WEFT0020 | CodeQuality | Error | Same-target conditional assignments use a conditional expression
WEFT0021 | Reliability | Error | Catch blocks explicitly recover from or propagate exceptions
WEFT0022 | Reliability | Error | Path composition preserves preceding components with Path.Join
WEFT0023 | CodeQuality | Error | Nullable properties are captured before unwrapping
WEFT0024 | Reliability | Error | Catch-all handlers filter failures or rethrow the original exception
WEFT0025 | CodeQuality | Error | Directly nested conditions are combined without changing alternative branches
WEFT0026 | CodeQuality | Error | Hidden visible fields explicitly distinguish base storage
WEFT0027 | CodeQuality | Error | Repeated string accumulation uses a string builder
WEFT0028 | CodeQuality | Error | Asserted nullable locals use typed captures before unwrapping
WEFT0029 | CodeQuality | Error | Captured exceptions use explicit guards before rethrowing
WEFT0030 | CodeQuality | Error | Shared fields are written through static members
WEFT0031 | CodeQuality | Error | Dictionary guards retrieve values in the same lookup
