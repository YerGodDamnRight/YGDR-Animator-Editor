
<h1 style="text-align: center;">YGDR Animator Editor</h1>


<p align="center"><strong>Multi-transition editing · Inline rename · Blend tree enchancements · VRC integration · Parameter management</strong></p>

<p align="center">
  <a href="https://unity.com"><img src="https://img.shields.io/badge/Unity-2022.3_LTS-black?logo=unity" alt="Unity"></a>
  <a href="https://creators.vrchat.com/sdk/"><img src="https://img.shields.io/badge/VRChat_SDK-3.x-blue?logo=vrchat" alt="VRChat SDK"></a>
  <a href="https://github.com/pardeike/Harmony"><img src="https://img.shields.io/badge/Harmony-2.x-orange" alt="Harmony"></a>
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/License-GPLv3-green" alt="License"></a>
  <a href="https://claude.ai"><img src="https://img.shields.io/badge/Built_with-Claude-blueviolet?logo=anthropic" alt="Built with Claude"></a>
</p>

<p align="center">
  <a href="#installation">Install</a> ·
  <a href="#features">Features</a> ·
  <a href="#usage">Usage</a> ·
  <a href="./Packages/com.ygdr.animator/YGDR%20Animator%20Editor%20Docs.md">Docs</a> ·
  <a href="https://github.com/YerGodDamnRight/YGDR-Animator-Editor/releases/latest">Releases</a> ·
  <a href="HOW_IT_WORKS.md">Architecture</a> ·
  <a href="LICENSE.md">License</a>
</p>

---

A Harmony-based Unity Editor extension that significantly expands the built-in Animator window. Designed for VRChat avatar creators managing complex animator controllers.

---

## Multi-Transition Editing

Select multiple transitions at once — mass-edit timing, conditions, and interruption settings simultaneously.

![Multi-Transition Editing](./Readme-gifs/multi_transition.gif)

---

## Multi-State Editing

Batch-edit Write Defaults, motion fields, tags, and state behaviours across multiple selected states at once.

![Multi-State Editing](./Readme-gifs/mass_edit.gif)

---

## Features

<details>
<summary><strong>Graph Enhancements</strong></summary>

- Double-click empty graph to create states
- Ctrl+A select all nodes, Ctrl+Shift+A select all transitions
- Context menus on AnyState/Exit nodes
- Frame and annotation overlays

![Graph Enhancements](./Readme-gifs/create_frame.gif)

</details>

<details>
<summary><strong>Chain & Fan Transition Modes</strong></summary>

Quickly wire sequential chains or radial fans of transitions without manually connecting each node.

![Chain and Fan Modes](./Readme-gifs/chain_fan_mode.gif)

</details>

<details>
<summary><strong>Copy & Paste / Replicate Transitions</strong></summary>

Copy transitions between states, replicate and redirect in bulk.

![Replicate and Redirect](./Readme-gifs/replicate_redirect.gif)

</details>

<details>
<summary><strong>Inline Rename (F2 / F3)</strong></summary>

Rename states, sub-state machines, and animation clips directly in the graph.

![Rename](./Readme-gifs/rename.gif)

</details>

<details>
<summary><strong>Blend Tree Enhancements</strong></summary>

Drag-to-reparent nodes, clip drag-drop onto nodes, inline rename.

![Blend Tree](./Readme-gifs/blendtree.gif)

</details>

<details>
<summary><strong>Layer & Controller Tools</strong></summary>

Layer templates, copy/paste, reorder support, pack/unpack utilities.

![Pack and Unpack](./Readme-gifs/pack_unpack.gif)

</details>

<details>
<summary><strong>VRC Integration</strong></summary>

Parameter Driver editor, VRC sync cache, network sync pattern support, full StateMachineBehaviour editor.

![Network Sync](./Readme-gifs/network_sync.gif)

</details>

<details>
<summary><strong>Parameter Tools</strong></summary>

- Reorder, type conversion, unused parameter indicators
- Add as AAP, find usages across layers
- Find Usage Window: search which states, transitions, and layers reference a given parameter or clip

</details>

---

## Installation

1. Get/Rate the package from [Gumroad](https://yergoddamnright.gumroad.com/l/anim-editor), [Jinxxy](https://jinxxy.com/YerGodDamnRight/anim-editor), or [Booth](https://yergoddamnright.booth.pm/)
2. Import via the [VPM repo](https://yergoddamnright.github.io/YGDR-VPM-Listing/) **or** download the latest `.unitypackage` from the [releases page](https://github.com/YerGodDamnRight/YGDR-Animator-Editor/releases/latest) and import into your project

**Requirements:**
- Unity 2022.3 LTS
- VRChat SDK 3.x *(optional — required for VRC features)*
- MDViewer 1.1.0 *(optional — enables built-in help docs)*

---

## Usage

Open via **YGDR → YGDR Animator Editor** in the Unity menu bar, or through the Animator window context menu.

Select states or transitions in the Animator graph — the editor window updates automatically.

---

## Documentation

[Full Feature Docs](./Packages/com.ygdr.animator/YGDR%20Animator%20Editor%20Docs.md) · [How It Works — Patches & Architecture](HOW_IT_WORKS.md)

---

## License

[GNU General Public License v3.0](LICENSE.md)

---

## 3rd Party Credits

[3rd Party Notices](./Packages/com.ygdr.animator/THIRD_PARTY_NOTICES.md)

---

<p align="center"><sub>by <a href="https://github.com/YerGodDamnRight">YerGodDamnRight</a> · Developed with AI assistance (<a href="https://claude.ai">Claude</a> / Anthropic)</sub></p>
