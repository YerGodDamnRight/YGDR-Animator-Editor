# Changelog

## v1.2.1
#### Added
- Added transitions tab animation preview
- Added keyframe ops Jitter/Offset/Remap
- Added transition defaults can transiton to self (anystate)

#### Fixed
- Fixed native Unity BlendTree lag on large graphs
- Added # field to animator/playable layer control state behaviors

## v1.2.0
#### Added
- Added network sync merge network states toggle
- Added transition line colors gradient mode
- Added network sync create backup layer toggle
- Added keyframe ops
- Added custom transition routing feature
- Transition conditions reorderable
- Added transition tags delete button
- Added changelog window
- Split network sync transition duration, offset and exit time

#### Fixed
- Param driver switch copy mode src/dest fix
- Animation clip dropdown not changing after exiting play mode
- Shared behavior paste fix
- Fixed controller clean fn
- Fixed native state parameter reset bug
- Fixed menu type param sliders
- Fixed network sync created params bug
- Fixed network sync remote default state race condition

## v1.1.0
#### Added
- Locking animation tab, prevents clips from being changed on same animator
- Support menu editing from controller tab
- Toggle option inspector mode, only shows tab relevant to selected
- State defaults write defaults merge to transition section

#### Fixed
- Move settings to collapsible footer, shared behaviors collapsed into add behavior dropdown
- UI Toolkit conversion

## v1.0.7
#### Added
- Blend tree param remap function
- Param driver flip src/dest function
- Transitions filter select
- Shared transitions match by options
- Param list syncable button toggle
- Drag mass saved/sync/set syncable toggles in param list
- Shared behavior paste modes added

#### Fixed
- Param name truncation
- Param duplicate warning fix
- New param add selects next
- Changed all param dropdowns to advanced type
- Removed self transition default option toggle
- Controllers now update when in scene view

## v1.0.6
#### Added
- Added multi instance state behaviors and ordering
- Network sync unique driver toggle
- Frames can move other frames and un/lock them
- Parameter list to param object syncing
- Network sync copy state modifiers
- Added blendtree parameters to find all uses window

#### Fixed
- Remove unused parameters fix

## v1.0.5
#### Added
- Create new clip cache output folder
- Create new clip added to base layer
- Param list saved/default values
- Added saved flags and default value linking for parameters list

#### Fixed
- Fixed crashguard stuck feature flags

## v1.0.4
#### Added
- Clip menu nesting add new clip button
- Clip menu nesting delimiter options

## v1.0.3
#### Added
- Chain/fan modes from anystate/entry

#### Fixed
- Remove frame data from controllers instead of leaving empty object
- Rename sibling cancel fix

## v1.0.2
#### Added
- User color palettes
- Copy/paste/duplicate layer/params list
- Toggle creator added light/physbones/blendshapes
- Right click drag to create transition
- Clip remapper added GO autofill fields
- Clip menu nesting added advanced searchable dropdown
- Added layer index indicators

#### Fixed
- Remap to parameter dropdown fix
- Minor UI adjustments
- Layer name copy/paste bug
- Changed doubleclick new state to alt+doubleclick

## v1.0.1
#### Added
- Duplicate condition warning on range
- Sync/unsync icons as toggle buttons
- Entry transitions mass editing/labels
- Parameter usage from multiplier/motion time/mirror

#### Fixed
- Remove package deps
- Param list comp indicator fix

## v1.0.0
#### Added
- Configurable hotkeys
- Localization (JP/KR/FR/DE/ES/ZH)
- Blend tree templates w/ sub-asset export
- Footer rework
- Quick toggle creator
- Tags + clear option
- Hotkey: list transition conditions
- Write defaults collapsible
- Node overlay: clip frames/duration
- VRCFury GO sync
- GNU GPL license
- Standalone (no VRCSDK/MDV deps)
- Duplicate node name increment
- Layer select/focus/arrow nav
- Layer F2 rename (compact mode)
- Empty params: check drivers + blend trees
- Param list cache
- Anystate/exit in/out selection
- Param type mismatch conversion
- Find uses: includes param drivers
- Param driver rename source field
- Find effecting: scoped to selected GO
- Frame create/delete always visible
- Copy/paste transitions -> anystate/exit snap
- Param rename resets auto-convert range
- Motionless state warnings
- GO clip edit focus

## v0.9.7
#### Added
- Added seeded multi/fan transitions

#### Fixed
- Fixed shared transitions changing mixed values on mode change

## v0.9.6
#### Added
- Add transition incoming/outgoing selection from anystate/entry/exit nodes
- Ctrl+A select all nodes / Ctrl+Shift+A select all transitions
- Multi transition from/to anystate/exit nodes
- Unused parameter indicators
- Added copy/pasting of state behaviors
- Changing parameter names, carries over to Game Objects
- Added double click node to make new transitions
- Added parameter context item to add parameter to selected clip as AAP

## v0.9.5
#### Added
- MDV integration
- Transition anystate/entry/exit application
- Footer links

#### Fixed
- Layer indicator performance caching
- Parameter reorder/index/linked name fix

## v0.9.4
#### Added
- 3 tier crash safety
- Compatibility toggles
- Clip remapper
- Analysis highlight
- Parameter budget
- Component param sibling naming

#### Fixed
- Codebase restructure
- Node placement bug fixes

## v0.9.3
#### Added
- Find object uses
- Parameter icons
- Effecting parameters
- Frames feature
- Layer templates
- Control toggles

#### Fixed
- Grid custom background fixes
- Shared state behavior fixes

## v0.9.2
#### Added
- VRC sync icons
- Controller sub assets section
- Interface custom colors
- Find uses window

#### Fixed
- Visual overhaul

## v0.9.1
#### Added
- Transition custom colors

## v0.8.0
#### Added
- Initial release
