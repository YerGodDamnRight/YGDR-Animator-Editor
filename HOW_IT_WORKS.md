# How It Works

YGDR Animator Editor adds features to Unity's built-in Animator window without modifying Unity's source code. It does this using **Harmony** — a runtime patching library that intercepts existing methods and runs additional code before, after, or instead of them.

---

## Harmony Patches

Harmony lets you hook into any C# method at runtime by injecting code around it.

There are three patch types:

| Type | When it runs | Common use |
|---|---|---|
| `Prefix` | Before the original method | Intercept input, cancel execution |
| `Postfix` | After the original method | Modify output, add UI on top |
| `Transpiler` | Rewrites the method's IL | Inject calls mid-method (e.g. context menus) |

Most features in this package use **Postfix** patches to draw additional UI over the Animator graph, or **Transpiler** patches to inject context menu callbacks into Unity's internal node drawing methods.

Because Unity's Animator window internals are not public, types and methods are resolved at runtime using `AccessTools` (part of Harmony's reflection utilities):

```csharp
AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI")
AccessTools.Method(graphGUIType, "OnGraphGUI")
```

---

## Patch Structure

Every patch follows the same shape:

```csharp
[HarmonyPatch]
internal static class PatchSomething
{
    static MethodBase TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI"),
            "OnGraphGUI");

    static void Postfix(object __instance)
    {
        // runs after Unity's OnGraphGUI
        // draw custom UI, handle input, etc.
    }
}
```

All patches are registered at editor startup via `[InitializeOnLoad]` in `AnimatorEditorInit`.

---

## Special Harmony Parameters

Declare these in any patch method — Harmony injects them automatically by name:

| Parameter | Type | What it is |
|---|---|---|
| `__instance` | `object` | `this` of the patched method |
| `__result` | matches return type | Return value — Postfix can read or overwrite it |
| `__state` | any | Pass data from a Prefix into the matching Postfix |
| `___fieldName` | matches field type | Direct access to a private field (three underscores) |

Named parameters matching the original method's signature are also injected by name:

```csharp
// original: void NodeUI(bool isSelected)
static void Postfix(object __instance, bool isSelected)
{
    // isSelected injected from the original method's argument
}
```

---

## Bootstrap Timing — `TargetMethod()` Cannot Use the Cache

`AnimatorEditorInit` caches internal Unity types at startup (`GraphGUIType`, `StateNodeType`, etc.). These are available inside patch method bodies — but **not** inside `TargetMethod()`.

Harmony resolves patch targets before `AnimatorEditorInit` finishes. Cached fields are still `null` at that point. Using them in `TargetMethod()` silently skips the patch with no error.

```csharp
// WRONG — cache is null at patch time, patch silently never applies
static MethodBase TargetMethod() =>
    AccessTools.Method(AnimatorEditorInit.GraphGUIType, "OnGraphGUI");

// CORRECT — resolve inline every time
static MethodBase TargetMethod() =>
    AccessTools.Method(
        AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI"),
        "OnGraphGUI");
```

| Location | Use cache? |
|---|---|
| `TargetMethod()` / `TargetMethods()` | No — inline only |
| `Prefix()`, `Postfix()`, `Transpiler()` body | Yes — cache is populated by then |

---

## Finding Methods with ILSpy

Unity's Animator window internals are in `UnityEditor.dll`. To find what to patch:

1. Open ILSpy and load `UnityEditor.dll` (found in your Unity install under `Editor/Data/Managed/UnityEditor.dll`)
2. Browse to `UnityEditor.Graphs.AnimationStateMachine` for graph/node types, or `UnityEditor.Animations` for controller types
3. Find the method you want to hook — note the exact type name and method name
4. Use `AccessTools.TypeByName("full.type.Name")` and `AccessTools.Method(type, "MethodName")` in `TargetMethod()`
5. Check parameter names in ILSpy — they can be injected into your patch by name

The most commonly patched types in this package:

| Internal type | What it controls |
|---|---|
| `GraphGUI` | Main graph canvas, input, drawing |
| `StateNode` | Individual state nodes |
| `EdgeGUI` | Transition arrows |
| `GraphBottomBar` | Bottom bar of the Animator window |

---

## Operations Layer

Patches only handle **input and UI**. Any actual mutation of the animator controller (adding states, editing transitions, reordering layers) is handled in a separate operations layer — plain C# classes with no Harmony or reflection:

```
AnimatorEditorInit     ← startup, reflection cache
    └── Patches        ← Harmony hooks, UI, input
            └── Ops    ← pure C# animator mutations
```

This keeps patching logic isolated from data logic, making features easier to test and maintain.

---

## Adding a Feature

1. Identify the Unity internal method to hook using ILSpy
2. Write a patch class targeting that method
3. Implement the feature logic in a separate `Ops` class
4. Register nothing — all patches are discovered automatically by Harmony at startup

---

## Common Pitfalls

**Mutating a `GUIStyle` in-place causes an infinite repaint loop.**
Unity's GUI system repaints whenever styles change. If you write to a shared `GUIStyle` directly, every repaint changes it, which triggers another repaint.
Always copy first:
```csharp
// WRONG
GUIStyle style = GUI.skin.label;
style.normal.textColor = Color.red; // mutates the shared style

// CORRECT
GUIStyle style = new GUIStyle(GUI.skin.label);
style.normal.textColor = Color.red;
```

**Not calling `Event.current.Use()` leaks input.**
If your patch handles a mouse click or key press, call `Event.current.Use()` after handling it. Without it the event propagates to Unity's code and may trigger unintended behaviour (state selection, drag, etc.).

**Drawing outside `EventType.Repaint` causes flickering or errors.**
Only draw GUI elements during `Repaint` events. Wrap draw calls:
```csharp
if (Event.current.type == EventType.Repaint)
{
    // draw here
}
```

**`AccessTools.Field` hits the wrong type when inheritance is involved.**
If the field is defined on a base class, `AccessTools.Field(derivedType, "fieldName")` may return `null`. Pass the type that actually declares the field, not the runtime type of the instance.
