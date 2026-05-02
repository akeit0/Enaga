# Layout measurement notes

This note records the current findings around intrinsic measurement and stack layout.

## Core problem

The current runtime still has places where layout measurement is derived from **React element trees** rather than from the **real committed host tree**.

That becomes unsafe when the measurement path tries to understand composite children by effectively evaluating them during measurement.

Observed failure mode:

1. a stack/container wants child intrinsic size
2. measurement expands a composite child
3. the composite child uses hooks (`useHostState`, `useSyncExternalStore`, etc.)
4. the measurement path is not a normal React render
5. hook dispatch is invalid or partially initialized
6. result: crash, undefined behavior, or invisible layout

Concrete example:

- replacing
  - `badges={<Badge text={\`Frame ${host.frame}\`} />}`
- with
  - `badges={<FrameBadge />}`
- exposed this because `FrameBadge` reads host state via a hook.

## Important conclusion

**App-side explicit measurement is not the right direction.**

We intentionally avoided moving more layout knowledge into:

- `examples\SampleApp\src\app\catalog-ui.tsx`
- page components
- app-only wrappers

Doing that would:

1. duplicate renderer/layout rules in app code
2. make sample helpers fatter over time
3. hide runtime limitations instead of fixing them
4. push arbitrary measurement contracts onto app authors

That is the opposite of the intended architecture.

## Desired direction

The durable fix should move **down** into the runtime and native side:

1. **Do not execute composite components for measurement**
   - no render-function expansion in intrinsic measurement
   - no hook-bearing component evaluation outside React

2. **Prefer committed host-tree layout over element-tree measurement**
   - let React perform the real render once
   - derive layout from the host nodes that actually exist after reconciliation
   - this naturally handles composite wrappers like `FrameBadge`

3. **Push stack/intrinsic math toward the native side**
   - keep generic container measurement in `native-runtime.tsx` only as glue
   - prefer C# for reusable stack sizing / distribution work
   - avoid app-level helper-specific sizing logic

## Practical runtime rule

If a layout path needs information from a child component, it should prefer one of these:

1. data already present on the committed host node tree
2. native stack/layout calculation
3. explicit primitive-level measurements (`text`, `image`, host controls)

It should **not** fall back to "call the child component and see what it returns."

## Why host-tree layout is the better root fix

Using the committed host tree:

- respects normal React hook semantics
- works for composite wrappers without requiring app-specific measurement shims
- reduces pressure to make every app helper "measurement aware"
- keeps the abstraction boundary where it belongs: runtime/layout engine, not app code

## What to avoid in follow-up work

- adding more explicit measure helpers to sample pages
- adding more layout intelligence to `catalog-ui.tsx`
- requiring wrapper components to carry bespoke intrinsic contracts just to render correctly

## Good follow-up targets

When revisiting this area, inspect these together:

- `lib\Enaga.React\src\native-runtime.tsx`
- `src\Enaga.React.OkojoRuntime\Rendering\NativeStackLayoutCalculator.cs`
- `src\Enaga.React.OkojoRuntime\Scripting\OkojoNodeReactHost.cs`

The next milestone should be:

**stack layout and intrinsic measurement that operate on runtime/host data without composite render expansion.**
