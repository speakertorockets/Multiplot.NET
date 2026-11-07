---
name: csharp-15
description: Write, review and modernize C# 15 (.NET 11) code correctly. Covers every C# 15 feature - collection expression arguments (`[with(...), ...]`), union types (`union`, `[Union]`, `IUnion`, custom unions), closed class hierarchies (`closed`), extension indexers, labeled `break`/`continue`, non-virtual static interface members on runtimes without default interface methods, and the preview memory-safety model (`unsafe(expr)`, `safe`, requires-unsafe members). Use when code targets net11.0 or LangVersion 15/preview, when the user mentions any of these features, when replacing marker interfaces, OneOf-style results, `goto`/flag loop exits or capacity/comparer boilerplate, or when the build reports CS8121, CS8509, CS8655, CS8510, CS0121/CS0411 on a `with(...)` element, CS9370, CS9380 or CS9363.
---

# C# 15

C# 15 ships with the .NET 11 SDK and Visual Studio 2026. It is the default language version for
projects that target `net11.0`. Its features are:

| Feature | Keyword / syntax | Needs preview? | Reference |
| --- | --- | --- | --- |
| Collection expression arguments | `[with(args), e1, e2]` | No | [reference/collection-arguments.md](reference/collection-arguments.md) |
| Union types | `union U(A, B);`, `[Union]` | No | [reference/unions.md](reference/unions.md) |
| Closed hierarchies | `closed class` / `closed record class` | No | [reference/closed-hierarchies.md](reference/closed-hierarchies.md) |
| Extension indexers | `this[...]` inside `extension(T x) { }` | No | [reference/extension-indexers.md](reference/extension-indexers.md) |
| Labeled `break` / `continue` | `outer: for (...) { ... break outer; }` | No | [reference/labeled-break-continue.md](reference/labeled-break-continue.md) |
| Non-virtual static interface members on older runtimes | `static` helpers in interfaces on .NET Framework / netstandard | No | [reference/static-interface-members.md](reference/static-interface-members.md) |
| Memory safety model | `unsafe(expr)`, `safe`, requires-unsafe `unsafe` members | **Yes** (`LangVersion=preview` + feature flag) | [reference/memory-safety.md](reference/memory-safety.md) |

## How to use this skill

1. Always apply **section 0** below first.
2. Then read **only** the reference file for the feature you are using (table above). Each file ends
   with the mistakes to avoid and its Microsoft Learn sources.
3. When upgrading a project to the .NET 11 SDK, also read
   [reference/breaking-changes.md](reference/breaking-changes.md).
4. Finish with the agent checklist (section 9) and use the diagnostic table to map compiler errors to
   the right reference file.

---

## 0. Before using any C# 15 feature

1. **Check the language version.** Look at the project's `TargetFramework` and `LangVersion`.
   `net11.0` without a `LangVersion` gives C# 15. To see the active version, temporarily add
   `#error version`: the compiler reports CS8304 with the compiler and language versions.
2. **Don't set `<LangVersion>latest</LangVersion>`.** Microsoft warns against it because it varies
   by machine. Using a language version newer than the target framework's default is unsupported.
   Prefer moving the TFM to `net11.0`.
3. **Union types need runtime types.** `System.Runtime.CompilerServices.UnionAttribute` and
   `System.Runtime.CompilerServices.IUnion` ship in the .NET 11 runtime. The compiler does **not**
   synthesize them. When you target an older TFM, the project must reference or define them.
4. **Know the new contextual keywords.** These can break existing identifiers, and `@` escapes them
   (details in [reference/breaking-changes.md](reference/breaking-changes.md)):
   - `with(` at the start of a collection-expression element: use `@with(...)` to call a method
     named `with`.
   - `union` before a type name in a declaration: use `@union`.
   - `closed` in type declarations and modifier positions: use `@closed`.
   - `safe`, in preview, as a member modifier: use `@safe`.
5. **The SDK is still prerelease** (as of the October 2026 docs). The feature specs may differ from
   the shipped compiler. The Microsoft Learn language reference describes shipped behavior, so it
   wins when they disagree.

---

## 9. Agent checklist

- [ ] Confirm the project is on C# 15 (`net11.0`, or an explicit `LangVersion` of 15). Don't add
      `latest`.
- [ ] Use `[with(...), ...]` instead of construct-then-add boilerplate when a capacity, comparer or
      constructor argument matters. Never use it with arrays or spans.
- [ ] Model "one of these types" with `union`. Model "one of my subclasses" with `closed`. Switch
      on the union or the closed base, not on `.Value` or `object`.
- [ ] In exhaustive switches, prefer explicit `null` arms over `_`, and always handle `null` when
      default union values or nullable inputs can arrive.
- [ ] Seal the leaves of closed hierarchies, and treat their subtype lists as public API.
- [ ] Hand-write a union with `TryGetValue`/`HasValue` for value-type cases on hot paths.
- [ ] Give each labeled loop a unique label in the method. `continue` never targets a `switch`.
- [ ] Don't use extension indexers on arrays or strings, or inside expression trees and
      `IQueryable` queries.
- [ ] Touch the memory-safety preview only when the user has opted in. Keep `unsafe` blocks
      minimal, and document every requires-unsafe member with `/// <safety>`.

## Diagnostic quick reference

| Diagnostic | Meaning in C# 15 context |
| --- | --- |
| CS8121 | A type pattern isn't compatible with any union case type (for example `pet is Pet`). |
| CS8509 | Switch not exhaustive: a missing union case, or an inaccessible closed-hierarchy subtype. |
| CS8655 | Union contents are maybe-null and there's no `null` arm. |
| CS8510 | Unreachable arm (a base arm after its subtypes, or `Car` before `Sedan`). |
| CS0121 / CS0411 | `with(...)` arguments don't pick overloads or infer type arguments. |
| CS9370 / CS9380 / CS1519 | `union`/`closed` used as an identifier. Escape it with `@`. |
| CS9363 | A member with pointer parameters (for example an indexer) called outside `unsafe` (preview). |
| CS9376 | A requires-unsafe parameterless constructor used where `new()`/`struct` is required (preview). |
| CS9392 | An explicit-layout field needs `safe`/`unsafe` (preview). |
| CS9401 / CS0227 | `safe`/`unsafe` modifiers or blocks used without `AllowUnsafeBlocks`. |
| `SwitchExpressionException` | Run time: a `default` union (null contents) reached a switch with no `null` arm. |

Reference file per diagnostic: CS8121/CS8509/CS8655/CS8510 and `SwitchExpressionException` →
`reference/unions.md` (CS8509/CS8510 also `reference/closed-hierarchies.md`); CS0121/CS0411 →
`reference/collection-arguments.md`; CS9370/CS9380/CS1519 → `reference/breaking-changes.md`;
CS9363/CS9376/CS9392/CS9401/CS0227 → `reference/memory-safety.md`.

## Sources

- What's new in C# 15: https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-15
- Roslyn language feature status: https://github.com/dotnet/roslyn/blob/main/docs/Language%20Feature%20Status.md
- Each reference file lists the sources for its feature.
