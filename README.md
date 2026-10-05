# Meshia Mesh Simplification (Synthos Edition)

A high-performance, Burst-accelerated mesh decimation library for Unity and VRChat. 

This repository is a customized edition of [Ram.Type-0's Meshia](https://github.com/RamType0/Meshia.MeshSimplification) (under the MIT License), tailored specifically for **VRChat World / Scene optimization**, batch environment processing, and standalone usage with tools like **Synthos Scene Optimizer**.

---

## ⚡ What Was Changed?

1. **Independent UV Barycentric Interpolation (`UseBarycentricCoordinateInterpolationForUV`)**
   - **In Stock Meshia:** Barycentric interpolation was all-or-nothing—forcing surface normals, tangents, and UVs to all interpolate together, which frequently distorted vertex normals and lighting on flat or hard-edged surfaces.
   - **In Synthos Edition:** Normals and tangents can use standard linear interpolation while texture coordinates (UV0–UV7) and vertex colors independently use barycentric interpolation. This preserves lightmaps, trim sheets, and texture mappings with zero shading distortion.

2. **Watertight Edge Preservation (`PreserveBorderEdges = true`)**
   - **In Stock Meshia:** `PreserveBorderEdges` defaulted to `false`, causing holes and seam splits on open meshes and modular building pieces.
   - **In Synthos Edition:** `PreserveBorderEdges` defaults to `true` to keep environmental and scene geometry watertight.

3. **Batch Scene Processing Support**
   - Optimized for multi-mesh scene decimation jobs via `MeshSimplifier.SimplifyBatch()` without requiring interactive avatar inspector wrappers.

---

## 🚫 What Is No Longer Needed?

- **NDMF (Non-Destructive Modular Framework) is NO LONGER required:** The original package was hard-coupled to the avatar-only NDMF pipeline. All NDMF dependencies have been cleanly removed so the tool can run standalone on scenes, worlds, or standalone C# pipelines.
- **`com.anatawa12.custom-localization-for-editor-extension` is NO LONGER required:** Stripped unnecessary third-party localization packages.

---

## 📦 Installation via VPM (VRChat Creator Companion)

Add the Synthos package repository to VCC / ALCOM:
```
https://grimreaper35487.github.io/Synthos-VRC-Packages/index.json
```
Then add **Meshia Mesh Simplification (Synthos Edition)** to your project, or install **Synthos Scene Optimizer** (which will automatically pull this in as a dependency).

---

## 💻 C# Usage

```csharp
using Meshia.MeshSimplification;
using System.Collections.Generic;
using UnityEngine;

// 1. Configure options (PreserveBorderEdges is true by default)
var options = MeshSimplifierOptions.Default;
options.UseBarycentricCoordinateInterpolationForUV = true; // High-accuracy UV preservation

// 2. Set target reduction
var target = new MeshSimplificationTarget
{
    Kind = MeshSimplificationTargetKind.RelativeVertexCount,
    Value = 0.5f // Reduce by 50%
};

// 3. Batch Simplify (Burst accelerated)
var batch = new List<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options)>
{
    (myMesh, target, options)
};

MeshSimplifier.SimplifyBatch(batch);
```

---

## 📜 Credits & License

* **Original Library:** Developed by [Ram.Type-0](https://github.com/RamType0/Meshia.MeshSimplification) under the [MIT License](LICENSE.md).
* **Modifications & World Optimization:** Maintained by [Synthos](https://github.com/GRIMREAPER35487).
