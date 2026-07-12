# YGDR Animator Editor Docs

![Unity](https://img.shields.io/badge/Unity-2022.3_LTS-black.png?logo=unity "=125")
![VRChat SDK](https://img.shields.io/badge/VRChat_SDK-3.x-blue.png?logo=vrchat)
![Harmony](https://img.shields.io/badge/Harmony-2.x-orange.png)

Powerful Unity Editor tool for advanced animator controller editing. Extends Animator window with multi-transition editing, state property management, VRC-specific features, and graph enhancements.

Open via **YGDR → Animator Editor → Open**.

---

## Contents

- [Glossary](#glossary)
- [Custom Editor Window](#custom-editor-window)
  - [Transitions Tab](#transitions-tab)
  - [States Tab](#states-tab)
  - [Controller Tab](#controller-tab)
  - [Settings Tab](#settings-tab)
- [Layer Panel Enhancements](#layer-panel-enhancements)
  - [Toggle Layer Creator](#toggle-layer-creator)
- [Graph Window Enhancements](#graph-window-enhancements)
- [Blend Tree Enhancements](#blend-tree-enhancements)
- [Bottom Bar](#bottom-bar)
- [Frames](#frames)
- [Graph Node Analysis](#graph-node-analysis)
- [Constraint Converter](#constraint-converter)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Bug Fixes & Compatibility](#bug-fixes--compatibility)
- [Undo Safety](#undo-safety)

---

## Glossary

| Term | Definition |
|---|---|
| **AAP** | Animator-Animated Parameter. A parameter whose value is driven by an animation clip rather than transitions/scripts. |
| **WD** | Write Defaults. Per-state toggle controlling whether unanimated properties reset to defaults on state entry. |
| **Sub-State Machine** | Nested state machine inside a layer. Groups related states. Has Entry, Exit, Any State, parent-machine references. |
| **Multi-Transition** | Multiple transitions sharing same source and destination states. Useful for OR-logic conditions. |
| **Frame** | Sticky-note overlay on graph. Annotates and groups nodes. Stored as hidden sub-asset on controller. |
| **Direct Blend Tree** | Blend tree type where each child weight is driven by a separate parameter directly. Requires WD on. |
| **Network Sync** | Pattern for syncing local-only parameters across VRChat clients via int/bool encoded sync parameters. |
| **Clip Remapper** | Tool fixing broken `AnimationClip` bindings when hierarchy paths change. |
| **AnyState Transition** | Transition originating from special AnyState node. Fires from any state in the layer when conditions met. |
| **Interruption Source** | Setting controlling which transitions can override an in-progress transition. |
| **VRC Parameter Driver** | `StateMachineBehaviour` setting/changing parameter values on state enter/exit. |
| **VRC Tracking Control** | `StateMachineBehaviour` toggling player tracking vs animation per body region. |
| **VRC Locomotion Control** | `StateMachineBehaviour` enabling or disabling avatar locomotion. |
| **VRC Animator Layer Control** | `StateMachineBehaviour` blending a specific animator sub-layer's weight over time. |
| **VRC Playable Layer Control** | `StateMachineBehaviour` blending an entire playable layer's weight over time. |
| **VRC Temporary Pose Space** | `StateMachineBehaviour` entering or exiting avatar pose space with optional delay. |
| **Harmony** | Runtime patching library. Used to inject features into Unity Editor internals. |

---

## Custom Editor Window

Tabbed interface for editing currently selected object(s) in Animator graph. Updates automatically as selection changes in Animator window.

### Transitions Tab

Edit multiple transitions at once. Select one or more transitions in Animator graph → tab shows all selected together → mass-edit shared properties. Toggle pill button on right to collapse displayed transition tags to scrollable list.

**Transition Details** — Edit timing (exit time, duration), interruption settings, atomic flags. Changes sync to Animator graph in real time.

**Condition Rows** — Each row displays parameter name, comparison mode, threshold value.

**All Conditions Mode** — Displays all conditions for all selected transitions, grouped by source. Tab between fields to enter values quickly.

**Shared Conditions Mode** — `+` adds shared condition (parameter, mode, threshold) to every selected transition. Supports all condition types:
- Bool: `If` / `IfNot`
- Int: `Equals` / `NotEqual` / `Greater` / `Less`
- Float: `Greater` / `Less`

Tool detects duplicate parameters across transitions and shows warning in either mode.

**Reverse** — `⇄` swaps all transition conditions (`Equals` → `NotEqual`, `Greater` → `Less`).

**Merge & Separate** — Tab detects multi-transitions (same source and destination). Offers options to merge or break apart.

---

### States Tab

Select one or more state nodes → tab shows properties for all selected. Collapse with right-side pill button.

**State List** — Each selected state has `In` / `Out` buttons to quickly select relevant transitions.

**Align States** — Buttons to vertically/horizontally & align/distribute all selected states. Useful for organizing complex state machines.

**State Properties** — Edit names (appends `#1`, `#2`…`#n` to subsequent selected nodes to prevent duplicates), speed, motion (animation clip), cycle offset, write defaults, mirror, foot IK toggles, tag. Motion fields show preview of assigned clip, accept drag-drop, and display `-` on mixed values. Tag field sets `AnimatorState.tag` — used to match Color Tags defined in Settings.

#### Shared Behaviors

VRC Parameter Drivers, VRC Play Audio, VRC Tracking Control, VRC Locomotion Control, VRC Animator Layer Control, VRC Playable Layer Control, VRC Temporary Pose Space.

> [!IMPORTANT]
> VRC features require VRChat SDK installed. Without SDK, these sections will not appear.

Each section has **Add to All** / **Remove All** buttons in its header. Sections only appear when at least one selected state has the component.

**VRC Parameter Driver, VRC Play Audio, VRC Animator Layer Control, and VRC Playable Layer Control support multiple instances per state.** Clicking **Add to All** again adds another instance instead of replacing the existing one — each gets its own named, collapsible row (rename by editing the name field). Use the **↑ / ↓** buttons next to a row's name to reorder that instance among the others (arrows gray out at the top/bottom row). Use the **−** next to a row's name to remove just that one instance across selected states; **Remove All** clears every instance of that type instead. States are matched up by instance name, so editing, reordering, or removing a row only affects the selected states that actually have an instance with that name.

**VRC Parameter Driver** — Add or edit shared drivers across selected states. Rows are reorderable. Each row specifies type (`Set` / `Add` / `Random` / `Copy`), parameter name, and value. `Copy` type has Source, Destination, and Convert Range fields. New rows default to the first unused controller parameter. Click `-` to remove a row. Removing all rows removes the component.

**VRC Play Audio** — Configure shared play-audio behaviour: source path (drag `AudioSource` to resolve), playback order, clips list (reorderable), volume/pitch min/max ranges, loop toggle, on-enter/on-exit play/stop flags, delay.

**VRC Tracking Control** — Override tracking on shared states for head, hands, feet, hips, fingers, eyes, eyelids, mouth, jaw. Use **Set All** row to apply one value across all body regions at once.

| Color | Meaning |
|---|---|
| Green | Tracking |
| Yellow | Animation |
| Blue | Mixed values across selection |

**VRC Locomotion Control** — Enable or disable avatar locomotion. Two-button toggle: **Disable** / **Enable** — active button shows green text.

**VRC Animator Layer Control** — Blend a specific animator sub-layer's weight over time. Fields:

| Field | Description |
|---|---|
| Playable | Playable layer to affect |
| Layer | Index of sub-layer to affect |
| Goal Weight | Target weight (0–1 slider) |
| Blend Duration | Time in seconds to reach goal weight |

**VRC Playable Layer Control** — Blend an entire playable layer's weight over time (Action / FX / Gesture / Additive). Fields: Layer enum, Goal Weight (0–1 slider), Blend Duration.

**VRC Temporary Pose Space** — Enter or exit avatar pose space. Two-button toggle: **Enter** / **Exit** — active button shows green text. Fixed Delay toggle switches delay interpretation between seconds and normalized %.  

---

### Controller Tab

Shows currently active `AnimatorController` and management tools.

**Overview** — Tabs for Per-Layer Write Defaults, Network Sync, Sub-Assets. Includes dedicated `Clean` button for controllers with orphaned sub-assets.

#### Write Defaults

Two-column layer list with WD on/off state. Buttons for setting individual layers or all layers on/off. Mixed layers listed at bottom when present.

#### Network Sync

One-click network syncing for chosen layer. Options:

- Sync parameter type (int vs bool encoded)
- Transition type (All-to-All / Any-State)
- Toggle to preserve transition properties
- Name for newly created sync parameters (duplicates blocked)
- Prefix added to front of all networked states
- Toggle to remove state behaviors for network states
- Pack into sub-state machine node for clean layers
- **Own Driver Instance** — When on, Network Sync writes to its own dedicated Parameter Driver (named "Network") instead of sharing an existing driver already on the state, so it never touches driver rows you've set up for other purposes.

#### Sub-Assets

Sub-tabs listing all layers, states, blend trees, and clips in controller. Each entry shows warning icons for empty layers, invalid transitions, empty motion fields, and broken animation bindings. Searchable. Click item → focuses in graph.

#### Clip Remapper

Fix broken animation clip bindings.

- Drag GameObject (with `Animator` + controller) into field → enables scan button → flags broken bindings. Up to 5 broken path segments shown as clickable buttons — click one to auto-fill the From path field.
- **From / To path fields** — Each has a drag-and-drop GameObject slot. Drop a GO onto it → full hierarchy path auto-fills the text field.
- **Auto-Repath** — Automatically updates bindings on hierarchy GameObject rename/move. Tracks only bindings that were valid when toggled on.
- Select clip from list → focuses asset in Project window. Select multiple clips in Project → list highlights in green → direct remap available. List shows only clips belonging to avatar in slot.

Clip Remapper integration based on [hfcRed's Animation-Repathing](https://github.com/hfcRed/Animation-Repathing).

---

### Settings Tab

Tool-wide configuration. Persisted in `EditorPrefs` → available cross-project.

#### Interface

**Language** — Dropdown at top of section. Switches all UI labels to selected language. Persisted in `EditorPrefs` → applies cross-project. Supported: English, Français, Deutsch, 日本語, 한국어, Español, 简体中文.

UI toggles.

- **Layer Indicators** — WD / Frames / Empty indicators on controller layers
- **Type Icons** — Float / Int / Bool / Trigger icons with custom color pickers on parameters list
- **VRC Icons** — VRC parameter icons (same color pickers)
- **AAP Icons** — Marks parameters controlled by a clip → click to find affected states/clips
- **Graph Footer** — Shows selected node/transition count + current operation mode
- **VRC Comp Icons** — Marks parameters bound to VRC contact / physbone / raycast components → click to locate component. Also shows sync status and saved status → click either icon to toggle that flag on the VRC expression parameter. Parameter default value stays linked both ways with its VRC expression parameter (editing either side updates the other); if both change at once, the VRC expression parameter's value wins.
- **Param Budget** — Displays current parameters, synced count, total allowed
- **Empty Params** — Highlights parameters with no usages in the controller

Color pickers:
- **Primary / Secondary / Accent** — Adjust full interface palette
- **Graph Analysis** — Adjust highlight indicator colors

#### Graph Background

Change graph background color, replace with image (transparency adjustable), toggle gridlines, change major/minor line colors, adjust scale/divisions.

#### Node Colors

Toggle 3D vs flat state nodes. Assign custom colors for selection, state nodes, blend tree nodes.

#### Node Icons

Overlay icons for nodes. Available: empty node, looping animation, WD on/off, contains behaviors, parameter affecting speed, parameter affecting motion, clip name in node, clip time/duration in node, node coordinates in graph. Custom active/inactive colors and names.

#### Transition Overlay

- **Labels** — Show condition/threshold for single transitions, count for multiple, `invalid` for null transitions. Show VRC hand gesture names when parameter is `GestureLeft` / `GestureRight` and uses `=` or `≠`.
- **Expanded Conditions Box** — When Labels is on and exactly one transition edge is selected, hold <kbd>Alt</kbd> → expanded overlay box appears above nodes showing all conditions for that edge without truncation, one per line.
- **Selection Colors** — Color pickers for default, incoming, outgoing transition lines when single node selected.
- **Indicator Arrows** — Arrow cap color for default, invalid, instant (0 duration) transitions.
- **Animate** — Animated arrow caps for selected transitions, or transitions referenced by selected nodes.

#### Transition Defaults

Default settings for newly created transitions.

#### State Defaults

Default settings for newly created state nodes.

#### Keybinds

Rebind graph shortcuts. Click a binding slot → press desired key combination → saves automatically. Supports modifier keys (Ctrl / Shift / Alt) + any key.

See [Rebindable Shortcuts](#rebindable-shortcuts-defaults) for the full action list.

#### Miscellaneous

- **WD Blend Trees** — Controller WD section can change/detect blend tree WD status. Disable for direct blend trees (require WD on).
- **Prevent Layer Scroll** — Stops Unity scrolling layer list to top on new layer creation.
- **Prevent Param Scroll** — Same behavior for parameters list.
- **Default Weight 1** — New layers auto-set weight to `1`.
- **Clip Menu Nesting** — Nest Animation window clips in sub-menus by name using a searchable advanced dropdown. Choose the separator character — `-`, `.`, or `_` — and name clips `parent<sep>child<sep>name`. Disabling falls back to Unity's stock clip popup.
- **Layer Templates** — Replaces layer `+` button with dropdown ([see below](#layer-templates)).
- **Param Add Menu** — Parameter `+` button gains quick options for VRC built-in parameters. Right-click parameter adds:
  - Add parameter below
  - Convert to Float / Int / Bool / Trigger → submenu with two independent actions:
    - **Controller** — converts type and auto-updates all references in the controller (transitions, behaviours, AAP clips)
    - **VRC Params** — converts the matching VRC expression parameter type independently (use for type mismatches without touching controller references)
  - Set Synced / Set Not Synced → toggle VRC sync status on the parameter
  - Add to VRC Parameters → adds parameter to VRC expression parameters asset
  - Add All to VRC Parameters → bulk-adds all controller parameters to VRC expression parameters
  - Find parameter uses → opens window showing where parameter is used (transitions, behaviors, AAP clips, affecting GameObjects) + threshold conditions
  - Find AAP Uses → opens window listing all states/clips controlling parameter
  - Create AAP → creates an AAP animation clip that drives the parameter
  - Remove AAP → removes the AAP clip driving the parameter
  - Remap to Parameter → dropdown redirects all uses to different parameter — affects transitions, VRC behaviours, AAP clip bindings, VRC expression parameters and menus (expression parameters/menus only when a GameObject containing them is selected)
  - Delete and Clean → removes parameter from all transitions + parameter list without leaving `Parameter does not exist in Controller` warnings
  - Remove Unused Parameters → deletes any parameter not referenced by transitions, behaviours, or AAP clips
- **Parameter Copy / Paste / Duplicate** *(keyboard only)* — Configurable hotkeys for copying parameters between controllers or duplicating quickly:
  - **Copy** — copies selected parameter (name, type, default value) to clipboard.
  - **Paste** — inserts copy after selected parameter. Auto-renames on collision (`Name 1`, `Name 2`, …).
  - **Duplicate** — copy + paste in one action.
- **Palettes** — Save and share interface color palettes. **Save Palette** snapshots all current interface colors (Primary, Secondary, Accent, and all sub-colors) into a named slot. Each saved slot shows an editable name field, a row of color swatches (click swatches to apply that palette), a **Copy** button that copies an encoded string to clipboard, and **−** to delete. To import a shared palette: paste the encoded string into the text field at the bottom → click **Apply**.
- **Color Tags** — Named tags with custom colors for visually categorizing states and transitions. Each tag has a name field and a color picker. Click `+ Add Tag` to create, `-` to remove. Applied via right-click context menu **Tag** submenu on states or transitions. Tagged state nodes show a thin colored strip above them; tagged transitions use the tag color for their arrow. Tag name matches `AnimatorState.tag`; transition tag stored in `AnimatorStateTransition.name`.
- **Frames** — Enables custom Frames feature ([see Frames](#frames)).

#### Compatibility

Disable individual Harmony patches if they conflict with other tools.

> [!WARNING]
> On Unity start, conflicting patches auto-disable until manually re-enabled.

> [!CAUTION]
> Editor lockup recovery: **YGDR → Animator Editor → Emergency: Unpatch All**. Use only as last resort — disables all features until manual re-enable.

> [!IMPORTANT]
> Editor patch guard recovery: **YGDR → Animator Editor → Reset All Feature Prefs (Recovery)**. Renables All patches by resetting false registry flags.
---

## Layer Panel Enhancements

Extends built-in layer list in Animator window.

### Layer Index

Each layer row displays its zero-based index as small gray text in the bottom-left corner of the row (standard mode only). Useful for referencing layers in VRC Animator Layer Control behaviours.

### Layer Right-Click Context Menu

Right-click any layer row to access:

- **Copy Layer** — Copies layer (states, transitions, frames) to clipboard. Also available via configurable Copy hotkey.
- **Paste Layer** — Pastes copied layer as new layer below current. Cross-controller paste auto-adds referenced parameters to destination. Also available via configurable Paste hotkey.
- **Paste Layer Settings** — Applies only layer properties (avatar mask, blend mode, weight, IK pass, sync settings) from clipboard. Does not replace states.
- **Duplicate Layer** *(keyboard only)* — Configurable Duplicate hotkey immediately copies and pastes the current layer in one action.
- **Delete Layer** — Removes layer.
- **Create Template** *(visible when Layer Templates enabled)* — click opens parameter-mapping window. Saves current layer as user template. Seperate new layer name with `.` or `/` to create submenu heirarchy

### Layer Templates

When **Layer Templates** enabled in Settings, `+` button becomes a dropdown:

- **New Layer** — Creates blank layer → immediately enters rename mode.
- **Package templates** — Listed directly → click opens parameter-mapping window → import template.
- **User/ templates** — User-saved templates under `User/` submenu.
- **Delete User Template/** — Removes user template + associated clips (with confirmation).

Selecting a template opens parameter window to review and remap parameters before import.

### Toggle Layer Creator

Creates a complete bool-toggle layer setup in one step. Accessible via the **Toggle** button in the Layer panel.

1. Set **Parameter** name and **Layer Name** (layer name auto-mirrors parameter name unless manually edited).
2. Toggle **Write Defaults** for the generated states.
3. Drag one or more GameObjects from the Hierarchy into the objects list.
4. Per object, choose which component bindings to include — buttons appear only when the component exists on the object:

| Button | Animates |
|---|---|
| Object | `GameObject.m_IsActive` |
| Renderer | `Renderer.m_Enabled` |
| Particle | `ParticleSystem.m_Enabled` |
| Audio | `AudioSource.m_Enabled` |
| Light | `Light.m_Enabled` |
| PhysBone | `VRCPhysBone.m_Enabled` *(VRC SDK only)* |

**Blendshape** button appears when the object has a `SkinnedMeshRenderer` with blendshapes. Click to expand per-shape sub-rows. Each row shows the shape name and **Off** / **On** float fields (0–100). Click `+` at the bottom of the expanded section → dropdown lists all available shapes not yet added. Click `−` to remove a shape row.

Click **Create** → generates: Bool parameter (if not present), new layer (weight 1), `Off` and `On` states with instant transitions and corresponding animation clips saved alongside the controller.

---

## Graph Window Enhancements

Patches built-in Animator window graph view. Works seamlessly with Unity native controls.

### Mouse Interactions

#### Right-Click Drag → Create Transition

Right-click and drag from a state node, AnyState, or Entry → release on a destination to create a transition instantly.

| Source | Valid Destinations |
|---|---|
| State node | State, Sub-State Machine, Exit |
| AnyState | State only (Exit not supported) |
| Entry | State, Sub-State Machine (Exit not supported) |

Drag activates after moving 8px. Preview line follows cursor while dragging. Releasing on empty space cancels. Fully undoable.

#### Double-Click Empty Space → Create State

Double-click empty space → instantly creates new `AnimatorState` at cursor. State centered on click position → assigned dummy clip.

#### Drag-Drop Multiple Animation Clips

Drag multiple clips from Project window → drop onto graph → each clip creates new state.

#### Drag-Drop Clips onto State nodes

Drag Animation clips from assets folder onto state nodes to apply clip motion to them.

### Context Menus

#### State Node Context Menu

Right-click a state node:

- **Set Clip Loop Time** — Toggle loop time on all clips used by selected states.
- **Pack into Sub-State Machine** — Select 2+ states → right-click → groups into new sub-state machine. Node positions preserved within bounding box. Fully undoable.
- **Select Transitions** — Submenu: all incoming / outgoing / shared transitions for selected nodes.
- **Copy / Paste Behaviors** — Copy `StateMachineBehaviour(s)` → paste onto other states. Menu shows all 7 supported types: Param Drivers, Play Audio, Tracking Control, Locomotion Control, Animator Layer Control, Playable Layer Control, Temporary Pose Space.
- **Tag** — Submenu listing all Color Tags defined in Settings. Click a tag → applies it to all selected states (sets `AnimatorState.tag`) and any selected transitions simultaneously. Toggle behavior: if all selected objects already carry that tag, clicking removes it. **Remove Tags** at the bottom of the submenu clears tags from all selected states and transitions at once. Tagged state nodes show a colored strip above them on the graph.
- **Multi-Transition** — Select source node → click menu item → select other nodes → invoke menu item again → creates transitions from source to all destinations. AnyState or Entry can be source (right-click either → Multi-Transition, then select destinations). Exit can be destination (select states as source, then select Exit node as destination in phase 2). AnyState → Exit and Entry → Exit are not supported.

##### Pack / Unpack Diagram

```
BEFORE PACK                          AFTER PACK
┌─────────────────────────┐          ┌─────────────────────────┐
│  Layer                  │          │  Layer                  │
│  ┌────┐  ┌────┐  ┌────┐ │   →      │  ┌────────────────────┐ │
│  │ A  │→ │ B  │→ │ C  │ │          │  │ NewSubSM           │ │
│  └────┘  └────┘  └────┘ │          │  │ ┌──┐ ┌──┐ ┌──┐     │ │
│                         │          │  │ │A │→│B │→│C │     │ │
└─────────────────────────┘          │  │ └──┘ └──┘ └──┘     │ │
                                     │  └────────────────────┘ │
                                     └─────────────────────────┘
```

##### Multi-Transition Diagram

```
Step 1: Right-click source        Step 2: Select dests + invoke again

                ┌─────┐                    ┌─────┐
                │  A  │ (source)           │  A  │
                └─────┘                    └──┬──┘
                                       ┌──────┼──────┐
       ┌─────┐  ┌─────┐  ┌─────┐       ▼      ▼      ▼
       │  B  │  │  C  │  │  D  │    ┌─────┐┌─────┐┌─────┐
       └─────┘  └─────┘  └─────┘    │  B  ││  C  ││  D  │
                                    └─────┘└─────┘└─────┘
```

#### AnyState / Entry / Exit Node Context Menu

Right-click AnyState, Entry, or Exit node:

- **AnyState** — When AnyState is selected and Multi-Transition is invoked, AnyState becomes the source. Click destination states, then invoke Multi-Transition again to create transitions from AnyState to all selected destinations. AnyState → Exit is not supported (menu item disabled).
- **Entry** — **Select All Outgoing Transitions** — selects all entry transitions in the current state machine. Entry can also be used as a Multi-Transition source: invoke Multi-Transition with Entry selected → click destination states → invoke again to create entry transitions to all selected destinations. Entry → Exit is not supported (menu item disabled).
- **Exit** — **Select All Incoming Transitions** — selects all transitions in the current layer (including nested sub-state machines) whose destination is Exit.

#### Sub-State Machine Node Context Menu

Right-click sub-state machine node:

- **Unpack Sub-State Machine** — Moves all states + transitions back into parent → retains positions and transitions → removes empty sub-state machine. Fully undoable.

#### Transition Arrow Context Menu

Right-click directly on selected transition arrow:

- **Tag** — Submenu listing all Color Tags defined in Settings. Click a tag → applies it to all selected transitions (stored in `AnimatorStateTransition.name`). Toggle behavior: removes tag if all selected transitions already carry it. Tagged transitions use the tag color for their arrow display, overriding the default transition overlay color.
- **Reverse Transitions** — Creates new transition from destination → source with inverse conditions (`Equal` → `NotEqual`).
- **Redirect Transitions** — Enters redirect mode (bottom bar shows `Redirect Transitions — click destination`). Click a destination state → copies selected transitions to that destination, retaining all properties.
- **Replicate Transitions** — Enters replicate mode (bottom bar shows `Replicate Transitions — click sources`). Click source states → copies selected transitions onto those sources, retaining original destinations and all properties.
- **Delete All Transitions in Layer** — Deletes all transitions in current layer. Excludes sub-state machines and parent layers when inside a sub-state machine.

### Modes

#### Copy-Paste Transitions

Select one or more transitions → <kbd>Ctrl</kbd>+<kbd>C</kbd> to copy. Click source → <kbd>Ctrl</kbd>+<kbd>V</kbd> → click destination → transitions paste with all conditions intact. Visual preview shows landing position. <kbd>Esc</kbd> cancels. Bottom bar shows `Paste N Transitions`.

> [!NOTE]
> Copied transitions are also the source for Seeded Fan and Seeded Multi-Transition. Copy transitions first before using either seeded mode.

#### Chain Transition Mode

<kbd>Ctrl</kbd>+<kbd>Double-click</kbd> on state node:

1. Click destination node → transition created.
2. Continue clicking destinations → chain more transitions from previous node.
3. Press <kbd>Esc</kbd> to exit.

Preview line follows cursor while active. Bottom bar shows `Chain Mode`.

```
Click 1: A    Click 2: B       Click 3: C               Esc
┌───┐         ┌───┐  ┌───┐     ┌───┐  ┌───┐  ┌───┐     done
│ A │   →     │ A │→ │ B │ →   │ A │→ │ B │→ │ C │
└───┘         └───┘  └───┘     └───┘  └───┘  └───┘
```

> [!TIP]
> Combine new node double click with Chain Mode transitions to rapidly build framework state machines.

#### Fan Transition Mode

<kbd>Shift</kbd>+<kbd>Double-click</kbd> on state node:

1. Click destination node → transition created from source.
2. Continue clicking destinations → each creates another transition from the same source.
3. Press <kbd>Esc</kbd> to exit.

Preview line follows cursor while active. Bottom bar shows `Fan Mode`.

```
Click 1: B    Click 2: C         Esc
              ┌───┐  ┌───┐       done
┌───┐  ┌───┐  │ A │→ │ B │
│ A │→ │ B │  │   │  └───┘
└───┘  └───┘  │   │→ ┌───┐
              └───┘  │ C │
                     └───┘
```

> [!TIP]
> Use Fan Mode to quickly wire one hub state (e.g. idle) to many destinations in one pass.

##### Seeded Fan Mode

Requires transitions copied to clipboard first. While in Fan Mode, press <kbd>Ctrl</kbd>+<kbd>V</kbd> to seed clipboard transitions. Each click pastes copied transitions (all conditions and settings preserved) instead of creating a blank one. Bottom bar shows `Fan Mode : Seeded`. Press <kbd>Ctrl</kbd>+<kbd>V</kbd> again to toggle back to blank.

##### Seeded Multi-Transition

Requires transitions copied to clipboard first. While Multi-Transition is pending (bottom bar shows `Multi Transition — click destination`), select destination nodes then press <kbd>Ctrl</kbd>+<kbd>V</kbd> to immediately complete the operation using clipboard transitions instead of blank ones. No second click required. Applies to all destination types: state → state, AnyState → state, and state → Exit. All copied conditions and settings are preserved.

### Inline Renaming

#### <kbd>F2</kbd> — States, Sub-State Machines, Blend Tree Nodes, Pamameters, Layers, Frames

Select node → <kbd>F2</kbd> → rename directly on graph. <kbd>Enter</kbd> confirms, <kbd>Esc</kbd> cancels.

**Parameter Sibling Rename** — When renaming a VRC PhysBone, Contact, or Raycast parameter (detected by known suffixes), a dialog appears offering to batch-rename all sibling parameters sharing the same prefix to match the new name. Skipping renames only the selected parameter and updates all its references.

#### <kbd>F3</kbd> — Animation Clips & Blend Tree Leaves, Frame Comments

Select state, blend tree leaf, or Frame → <kbd>F3</kbd> → rename clip assigned to node. Rename field appears on graph → asset updates in Project. <kbd>Enter</kbd> confirms, <kbd>Esc</kbd> cancels.

---

## Blend Tree Enhancements

### Drag-Drop Animation Clips

Drag clips from Project → drop onto blend tree node:

- **Leaf node** (existing clip) → replaces clip.
- **Blend tree node** → adds new child nodes with dropped clips.

### Drag-Reparent

Drag blend tree node from one parent to another. Motion, threshold, other values preserved. Works across blend tree nodes in same graph. Eligible parents highlighted green

### Copy-Paste Nodes

<kbd>Ctrl</kbd>+<kbd>C</kbd> / <kbd>Ctrl</kbd>+<kbd>V</kbd> — copy blend tree node (full subtree if itself a blend tree) → paste onto new parent in same or different blend tree. Deep-copies entire subtree. <kbd>Esc</kbd> cancels pending paste. Also available in right-click context menu.

### Node Type Color

Blend tree node titles use custom colors to distinguish blend tree vs clip. Colors configurable in Settings.

### Blend Tree Templates

Save and reuse blend tree structures. Right-click any blend tree node in graph:

- **Save as Template** → names and saves current blend tree (structure + parameters) as reusable asset.
- **Import Template** → submenu lists saved templates. Selecting one opens parameter remap window before importing into the current blend tree.

Template names support `.` as separator → displays as nested submenus in Import Template list (e.g. `VRC.Toggle` appears under `VRC/Toggle`).

---

## Bottom Bar

Graph bottom bar displays:

| Position | Content |
|---|---|
| Left | Selected states/transitions count |
| Center | Active mode label (normal, chain, fan, paste, etc.) |
| Right | Controller path (clickable → pings controller in Project) |

Chain mode, fan mode, copy-paste mode, and other temporary modes update label in real time.

---

## Frames

Visual sticky notes for animator graph → organize and annotate layers. Derived from Substance Designer frames.

Frames stored as hidden sub-assets inside each controller → visible to all users with tool. Deleted frames garbage-collected at Unity domain reload/open → keeps controllers clean.

**Creating frames** — Right-click empty graph → **Create Frame**. If nodes selected at creation → frame auto-fits around them.

**Deleting all frames** — Right-click empty graph → **Delete All Frames** (excludes sub-state machines and parent layers when inside a sub-state machine).

Lock/unlock by clicking lock icon in upper-left corner. Resize by selecting and dragging square handles at sides/corners. Multiple frames can be selected, moved, and copy-pasted at once.

Frames can nest. If a frame with **Move Contents** on has a smaller frame sitting inside it (and that smaller frame is higher in Z-Layer), dragging the bigger frame carries the smaller one along with it, keeping its position inside unchanged — like moving a folder with files in it.

Locked frames never move, dragged or carried. Locking/unlocking a frame also locks/unlocks any frame nested inside it, so you don't have to lock them one by one.

### Frame Context Menu

Right-click a frame:

- **Rename** — Rename frame title. Also available via <kbd>F2</kbd> on selected unlocked frame.
- **Edit Comments** — Add multi-line comments to frame body. Also via <kbd>F3</kbd> on unlocked frame.
- **Color** — Color picker for frame color + transparency.
- **Z-Layer** *(shown as `z#` in top-right corner)* — Frame stacking shortcuts:

  | Action | Shortcut |
  |---|---|
  | Move to Top | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>]</kbd> |
  | Move Up | <kbd>Ctrl</kbd>+<kbd>]</kbd> |
  | Move Down | <kbd>Ctrl</kbd>+<kbd>[</kbd> |
  | Move to Bottom | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>[</kbd> |

- **Fit to Selected** — Resizes frame to fit currently selected nodes.
- **Move Contents** — When enabled, nodes inside frame bounds move with frame, and any nested frames come along too.
- **Lock** — Prevents move/resize. Also locks any nested frames.
- **Delete** — Deletes frame.

> [!NOTE]
> Frames are copied automatically when a layer is copy-pasted, including cross-controller pastes.

---

## Graph Node Analysis

Right-click empty graph:

- **Unreachable States** — Highlights states with no incoming transitions or only invalid ones. Sub-state machines highlighted when containing unreachable states.
- **Terminal States** — Highlights states with no valid exit: only invalid exit transitions, only self-transitions, or part of group isolated from reaching any other state.

---

## Constraint Converter

Adds **Convert to** menu items to the right-click context menu of Unity and VRC constraint components in the Inspector.

Supported conversions:

| From | To |
|---|---|
| Unity PositionConstraint | RotationConstraint, ParentConstraint |
| Unity RotationConstraint | PositionConstraint, ParentConstraint |
| Unity ParentConstraint | PositionConstraint, RotationConstraint |
| VRC PositionConstraint | VRC RotationConstraint, VRC ParentConstraint |
| VRC RotationConstraint | VRC PositionConstraint, VRC ParentConstraint |
| VRC ParentConstraint | VRC PositionConstraint, VRC RotationConstraint |

Copies sources, weights, and locked/active state to the new component. Removes the old component. Fully undoable.

> [!IMPORTANT]
> VRC constraint conversions require VRChat SDK installed.

---

## Keyboard Shortcuts

Most graph shortcuts are rebindable via **Settings → Keybinds**. Defaults shown below.

### Fixed Shortcuts

| Shortcut | Action |
|---|---|
| <kbd>F2</kbd> | Rename state / sub-state machine / blend tree node / frame / layer / parameter |
| <kbd>F3</kbd> | Rename clip / blend tree leaf / frame comments |
| <kbd>Ctrl</kbd>+<kbd>Double-click</kbd> node | Enter Chain Transition Mode |
| <kbd>Shift</kbd>+<kbd>Double-click</kbd> node | Enter Fan Transition Mode |
| <kbd>Esc</kbd> | Exit chain / fan / paste / rename / pending mode |
| <kbd>Enter</kbd> | Confirm inline rename; complete Multi Transition with current selection as destinations |
| <kbd>Ctrl</kbd>+<kbd>]</kbd> | Frame: move up |
| <kbd>Ctrl</kbd>+<kbd>[</kbd> | Frame: move down |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>]</kbd> | Frame: move to top |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>[</kbd> | Frame: move to bottom |

### Rebindable Shortcuts (defaults)

| Default | Action |
|---|---|
| <kbd>I</kbd> | Select transitions pointing to selected states or Exit |
| <kbd>O</kbd> | Select outgoing transitions of selected states / AnyState |
| <kbd>P</kbd> | Select both incoming and outgoing transitions of selected states |
| <kbd>Ctrl</kbd>+<kbd>A</kbd> | Select all Nodes |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>A</kbd> | Select all Transitions |
| <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copy selected transitions / blend tree nodes / frames / layer / parameter |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> | Paste transitions / blend tree nodes / frames / layer / parameter |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> (in Fan Mode) | Toggle Seeded Fan — paste clipboard transitions instead of blank |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> (in Multi-Transition) | Complete with seeded transitions using current selection as destinations |
| *(unbound)* | **Duplicate** — copy + immediately paste selected layer, parameter, or node |
| *(unbound)* | Chain Transition Mode — keyboard alternative to Ctrl+double-click |
| *(unbound)* | Fan Transition Mode — keyboard alternative to Shift+double-click |
| *(unbound)* | **Multi Transition** — Phase 1: set selected states (or AnyState) as sources; Phase 2: press again with destinations selected (or Exit) to execute |
| *(unbound)* | **Reverse Transitions** — create reversed + negated copy of each selected transition |
| *(unbound)* | **Replicate Transitions** — copy selected transitions onto new source states, keeping original destinations |
| *(unbound)* | **Redirect Transitions** — copy selected transitions to new destination states, keeping original sources |


---

## Bug Fixes & Compatibility

- Reordering layers no longer switches graph from selected layer view.
- Arrow keys to move between layers
- Undoing parameter rename no longer triggers `Parameter does not exist in Controller` warnings.
- <kbd>F2</kbd> renames selected layer or parameter directly in respective list.
- Renaming a parameter affects AAP clip bindings, VRC behaviours, expression parameters, and menus. Expression parameters and menus are only updated when a GameObject containing them is selected in the scene.

---

## Undo Safety

All operations within controller (pack, unpack, state moves, transition creation, layer copy-paste, VRC parameter edits, etc.) fully undoable. Tool uses Unity's `Undo` system at all system boundaries → properly registers object creation and destruction.

Node Colors, Initial patching hook methods, EditorPrefs settings mechanism based on Ratz by ([rrazgriz](https://github.com/rrazgriz/RATS))